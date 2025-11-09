"""
Separable CMA-ES (diagonal covariance) — stdlib-only, task‑3 ready.
Implements evolution paths (pc, ps), rank‑1 and rank‑μ covariance updates,
step‑size control (CSA), and exposes a tiny API:
  - CMAState(mean: List[float], sigma: float)
  - cma_es_step(state, lam, fitnesses)
  - per_dim_step_scales(state)

Notes
-----
* "Separable" here means we keep only the diagonal of the covariance matrix C, so
  sampling is axis‑aligned. This reduces cost and is stable for small dimensions.
* Formulas follow Hansen's CMA‑ES (rank‑μ update with evolution paths), adapted
  for diagonal C and pure‑Python readability.
"""
from __future__ import annotations
from dataclasses import dataclass
from typing import List, Tuple, Optional
import math

# -----------------
# Small vector ops
# -----------------
# These helpers avoid external deps (e.g. NumPy) while keeping code legible.

def _zeros(n: int) -> List[float]: return [0.0] * n

def _ones(n: int) -> List[float]:  return [1.0] * n

def _add(a, b): return [x + y for x, y in zip(a, b)]

def _mul(a, s): return [x * s for x in a]

def _hadamard(a, b): return [x * y for x, y in zip(a, b)]

def _sqrt(a): return [math.sqrt(x) for x in a]

def _norm(a): return math.sqrt(sum(x * x for x in a))


# -----------------
# Strategy state
# -----------------
@dataclass
class CMAState:
    """Mutable state for one CMA‑ES run (diagonal covariance).

    Attributes
    ---------
    mean : List[float]
        Current distribution mean (parameter vector).
    sigma : float
        Global step‑size (scalar).
    cov_diag : Optional[List[float]]
        Diagonal of covariance matrix C (per‑dim variance multipliers). Defaults to ones.
    pc, ps : Optional[List[float]]
        Evolution paths for covariance (pc) and step‑size (ps) adaptation.
    mu, weights, mueff : Optional[int | List[float] | float]
        Parent number, recombination weights, and their variance‑effective size.
    cs, damps : Optional[float]
        Cumulation and damping for step‑size control.
    cc, c1, cmu : Optional[float]
        Learning rates for covariance updates (rank‑1 and rank‑μ) and cumulation.
    chiN : Optional[float]
        Expectation of norm of a N(0, I) vector in N dims (for CSA).
    evals : int
        Total number of candidate evaluations seen so far (for hsig schedule).
    """
    mean: List[float]
    sigma: float
    cov_diag: Optional[List[float]] = None
    pc: Optional[List[float]] = None
    ps: Optional[List[float]] = None
    mu: Optional[int] = None
    weights: Optional[List[float]] = None
    mueff: Optional[float] = None
    cs: Optional[float] = None
    damps: Optional[float] = None
    cc: Optional[float] = None
    c1: Optional[float] = None
    cmu: Optional[float] = None
    chiN: Optional[float] = None
    evals: int = 0


# ---------------------------
# Parameter initialisation
# ---------------------------

