# safe_bo.py
# Stdlib-only Safe Bayesian Optimisation (fast enough for Task 3).
from __future__ import annotations
from typing import List, Tuple, Optional, Dict
import math, random

# ----------------------------
# TinyGP: fast enough for BO
# ----------------------------
class TinyGP:
    def __init__(self, lengthscale: float = 0.5, noise: float = 1e-6, max_points: int = 60):
        self.lengthscale = float(lengthscale)
        self.noise = float(noise)
        self.max_points = int(max_points)
        self.X: List[List[float]] = []
        self.y: List[float] = []
        self._L: Optional[List[List[float]]] = None
        self._alpha: Optional[List[float]] = None  # K^{-1} y cache
        self._dirty = True

    def _sqdist(self, a: List[float], b: List[float]) -> float:
        return sum((x - y) ** 2 for x, y in zip(a, b))

    def kernel(self, a: List[float], b: List[float]) -> float:
        return math.exp(-0.5 * self._sqdist(a, b) / (self.lengthscale ** 2))

    def _build_K(self):
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
        n = len(A)
        L = [[0.0] * n for _ in range(n)]
        for i in range(n):
            for j in range(i + 1):
                s = A[i][j] - sum(L[i][k] * L[j][k] for k in range(j))
                L[i][j] = math.sqrt(max(s, 1e-15)) if i == j else (s / L[j][j])
        return L

    def _solve_L(self, L, b):
        n = len(L)
        y = [0.0] * n
        for i in range(n):
            y[i] = (b[i] - sum(L[i][k] * y[k] for k in range(i))) / L[i][i]
        x = [0.0] * n
        for i in range(n - 1, -1, -1):
            x[i] = (y[i] - sum(L[k][i] * x[k] for k in range(i + 1, n))) / L[i][i]
        return x

    def _ensure_factorised(self):
        if not self._dirty:
            return
        if not self.X:
            self._L = None
            self._alpha = None
            self._dirty = False
            return
        K = self._build_K()
        self._L = self._cholesky(K)
        self._alpha = self._solve_L(self._L, self.y)  # cache alpha
        self._dirty = False

    def add(self, x: List[float], y: float):
        self.X.append(list(x))
        self.y.append(float(y))
        if len(self.X) > self.max_points:
            self.X.pop(0)
            self.y.pop(0)
        self._dirty = True

    def predict(self, x: List[float]) -> Tuple[float, float]:
        self._ensure_factorised()
        if not self.X:
            return 0.0, 1.0
        k = [self.kernel(x, xi) for xi in self.X]
        mu = sum(k[i] * self._alpha[i] for i in range(len(k))) if self._alpha else 0.0
        v = self._solve_L(self._L, k) if self._L else [0.0] * len(k)
        kxx = self.kernel(x, x) + self.noise
        var = max(1e-12, kxx - sum(vi * vi for vi in v))
        return mu, math.sqrt(var)

# ----------------------------
# Safe BO (batched propose)
# ----------------------------
class SafeBO:
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

    # ----- helpers -----
    def vectorise(self, params: Dict[str, float]) -> List[float]:
        return [float(params[n]) for n in self.names]

    def devectorise(self, vec: List[float]) -> Dict[str, float]:
        out = {}
        for n, (lo, hi), v in zip(self.names, self.ranges, vec):
            if v < lo: v = lo
            if v > hi: v = hi
            out[n] = float(v)
        return out

    def _sqdist_scaled(self, a: List[float], b: List[float]) -> float:
        d2 = 0.0
        for (lo, hi), ai, bi in zip(self.ranges, a, b):
            s = hi - lo
            z = (ai - bi) / s
            d2 += z * z
        return d2

    def _is_pred_safe_stats(self, x: List[float], mu: float, sigma: float) -> bool:
        # Distance rules first (cheap)
        if self.unsafe_X:
            dmin_u = min(self._sqdist_scaled(x, u) for u in self.unsafe_X)
            if dmin_u < (0.05 ** 2) * self.dim:
                return False
        if self.safe_X:
            dmin_s = min(self._sqdist_scaled(x, s) for s in self.safe_X)
            if dmin_s < (0.02 ** 2) * self.dim:
                return True
        # Fallback heuristic using the provided stats (no extra GP call)
        return (mu + 0.5 * sigma) > -self.safety_penalty

    # Legacy (should not be used in hot path)
    def _is_pred_safe(self, x: List[float]) -> bool:
        mu, sigma = self.gp.predict(x)
        return self._is_pred_safe_stats(x, mu, sigma)

    # ----- public API -----
    def update(self, x: List[float], y_penalised: float, is_safe: bool):
        self.gp.add(x, y_penalised)
        if is_safe:
            self.safe_X.append(list(x))
            self.safe_y.append(float(y_penalised))
        else:
            self.unsafe_X.append(list(x))

    def propose(self, incumbent: dict) -> dict:
        # Tuned to keep runs fast in pure Python
        NUM_CAND = 48
        EXPL_NOISE = 0.08
        BETA = 2.0

        inc = self.vectorise(incumbent)
        cand_vecs: List[List[float]] = []

        # Local neighbourhood
        for _ in range(NUM_CAND - 6):
            v = []
            for (lo, hi), m in zip(self.ranges, inc):
                span = hi - lo
                v.append(max(lo, min(hi, m + self.rng.gauss(0.0, EXPL_NOISE * span))))
            cand_vecs.append(v)
        # Global sprinkling
        for _ in range(6):
            cand_vecs.append([self.rng.uniform(lo, hi) for (lo, hi) in self.ranges])

        best_safe = None      # (acq, vec)
        best_fallback = None  # (safety_score, acq, vec)

        for v in cand_vecs:
            mu, sigma = self.gp.predict(v)           # ONE call per candidate
            acq = mu + BETA * sigma
            is_safe = self._is_pred_safe_stats(v, mu, sigma)
            if is_safe:
                if (best_safe is None) or (acq > best_safe[0]):
                    best_safe = (acq, v)
            safety_score = 1.0 if is_safe else (1.0 / (1.0 + sigma))
            if (best_fallback is None) or (safety_score, acq) > (best_fallback[0], best_fallback[1]):
                best_fallback = (safety_score, acq, v)

        if best_safe is not None:
            return self.devectorise(best_safe[1])
        return self.devectorise(best_fallback[2])
