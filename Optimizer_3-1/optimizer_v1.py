"""
Group 12 – Optimisation Engine (Version 1)
Safe Bayesian Optimisation + CMA-ES with safety gating and logging.
Designed to align with Project Plan Task 3.1 and Requirements R1–R6.

How to run (simulation fallback):
    python optimizer_v1.py --mode simulate --trials 120 --seed 42

How to run (replay from a CSV of recorded robot data):
    python optimizer_v1.py --mode replay --log trials_log.csv --trials 120 --seed 42

Outputs:
    - console summary of top reference solutions per gear (for tutor only)
    - logs/opt_trials_<timestamp>.csv      (all evaluations)
    - tutor_reference_solutions.json       (top-k per gear, not meant for students)

Note:
    This file is self-contained (stdlib only). Replace `evaluate_real_robot()`
    with your hardware harness when ready.
"""
from __future__ import annotations
import argparse, csv, json, math, os, random, time
from dataclasses import dataclass
from typing import Callable, Dict, List, Tuple, Optional
import dataclasses

from cma_es import CMAState, cma_es_step, per_dim_step_scales
from safe_bo import TinyGP, SafeBO  # type: ignore

# ---------------------------
# Parameter space
# ---------------------------
PARAM_BOUNDS: Dict[str, Tuple[float, float]] = {
    "motor_pwm": (0.30, 1.00),
    "accel_ramp_s": (0.00, 2.00),
    "pid_p": (0.00, 5.00),
    "pid_d": (0.00, 1.00),
    "cpg_freq_hz": (0.5, 3.0),
    "cpg_amp": (0.0, 1.0),
    "cpg_phase_deg": (0.0, 360.0),
}
GEAR_CHOICES = [1, 2, 3]

# Safety config
SAFETY_PENALTY = 2.0
MAX_VOLTAGE = 12.0
MAX_JOINT_DEG = 60.0

DEFAULT_SAFETY_THRESH = {
    "overcurrent_pwm_gt": 0.9,
    "overcurrent_accel_lt": 0.1,
    "overtemp_pwm_gt": 0.95,
    "overtemp_p_gt": 3.5,
    "jointlimit_d_gt": 0.8,
    "jointlimit_accel_lt": 0.05,
    "min_pwm": 0.32,
}
SAFETY_THRESH = DEFAULT_SAFETY_THRESH.copy()

# ---------------------------
# Utilities
# ---------------------------
def clip(x: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, x))

def vectorise(params: Dict[str, float]) -> List[float]:
    return [params[k] for k in PARAM_BOUNDS.keys()]

def devectorise(vec: List[float]) -> Dict[str, float]:
    return {k: clip(v, *PARAM_BOUNDS[k]) for v, k in zip(vec, PARAM_BOUNDS.keys())}

def random_in_bounds(rng: random.Random) -> Dict[str, float]:
    return {k: rng.uniform(lo, hi) for k, (lo, hi) in PARAM_BOUNDS.items()}

def _json_default(o):
    if dataclasses.is_dataclass(o):
        return dataclasses.asdict(o)
    if isinstance(o, (set,)):
        return list(o)
    return str(o)

# ---------------------------
# Safety gate
# ---------------------------
from dataclasses import dataclass as _dc

@_dc
class SafetyResult:
    overcurrent: bool = False
    overtemp: bool = False
    joint_limit: bool = False
    timeout: bool = False

    @property
    def any_violation(self) -> bool:
        return self.overcurrent or self.overtemp or self.joint_limit or self.timeout

def safety_gate(params: Dict[str, float], gear_choice: int) -> SafetyResult:
    pwm = params["motor_pwm"]
    accel = params["accel_ramp_s"]
    p = params["pid_p"]
    d = params["pid_d"]
    # CPG available if needed
    overcurrent = (pwm > SAFETY_THRESH["overcurrent_pwm_gt"] and accel < SAFETY_THRESH["overcurrent_accel_lt"])
    overtemp = (pwm > SAFETY_THRESH["overtemp_pwm_gt"] and p > SAFETY_THRESH["overtemp_p_gt"])
    joint_limit = (d > SAFETY_THRESH["jointlimit_d_gt"] and accel < SAFETY_THRESH["jointlimit_accel_lt"])
    timeout = (pwm < SAFETY_THRESH["min_pwm"])
    return SafetyResult(overcurrent, overtemp, joint_limit, timeout)