def _init_strategy(st: CMAState, dim: int, lam: int) -> None:
    """Fill in derived strategy parameters if not already set.

    Uses standard choices from CMA‑ES literature, with log‑linear weights and
    diagonal C. Values are cached in the state so this can be called idempotently
    per step without overhead once filled.
    """
    # Parent set size and recombination weights (log‑linear, positive)
    mu = st.mu or max(2, lam // 2)
    w = [math.log(mu + 0.5) - math.log(i + 1) for i in range(mu)]
    w_sum = sum(w); w = [wi / w_sum for wi in w]
    mueff = 1.0 / sum(wi * wi for wi in w)  # variance‑effective selection mass

    # Step‑size path cumulation and damping (CSA)
    cs = (mueff + 2) / (dim + mueff + 5)
    damps = 1 + 2 * max(0, math.sqrt((mueff - 1) / (dim + 1)) - 1) + cs

    # Covariance path cumulation and learning rates
    cc = (4 + mueff / dim) / (dim + 4 + 2 * mueff / dim)
    c1 = 2 / ((dim + 1.3) ** 2 + mueff)  # rank‑1 update rate
    cmu = min(1 - c1, 2 * (mueff - 2 + 1 / mueff) / ((dim + 2) ** 2 + mueff))  # rank‑μ rate

    # E||N(0, I)|| for dimension dim (used by CSA target length)
    chiN = math.sqrt(dim) * (1 - 1 / (4 * dim) + 1 / (21 * dim * dim))

    # Write into state
    st.mu, st.weights, st.mueff = mu, w, mueff
    st.cs, st.damps, st.cc, st.c1, st.cmu, st.chiN = cs, damps, cc, c1, cmu, chiN
    if st.cov_diag is None: st.cov_diag = _ones(dim)
    if st.pc is None: st.pc = _zeros(dim)
    if st.ps is None: st.ps = _zeros(dim)


# ---------------------------
# One CMA‑ES iteration (ask‑tell)
# ---------------------------

def cma_es_step(st: CMAState, lam: int, fitnesses: List[Tuple[float, List[float]]]) -> CMAState:
    """Perform one update of the CMA‑ES state using evaluated candidates.

    Parameters
    ----------
    st : CMAState
        Current strategy parameters (updated in place and returned).
    lam : int
        Population size used this generation (only for hsig schedule / bookkeeping).
    fitnesses : List[(fit, x)]
        Evaluated candidates (higher fit is better) with *phenotype* coordinates.
        The x vectors are assumed to be in the original parameter space; sampling
        should use per_dim_step_scales() externally.
    """
    if not fitnesses: return st
    dim = len(st.mean); _init_strategy(st, dim, lam)

    # Sort by fitness descending; take top μ parents
    fitnesses.sort(key=lambda t: t[0], reverse=True)
    mu = st.mu; w = st.weights  # type: ignore[assignment]

    # New weighted mean (recombination)
    m_old = list(st.mean)
    m_new = [sum(w[i] * fitnesses[i][1][j] for i in range(mu)) for j in range(dim)]  # type: ignore[index]

    # Transform step to isotropic coordinates (divide by sigma and sqrt of diag C)
    inv_sigma = 1.0 / st.sigma
    y_w = [(m_new[j] - m_old[j]) * inv_sigma for j in range(dim)]
    invsqrtC = [1.0 / s for s in _sqrt(st.cov_diag)]  # type: ignore[arg-type]

    # --- Step‑size path (ps) update (cumulation for sigma) ---
    ps = st.ps; cs = st.cs; mueff = st.mueff  # type: ignore[assignment]
    ps = _add(_mul(ps, 1 - cs),
              _mul([y_w[j] * invsqrtC[j] for j in range(dim)],
                   math.sqrt(cs * (2 - cs) * mueff)))  # type: ignore[index]

    # hsig heuristic: 1 if path length is near expected under random selection
    st.evals += len(fitnesses)
    hsig_cond = _norm(ps) / math.sqrt(1 - (1 - cs) ** (2 * st.evals / max(lam, 1))) / st.chiN  # type: ignore[operator]
    hsig = 1.0 if hsig_cond < (1.4 + 2 / (dim + 1)) else 0.0

    # --- Covariance path (pc) update (cumulation for C) ---
    cc = st.cc; pc = st.pc  # type: ignore[assignment]
    pc = _add(_mul(pc, 1 - cc),
              _mul(y_w, hsig * math.sqrt(cc * (2 - cc) * mueff)))  # type: ignore[arg-type]

    # --- Diagonal covariance update: rank‑1 (pc) + rank‑μ (parents) ---
    c1 = st.c1; cmu = st.cmu; C = st.cov_diag  # type: ignore[assignment]

    # Rank‑1: (pc ⊙ pc) scaled, with correction if hsig == 0 (stalling case)
    rank_one = [v * v for v in pc]
    add_one = [c1 * (r1 + (1 - hsig) * cc * (2 - cc) * c) for r1, c in zip(rank_one, C)]

    # Rank‑μ: accumulate weighted squared steps of top μ individuals
    y2 = [0.0] * dim
    for i in range(mu):  # type: ignore[operator]
        xi = fitnesses[i][1]
        yi = [(xi[j] - m_old[j]) * inv_sigma for j in range(dim)]
        for j in range(dim):
            y2[j] += w[i] * yi[j] * yi[j]  # type: ignore[index]
    add_mu = [cmu * v for v in y2]

    # Combine with decay on old covariance
    decay = [(1 - c1 - cmu) * c for c in C]
    C_new = [d + a1 + am for d, a1, am in zip(decay, add_one, add_mu)]

    # --- Step‑size control (CSA): compare path length to expectation ---
    damps = st.damps
    sigma_new = st.sigma * math.exp((cs / damps) * (_norm(ps) / st.chiN - 1.0))  # type: ignore[arg-type]

    # Commit updates (guard against degeneracy)
    st.mean = m_new
    st.ps = ps
    st.pc = pc
    st.cov_diag = [max(1e-12, c) for c in C_new]
    st.sigma = max(1e-12, sigma_new)
    return st


# ---------------------------
# Sampling helper for callers
# ---------------------------

def per_dim_step_scales(st: CMAState) -> List[float]:
    """Return per‑dimension standard deviations σ_j = sigma * sqrt(C_jj).

    Callers can sample axis‑aligned Gaussians as: x_j ~ N(mean_j, σ_j^2).
    """
    if st.cov_diag is None:
        return [st.sigma] * len(st.mean)
    return [st.sigma * math.sqrt(v) for v in st.cov_diag]