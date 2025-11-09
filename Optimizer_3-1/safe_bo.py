# Stdlib-only Safe Bayesian Optimisation (fast enough for Task 3).
# This module provides two pieces:
#   1) TinyGP  – a small Gaussian Process regression helper (RBF kernel, Cholesky solve),
#   2) SafeBO  – a lightweight, safety-aware Bayesian optimisation loop that proposes
#                new candidates while avoiding regions predicted to be unsafe.
# The code is intentionally dependency-free and tuned for speed/readability over features.

from __future__ import annotations
from typing import List, Tuple, Optional, Dict
import math, random

# ======================================
# TinyGP: a minimal GP for BO use-cases
# ======================================
class TinyGP:
    """A tiny Gaussian Process regressor with RBF kernel.

    The implementation is purposely compact:
      - stores up to `max_points` data points (FIFO eviction) to keep cubic ops bounded,
      - uses Cholesky factorisation to solve K^{-1} y and predict efficiently,
      - assumes zero mean prior and isotropic RBF kernel.

    Parameters
    ----------
    lengthscale : float
        RBF kernel lengthscale (controls smoothness / correlation length).
    noise : float
        Diagonal jitter added to K for numerical stability (also acts as observation noise).
    max_points : int
        Maximum number of training points kept; older points are dropped when exceeded.
    """

    def __init__(self, lengthscale: float = 0.5, noise: float = 1e-6, max_points: int = 60):
        self.lengthscale = float(lengthscale)
        self.noise = float(noise)
        self.max_points = int(max_points)
        self.X: List[List[float]] = []   # training inputs
        self.y: List[float] = []         # training targets (penalised fitness)
        self._L: Optional[List[List[float]]] = None   # Cholesky(L) of K
        self._alpha: Optional[List[float]] = None     # cached solution of K^{-1} y
        self._dirty = True                              # set when data changes

    # --- basic math helpers ---
    def _sqdist(self, a: List[float], b: List[float]) -> float:
        """Squared Euclidean distance between same-length vectors."""
        return sum((x - y) ** 2 for x, y in zip(a, b))

    def kernel(self, a: List[float], b: List[float]) -> float:
        """Isotropic RBF kernel k(a,b) = exp(-0.5 * ||a-b||^2 / ℓ^2)."""
        return math.exp(-0.5 * self._sqdist(a, b) / (self.lengthscale ** 2))

    def _build_K(self):
        """Construct the (n×n) kernel matrix with jitter on the diagonal."""
        n = len(self.X)
        K = [[0.0] * n for _ in range(n)]
        for i in range(n):
            for j in range(i, n):
                v = self.kernel(self.X[i], self.X[j])
                K[i][j] = v
                K[j][i] = v
            K[i][i] += self.noise
        return K

    def _cholesky(self, A):
        """Simple Cholesky factorisation A = L L^T for SPD matrix A."""
        n = len(A)
        L = [[0.0] * n for _ in range(n)]
        for i in range(n):
            for j in range(i + 1):
                s = A[i][j] - sum(L[i][k] * L[j][k] for k in range(j))
                # Guard against negative due to rounding
                L[i][j] = math.sqrt(max(s, 1e-15)) if i == j else (s / L[j][j])
        return L

    def _solve_L(self, L, b):
        """Solve (L L^T) x = b for x using forward/backward substitution."""
        n = len(L)
        # forward: L y = b
        y = [0.0] * n
        for i in range(n):
            y[i] = (b[i] - sum(L[i][k] * y[k] for k in range(i))) / L[i][i]
        # backward: L^T x = y
        x = [0.0] * n
        for i in range(n - 1, -1, -1):
            x[i] = (y[i] - sum(L[k][i] * x[k] for k in range(i + 1, n))) / L[i][i]
        return x

    def _ensure_factorised(self):
        """Factorise K and cache alpha if the training set changed since last call."""
        if not self._dirty:
            return
        if not self.X:
            # No data: clear caches
            self._L = None
            self._alpha = None
            self._dirty = False
            return
        K = self._build_K()
        self._L = self._cholesky(K)
        # Compute alpha = K^{-1} y without forming the inverse explicitly
        self._alpha = self._solve_L(self._L, self.y)
        self._dirty = False

    # --- public GP API ---
    def add(self, x: List[float], y: float):
        """Append a new training example (FIFO if over max_points)."""
        self.X.append(list(x))
        self.y.append(float(y))
        if len(self.X) > self.max_points:
            self.X.pop(0)
            self.y.pop(0)
        self._dirty = True

    def predict(self, x: List[float]) -> Tuple[float, float]:
        """Predict mean and standard deviation at location x.

        Returns (mu, sigma). With no data, return (0, 1) as non-informative prior.
        """
        self._ensure_factorised()
        if not self.X:
            return 0.0, 1.0
        # Cross-kernel vector k(x, X)
        k = [self.kernel(x, xi) for xi in self.X]
        # Predictive mean: k^T K^{-1} y  (we cached alpha = K^{-1} y)
        mu = sum(k[i] * self._alpha[i] for i in range(len(k))) if self._alpha else 0.0
        # Solve v from K v = k  using cached Cholesky; var(x) = k(x,x) - k^T v
        v = self._solve_L(self._L, k) if self._L else [0.0] * len(k)
        kxx = self.kernel(x, x) + self.noise
        var = max(1e-12, kxx - sum(vi * vi for vi in v))
        return mu, math.sqrt(var)