# ---------------------------
# Evaluation backends
# ---------------------------
@_dc
class EvalOutcome:
    speed_mps: float
    safety: SafetyResult
    @property
    def penalised_fitness(self) -> float:
        return self.speed_mps - (SAFETY_PENALTY if self.safety.any_violation else 0.0)

def evaluate_simulated(params: Dict[str, float], gear_choice: int, rng: random.Random) -> EvalOutcome:
    s = safety_gate(params, gear_choice)
    pwm = params["motor_pwm"]
    accel = params["accel_ramp_s"]
    p = params["pid_p"]
    d = params["pid_d"]
    f = params.get("cpg_freq_hz", 1.5)
    a = params.get("cpg_amp", 0.5)
    ph = params.get("cpg_phase_deg", 0.0)

    base = {1: 1.6, 2: 2.2, 3: 2.8}[gear_choice]
    speed = base * pwm * (1.0 - 0.15 * max(0.0, 0.5 - accel))
    speed *= math.exp(-0.12 * (p - 2.0) ** 2) * math.exp(-0.8 * (d - 0.3) ** 2)
    speed *= math.exp(-0.6 * (f - 1.5) ** 2) * math.exp(-2.0 * (a - 0.6) ** 2) * (0.95 + 0.05 * math.cos(math.radians(ph)))
    speed += rng.gauss(0.0, 0.03)
    if s.any_violation:
        speed *= 0.3
    return EvalOutcome(speed_mps=max(0.0, speed), safety=s)

class ReplayLog:
    def __init__(self, csv_path: str):
        self.rows: List[Dict[str, str]] = []
        if os.path.exists(csv_path):
            with open(csv_path, "r", newline="") as f:
                for r in csv.DictReader(f):
                    self.rows.append(r)
        self._index = 0

    def next(self) -> Optional[Dict[str, str]]:
        if not self.rows:
            return None
        r = self.rows[self._index % len(self.rows)]
        self._index += 1
        return r


def evaluate_replay(params: Dict[str, float], gear_choice: int, replay: ReplayLog) -> EvalOutcome:
    """Consume next valid row from replay; skip malformed/header rows and fallback to simulation."""
    def _to_bool(v: Optional[str]) -> bool:
        if v is None:
            return False
        return str(v).strip().lower() in ("true", "1", "yes", "y", "t")

    # Try to find a valid numeric row; skip rows where speed_mps is not numeric (e.g. repeated header)
    r = replay.next()
    while r is not None:
        speed_str = r.get("speed_mps", "")
        try:
            speed = float(str(speed_str).strip())
        except (ValueError, TypeError):
            r = replay.next()
            continue

        s = SafetyResult(
            overcurrent=_to_bool(r.get("overcurrent", "false")),
            overtemp=_to_bool(r.get("overtemp", "false")),
            joint_limit=_to_bool(r.get("joint_limit", "false")),
            timeout=_to_bool(r.get("timeout", "false")),
        )

        if s.any_violation:
            speed *= 0.3

        return EvalOutcome(speed_mps=max(0.0, speed), safety=s)

    # No valid replay row available: fallback to simulated evaluator
    rng = random.Random()
    return evaluate_simulated(params, gear_choice, rng)

# ---------------------------
# Logging
# ---------------------------
class TrialLogger:
    def __init__(self, out_path: str):
        os.makedirs(os.path.dirname(out_path), exist_ok=True)
        self.out_path = out_path
        self.fieldnames = [
            "t", "gear_choice", *PARAM_BOUNDS.keys(), "speed_mps",
            "penalised_fitness", "overcurrent", "overtemp", "joint_limit", "timeout",
            "algo", "note"
        ]
        write_header = not os.path.exists(out_path)
        self.f = open(out_path, "a", newline="")
        self.w = csv.DictWriter(self.f, fieldnames=self.fieldnames)
        if write_header:
            self.w.writeheader()
        self.t = 0

    def log(self, gear: int, params: Dict[str, float], outcome: EvalOutcome, algo: str, note: str = ""):
        row = {
            "t": self.t,
            "gear_choice": gear,
            **{k: round(params[k], 6) for k in PARAM_BOUNDS.keys()},
            "speed_mps": round(outcome.speed_mps, 6),
            "penalised_fitness": round(outcome.penalised_fitness, 6),
            "overcurrent": outcome.safety.overcurrent,
            "overtemp": outcome.safety.overtemp,
            "joint_limit": outcome.safety.joint_limit,
            "timeout": outcome.safety.timeout,
            "algo": algo,
            "note": note,
        }
        self.w.writerow(row)
        self.f.flush()
        self.t += 1

    def close(self):
        self.f.close()

