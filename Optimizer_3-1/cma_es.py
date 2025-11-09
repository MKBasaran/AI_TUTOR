# cma_es.py
"""
Separable CMA-ES (diagonal covariance) — stdlib-only, task-3 ready.
Implements evolution paths (pc, ps), rank-1 and rank-μ covariance updates,
and step-size control. External interface:
  - CMAState(mean: List[float], sigma: float)
  - cma_es_step(state, lam, fitnesses)
  - per_dim_step_scales(state)  # for sampling
"""
from __future__ import annotations
from dataclasses import dataclass
from typing import List, Tuple, Optional
import math

def _zeros(n: int) -> List[float]: return [0.0] * n
def _ones(n: int) -> List[float]:  return [1.0] * n
def _add(a, b): return [x + y for x, y in zip(a, b)]
def _mul(a, s): return [x * s for x in a]
def _hadamard(a, b): return [x * y for x, y in zip(a, b)]
def _sqrt(a): return [math.sqrt(x) for x in a]
def _norm(a): return math.sqrt(sum(x * x for x in a))

@dataclass
class CMAState:
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

def _init_strategy(st: CMAState, dim: int, lam: int) -> None:
    mu = st.mu or max(2, lam // 2)
    w = [math.log(mu + 0.5) - math.log(i + 1) for i in range(mu)]
    w_sum = sum(w); w = [wi / w_sum for wi in w]
    mueff = 1.0 / sum(wi * wi for wi in w)

    cs = (mueff + 2) / (dim + mueff + 5)
    damps = 1 + 2 * max(0, math.sqrt((mueff - 1) / (dim + 1)) - 1) + cs
    cc = (4 + mueff / dim) / (dim + 4 + 2 * mueff / dim)
    c1 = 2 / ((dim + 1.3) ** 2 + mueff)
    cmu = min(1 - c1, 2 * (mueff - 2 + 1 / mueff) / ((dim + 2) ** 2 + mueff))
    chiN = math.sqrt(dim) * (1 - 1 / (4 * dim) + 1 / (21 * dim * dim))

    st.mu, st.weights, st.mueff = mu, w, mueff
    st.cs, st.damps, st.cc, st.c1, st.cmu, st.chiN = cs, damps, cc, c1, cmu, chiN
    if st.cov_diag is None: st.cov_diag = _ones(dim)
    if st.pc is None: st.pc = _zeros(dim)
    if st.ps is None: st.ps = _zeros(dim)

def cma_es_step(st: CMAState, lam: int, fitnesses: List[Tuple[float, List[float]]]) -> CMAState:
    if not fitnesses: return st
    dim = len(st.mean); _init_strategy(st, dim, lam)

    fitnesses.sort(key=lambda t: t[0], reverse=True)
    mu = st.mu; w = st.weights  # type: ignore

    m_old = list(st.mean)
    m_new = [sum(w[i] * fitnesses[i][1][j] for i in range(mu)) for j in range(dim)]  # type: ignore

    inv_sigma = 1.0 / st.sigma
    y_w = [(m_new[j] - m_old[j]) * inv_sigma for j in range(dim)]
    invsqrtC = [1.0 / s for s in _sqrt(st.cov_diag)]  # type: ignore

    # ps update (cumulation for sigma)
    ps = st.ps; cs = st.cs; mueff = st.mueff  # type: ignore
    ps = _add(_mul(ps, 1 - cs),
              _mul([y_w[j] * invsqrtC[j] for j in range(dim)],
                   math.sqrt(cs * (2 - cs) * mueff)))  # type: ignore

    st.evals += len(fitnesses)
    hsig_cond = _norm(ps) / math.sqrt(1 - (1 - cs) ** (2 * st.evals / max(lam, 1))) / st.chiN  # type: ignore
    hsig = 1.0 if hsig_cond < (1.4 + 2 / (dim + 1)) else 0.0

    # pc update (cumulation for covariance)
    cc = st.cc; pc = st.pc  # type: ignore
    pc = _add(_mul(pc, 1 - cc),
              _mul(y_w, hsig * math.sqrt(cc * (2 - cc) * mueff)))  # type: ignore

    # Covariance update (diagonal)
    c1 = st.c1; cmu = st.cmu; C = st.cov_diag  # type: ignore
    rank_one = [v * v for v in pc]
    add_one = [c1 * (r1 + (1 - hsig) * cc * (2 - cc) * c) for r1, c in zip(rank_one, C)]

    y2 = [0.0] * dim
    for i in range(mu):  # type: ignore
        xi = fitnesses[i][1]
        yi = [(xi[j] - m_old[j]) * inv_sigma for j in range(dim)]
        for j in range(dim):
            y2[j] += w[i] * yi[j] * yi[j]  # type: ignore
    add_mu = [cmu * v for v in y2]
    decay = [(1 - c1 - cmu) * c for c in C]
    C_new = [d + a1 + am for d, a1, am in zip(decay, add_one, add_mu)]

    # Step-size control
    damps = st.damps
    sigma_new = st.sigma * math.exp((cs / damps) * (_norm(ps) / st.chiN - 1.0))  # type: ignore

    st.mean = m_new
    st.ps = ps
    st.pc = pc
    st.cov_diag = [max(1e-12, c) for c in C_new]
    st.sigma = max(1e-12, sigma_new)
    return st

def per_dim_step_scales(st: CMAState) -> List[float]:
    """Return σ_j = sigma * sqrt(C_jj) for axis-aligned sampling."""
    if st.cov_diag is None:
        return [st.sigma] * len(st.mean)
    return [st.sigma * math.sqrt(v) for v in st.cov_diag]