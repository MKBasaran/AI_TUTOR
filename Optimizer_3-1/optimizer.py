import math, random, time, csv
from statistics import mean

# ---------------------------
# CONFIG: bounds & penalties
# ---------------------------
BOUNDS = {
    "motor_pwm": (0.30, 1.00),
    "accel_ramp_s": (0.00, 2.00),
    "pid_p": (0.00, 5.00),
    "pid_d": (0.00, 1.00),
}
GEAR_CHOICES = [1, 2, 3]  # discrete modes you can map to hardware configs

SAFETY_PENALTY = 2.0      # m/s equivalent penalty for any safety violation
TIME_BUDGET_TRIALS = 120  # keep ≤150 per your plan

# ---------------------------
# USER: implement this hook
# ---------------------------
def evaluate(params: dict) -> dict:
    """
    Runs ONE attempt on robot/sim and returns measured speed and safety flags.
    Replace the body with your real control call + sensors.
    """
    # --- EXAMPLE stub (replace!) ---
    # A toy, noisy objective: higher PWM helps until slip; proper ramp & gains help stability
    pwm = params["motor_pwm"]
    ramp = params["accel_ramp_s"]
    kp   = params["pid_p"]
    kd   = params["pid_d"]
    gear = params["gear_choice"]

    # pseudo physics
    base = 2.5 * pwm * (1 - 0.25 * max(0.0, pwm - 0.8))  # slip beyond 0.8
    launch = 1.0 - 0.4 * max(0.0, 0.3 - ramp)            # penalize zero ramp on low-friction
    control = 1.0 - 0.15 * abs(kp - 2.0) - 0.10 * abs(kd - 0.2)
    gear_factor = {1: 0.95, 2: 1.00, 3: 0.90}[gear]

    speed = max(0.0, base * launch * max(0.6, control) * gear_factor)
    speed += random.gauss(0, 0.03)                       # measurement noise

    # simple safety heuristics
    overtemp = pwm > 0.95 and ramp < 0.2
    overcurrent = pwm > 0.9 and kp > 3.5
    timeout = False

    return {"speed_mps": float(speed), "overcurrent": overcurrent, "overtemp": overtemp, "timeout": timeout}

# ---------------------------
# Evolution Strategy (μ, λ)
# ---------------------------
def clip(x, lo, hi):
    return max(lo, min(hi, x))

def sample_candidate(mean_vec, sigma):
    """Gaussian sample with isotropic sigma; returns dict param values."""
    v = {}
    for i, key in enumerate(["motor_pwm","accel_ramp_s","pid_p","pid_d"]):
        lo, hi = BOUNDS[key]
        val = mean_vec[i] + random.gauss(0, sigma)
        v[key] = clip(val, lo, hi)
    return v

def es_optimize_for_gear(gear_choice, trials=40, mu=4, lam=12, init_sigma=0.15, seed=None, log_csv="trials_log.csv"):
    if seed is not None:
        random.seed(seed)

    # init mean at mid-bounds
    mean_vec = []
    for key in ["motor_pwm","accel_ramp_s","pid_p","pid_d"]:
        lo, hi = BOUNDS[key]
        mean_vec.append((lo+hi)/2.0)
    sigma = init_sigma

    best = None
    fields = ["t","gear_choice","motor_pwm","accel_ramp_s","pid_p","pid_d","speed_mps","penalized_fitness","overcurrent","overtemp","timeout"]
    with open(log_csv, "a", newline="") as f:
        w = csv.DictWriter(f, fieldnames=fields)
        try:
            w.writeheader()
        except:
            pass

        t = 0
        while t < trials:
            # λ offspring
            pop = []
            for _ in range(lam):
                cand = sample_candidate(mean_vec, sigma)
                cand["gear_choice"] = gear_choice
                result = evaluate(cand)
                penalty = SAFETY_PENALTY if (result["overcurrent"] or result["overtemp"] or result["timeout"]) else 0.0
                fitness = result["speed_mps"] - penalty
                pop.append((fitness, cand, result))

                row = {
                    "t": t, "gear_choice": gear_choice,
                    "motor_pwm": cand["motor_pwm"], "accel_ramp_s": cand["accel_ramp_s"],
                    "pid_p": cand["pid_p"], "pid_d": cand["pid_d"],
                    "speed_mps": result["speed_mps"], "penalized_fitness": fitness,
                    "overcurrent": result["overcurrent"], "overtemp": result["overtemp"], "timeout": result["timeout"],
                }
                w.writerow(row)
                t += 1
                if t >= trials:
                    break

            # select μ best
            pop.sort(key=lambda x: x[0], reverse=True)
            elites = pop[:mu]

            # update mean (simple average of elites) and adapt sigma
            if elites:
                mean_vec = [
                    mean([e[1]["motor_pwm"] for e in elites]),
                    mean([e[1]["accel_ramp_s"] for e in elites]),
                    mean([e[1]["pid_p"] for e in elites]),
                    mean([e[1]["pid_d"] for e in elites]),
                ]
                # decrease sigma if population converged, increase if spread helps
                fit_vals = [e[0] for e in elites]
                spread = (max(fit_vals) - min(fit_vals)) if len(fit_vals) > 1 else 0.0
                if spread < 0.05:
                    sigma *= 0.82
                else:
                    sigma *= 1.05
                sigma = max(0.02, min(0.25, sigma))

            # track best
            if best is None or elites[0][0] > best[0]:
                best = elites[0]

    best_fit, best_params, best_result = best
    return {"gear_choice": gear_choice, "params": best_params, "speed_mps": best_result["speed_mps"], "fitness": best_fit}

def optimize_all_gears(total_trials=TIME_BUDGET_TRIALS, seed=42):
    random.seed(seed)
    per_gear = max(10, total_trials // len(GEAR_CHOICES))
    results = []
    for g in GEAR_CHOICES:
        res = es_optimize_for_gear(g, trials=per_gear, seed=random.randint(0,10**6))
        results.append(res)
    results.sort(key=lambda r: r["fitness"], reverse=True)
    return results[0], results

if __name__ == "__main__":
    best, all_results = optimize_all_gears()
    print("=== BEST CONFIG ===")
    print(best)
    print("\n=== ALL GEARS (best first) ===")
    for r in all_results:
        print(r)