# ---------------------------
# Algo wrappers
# ---------------------------
@dataclass
class AlgoConfig:
    trials: int = 40
    lam: int = 12
    init_sigma: float = 0.15
    seed: int = 0
    mode: str = "simulate"
    replay_log: Optional[str] = None

def es_optimize_for_gear(gear: int, cfg: AlgoConfig, logger: TrialLogger, evaluator) -> Dict:
    rng = random.Random(cfg.seed + gear * 101)
    mean_vec = [0.5 * (lo + hi) for (lo, hi) in PARAM_BOUNDS.values()]
    state = CMAState(mean=mean_vec, sigma=cfg.init_sigma)

    def sample_candidate() -> Dict[str, float]:
        vec = []
        per_dim = per_dim_step_scales(state)  # sigma * sqrt(cov_diag[j])
        for (lo, hi), m, s in zip(PARAM_BOUNDS.values(), state.mean, per_dim):
            span = (hi - lo)
            v = clip(rng.gauss(m, s * span), lo, hi)
            vec.append(v)
        return devectorise(vec)

    best = {"gear_choice": gear, "params": devectorise(state.mean), "fitness": -1e9, "speed_mps": 0.0, "algo": "CMA-ES"}
    t = 0
    while t < cfg.trials:
        batch: List[Tuple[float, List[float]]] = []
        for _ in range(cfg.lam):
            params = sample_candidate()
            s = safety_gate(params, gear)
            if s.any_violation:
                outcome = EvalOutcome(speed_mps=0.0, safety=s)
            else:
                outcome = evaluator(params, gear)

            fit = outcome.penalised_fitness
            logger.log(gear, params, outcome, algo="CMA-ES")
            batch.append((fit, vectorise(params)))

            if fit > best["fitness"]:
                best = {"gear_choice": gear, "params": params, "fitness": fit, "speed_mps": outcome.speed_mps, "algo": "CMA-ES"}

            t += 1
            if t >= cfg.trials:
                break

        state = cma_es_step(state, cfg.lam, batch)

    return best

def safe_bo_for_gear(gear: int, cfg: AlgoConfig, logger: TrialLogger, evaluator):
    rng = random.Random(cfg.seed + gear * 2025)
    bo = SafeBO(rng, list(PARAM_BOUNDS.items()), safety_penalty=SAFETY_PENALTY, lengthscale=0.5)

    # seed designs
    center = devectorise(vectorise({k: (v[0] + v[1]) / 2.0 for k, v in PARAM_BOUNDS.items()}))
    seeds = [center]
    for _ in range(2):
        s = {}
        for k, (lo, hi) in PARAM_BOUNDS.items():
            span = hi - lo
            s[k] = clip(center[k] + rng.gauss(0, 0.05 * span), lo, hi)
        seeds.append(s)

    best = {"gear_choice": gear, "params": center, "fitness": -1e9, "speed_mps": 0.0, "algo": "Safe-BO"}
    t = 0
    for seed in seeds:
        safe = not safety_gate(seed, gear).any_violation
        outcome = evaluator(seed, gear) if safe else EvalOutcome(0.0, SafetyResult(True, True, True, True))
        bo.update(bo.vectorise(seed), outcome.penalised_fitness, not outcome.safety.any_violation)
        logger.log(gear, seed, outcome, "safe-bo-seed")
        if outcome.penalised_fitness > best["fitness"]:
            best = {"gear_choice": gear, "params": seed, "fitness": outcome.penalised_fitness, "speed_mps": outcome.speed_mps, "algo": "Safe-BO"}
        t += 1

    while t < cfg.trials:
        cand = bo.propose(best["params"])
        safe = not safety_gate(cand, gear).any_violation
        outcome = evaluator(cand, gear) if safe else EvalOutcome(0.0, SafetyResult(True, True, True, True))
        bo.update(bo.vectorise(cand), outcome.penalised_fitness, not outcome.safety.any_violation)
        logger.log(gear, cand, outcome, "safe-bo")
        if outcome.penalised_fitness > best["fitness"]:
            best = {"gear_choice": gear, "params": cand, "fitness": outcome.penalised_fitness, "speed_mps": outcome.speed_mps, "algo": "Safe-BO"}
        t += 1

    return best