# ======================================
# Safe Bayesian Optimisation controller
# ======================================
class SafeBO:
    """A small, safety-aware Bayesian Optimiser.

    It uses TinyGP to model the (penalised) objective and proposes new candidates by
    maximising an acquisition like UCB (mu + beta * sigma), *subject to* a cheap
    safety proxy. The safety proxy prefers points far from previously unsafe points
    and reasonably close to safe ones, with a fallback using GP stats.

    Parameters
    ----------
    rng : random.Random
        Random generator used for reproducible proposals.
    bounds : Dict[str, (lo, hi)] or list of (name, (lo, hi))
        Parameter ranges; used for vectorisation and clipping.
    safety_penalty : float
        The same penalty value used when computing penalised fitness.
    lengthscale, noise : float
        Passed to TinyGP.
    """

    def __init__(
        self,
        rng: random.Random,
        bounds,  # dict or list[(name,(lo,hi))]
        safety_penalty: float = 2.0,
        lengthscale: float = 0.5,
        noise: float = 1e-6,
    ):
        self.rng = rng
        self.safety_penalty = float(safety_penalty)

        # --- normalise bounds once ---
        if isinstance(bounds, dict):
            items = list(bounds.items())
        else:
            items = list(bounds)
        self.names: List[str] = [str(k) for k, _ in items]
        self.ranges: List[Tuple[float, float]] = []
        for _, v in items:
            lo, hi = float(v[0]), float(v[1])
            if not lo < hi:
                raise ValueError(f"Bad bounds: {lo}, {hi}")
            self.ranges.append((lo, hi))
        self.dim = len(self.names)

        # models / memory
        self.gp = TinyGP(lengthscale=lengthscale, noise=noise, max_points=60)
        self.safe_X: List[List[float]] = []     # observed safe designs
        self.unsafe_X: List[List[float]] = []   # observed unsafe designs
        self.safe_y: List[float] = []           # penalised fitnesses of safe points

    # ----- helpers for (de)vectorisation and distances -----
    def vectorise(self, params: Dict[str, float]) -> List[float]:
        """Map a dict of named params to a list, following `self.names` order."""
        return [float(params[n]) for n in self.names]

    def devectorise(self, vec: List[float]) -> Dict[str, float]:
        """Inverse mapping with clipping to declared bounds."""
        out = {}
        for n, (lo, hi), v in zip(self.names, self.ranges, vec):
            if v < lo: v = lo
            if v > hi: v = hi
            out[n] = float(v)
        return out

    def _sqdist_scaled(self, a: List[float], b: List[float]) -> float:
        """Squared distance in units of each dimension's span (i.e., box-normalised)."""
        d2 = 0.0
        for (lo, hi), ai, bi in zip(self.ranges, a, b):
            s = hi - lo
            z = (ai - bi) / s
            d2 += z * z
        return d2

    def _is_pred_safe_stats(self, x: List[float], mu: float, sigma: float) -> bool:
        """Heuristic safety classifier using memory + GP stats (no extra GP calls).

        Rules (cheap first):
          1) If very close to a known-unsafe point (scaled distance < 0.05^2 * dim): unsafe.
          2) If very close to a known-safe point (scaled distance < 0.02^2 * dim): safe.
          3) Else, deem safe if (mu + 0.5*sigma) clears the penalty threshold.
        """
        # Distance to unsafe points: stay away.
        if self.unsafe_X:
            dmin_u = min(self._sqdist_scaled(x, u) for u in self.unsafe_X)
            if dmin_u < (0.05 ** 2) * self.dim:
                return False
        # Distance to safe points: allow small steps around known-safe territory.
        if self.safe_X:
            dmin_s = min(self._sqdist_scaled(x, s) for s in self.safe_X)
            if dmin_s < (0.02 ** 2) * self.dim:
                return True
        # Fallback: simple GP-stat check against safety penalty.
        return (mu + 0.5 * sigma) > -self.safety_penalty

    # Legacy helper (kept for completeness / testing)
    def _is_pred_safe(self, x: List[float]) -> bool:
        mu, sigma = self.gp.predict(x)
        return self._is_pred_safe_stats(x, mu, sigma)

    # ------------- public BO API -------------
    def update(self, x: List[float], y_penalised: float, is_safe: bool):
        """Feed one observation into the models and memories.

        Parameters
        ----------
        x : List[float]
            The evaluated design (vector form).
        y_penalised : float
            The observed objective **after** applying any safety penalties.
        is_safe : bool
            Whether the evaluation was deemed safe (for distance-heuristic memory).
        """
        self.gp.add(x, y_penalised)
        if is_safe:
            self.safe_X.append(list(x))
            self.safe_y.append(float(y_penalised))
        else:
            self.unsafe_X.append(list(x))

    def propose(self, incumbent: dict) -> dict:
        """Propose the next candidate near the incumbent with some global exploration.

        Strategy: sample a modest batch of candidates (mostly local Gaussian jitters
        around the incumbent, with a few global uniforms), score them using a UCB-like
        acquisition (mu + beta*sigma), and pick the best that passes the safety test.
        If none pass, fall back to the most promising under a softened safety score.
        """
        # Tuned for fast pure-Python runs
        NUM_CAND = 48      # batch size
        EXPL_NOISE = 0.08  # local jitter as a fraction of each dimension's span
        BETA = 2.0         # exploration weight for UCB

        inc = self.vectorise(incumbent)
        cand_vecs: List[List[float]] = []

        # --- Local neighbourhood exploration (most candidates) ---
        for _ in range(NUM_CAND - 6):
            v = []
            for (lo, hi), m in zip(self.ranges, inc):
                span = hi - lo
                v.append(max(lo, min(hi, m + self.rng.gauss(0.0, EXPL_NOISE * span))))
            cand_vecs.append(v)
        # --- Global exploration (a few uniform points) ---
        for _ in range(6):
            cand_vecs.append([self.rng.uniform(lo, hi) for (lo, hi) in self.ranges])

        best_safe = None      # (acq, vec)
        best_fallback = None  # (safety_score, acq, vec)

        for v in cand_vecs:
            mu, sigma = self.gp.predict(v)        # ONE GP call per candidate
            acq = mu + BETA * sigma               # UCB

            # Safety check using distance heuristics + GP stats
            is_safe = self._is_pred_safe_stats(v, mu, sigma)
            if is_safe:
                if (best_safe is None) or (acq > best_safe[0]):
                    best_safe = (acq, v)

            # Fallback ranking prefers candidates predicted safe; otherwise prefers lower sigma
            safety_score = 1.0 if is_safe else (1.0 / (1.0 + sigma))
            if (best_fallback is None) or (safety_score, acq) > (best_fallback[0], best_fallback[1]):
                best_fallback = (safety_score, acq, v)

        # Prefer the best safe candidate if any; otherwise return the best fallback.
        if best_safe is not None:
            return self.devectorise(best_safe[1])
        return self.devectorise(best_fallback[2])