# ---------------------------
# Orchestrator
# ---------------------------
@dataclass
class RunConfig:
    total_trials: int = 120
    seed: int = 0
    mode: str = "simulate"      # "simulate" or "replay"
    log_path: Optional[str] = None

def optimise_all_gears(run: RunConfig) -> Tuple[Dict, List[Dict], str]:
    rng = random.Random(run.seed)
    trials_per_algo_per_gear = run.total_trials // (len(GEAR_CHOICES) * 2)
    print(f"[Task3] Budget split: {trials_per_algo_per_gear} trials × {len(GEAR_CHOICES)} gears × 2 algos"
          f" = {trials_per_algo_per_gear * len(GEAR_CHOICES) * 2} total")

    ts = time.strftime("%Y%m%d_%H%M%S")
    out_csv = os.path.join("logs", f"opt_trials_{ts}.csv")
    logger = TrialLogger(out_csv)

    if run.mode == "replay" and run.log_path:
        replay = ReplayLog(run.log_path)
        evaluator = lambda p, g: evaluate_replay(p, g, replay)
    else:
        evaluator = lambda p, g: evaluate_simulated(p, g, rng)

    per_gear_best: List[Dict] = []

    for gear in GEAR_CHOICES:
        es_best = es_optimize_for_gear(
            gear,
            AlgoConfig(trials=trials_per_algo_per_gear, lam=12, init_sigma=0.18, seed=run.seed, mode=run.mode),
            logger,
            evaluator,
        )
        bo_best = safe_bo_for_gear(
            gear,
            AlgoConfig(trials=trials_per_algo_per_gear, seed=run.seed, mode=run.mode),
            logger,
            evaluator,
        )
        pick = bo_best if bo_best["fitness"] >= es_best["fitness"] else es_best
        per_gear_best.append(pick)

    per_gear_best.sort(key=lambda r: r["fitness"], reverse=True)
    best_overall = per_gear_best[0]

    ref = {
        "timestamp": ts,
        "best_overall": best_overall,
        "top_per_gear": per_gear_best,
        "note": "For hint generation only. Do NOT reveal to students.",
    }
    with open("tutor_reference_solutions.json", "w") as f:
        json.dump(ref, f, indent=2, default=_json_default)

    logger.close()
    return best_overall, per_gear_best, out_csv

# ---------------------------
# CLI
# ---------------------------
if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--mode", choices=["simulate", "replay"], default="simulate")
    ap.add_argument("--log", type=str, default=None, help="Path to recorded trials_log.csv for replay mode")
    ap.add_argument("--trials", type=int, default=120)
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--config", type=str, default=None, help="Optional JSON file to override bounds/limits/safety")
    args = ap.parse_args()

    if args.config and os.path.exists(args.config):
        with open(args.config, "r") as f:
            cfg_json = json.load(f)
        if isinstance(cfg_json.get("param_bounds"), dict):
            new_bounds = {}
            for k, v in cfg_json["param_bounds"].items():
                if isinstance(v, (list, tuple)) and len(v) == 2:
                    new_bounds[str(k)] = (float(v[0]), float(v[1]))
            if new_bounds:
                for k, rngv in new_bounds.items():
                    PARAM_BOUNDS[k] = (float(rngv[0]), float(rngv[1]))
        if "max_voltage" in cfg_json:
            MAX_VOLTAGE = float(cfg_json["max_voltage"])
        if "max_joint_deg" in cfg_json:
            MAX_JOINT_DEG = float(cfg_json["max_joint_deg"])
        if isinstance(cfg_json.get("safety"), dict):
            SAFETY_THRESH.update({k: float(v) for k, v in cfg_json["safety"].items() if k in SAFETY_THRESH})

    best, per_gear, path = optimise_all_gears(
        RunConfig(total_trials=args.trials, seed=args.seed, mode=args.mode, log_path=args.log)
    )

    print("=== BEST CONFIG (tutor-only) ===")
    print(best)

    print("=== PER-GEAR PICKS (best first) ===")
    for r in per_gear:
        print({k: r[k] for k in ["gear_choice", "algo", "fitness", "speed_mps", "params"]})

    print(f"Log written to: {path}")
