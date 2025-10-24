# Group 12 – Robot Optimization Prototype
**Date:** October 2025  

---

## 1. Purpose and Context
This prototype implements a **transferable optimization framework** for educational robots.  
The goal is to demonstrate how an AI tutor could search for *near-optimal robot motion parameters* while logging each trial and staying safe.  

It directly supports the project plan sections **1.4–1.8** by providing:
- A **research-based optimization component** (Evolution Strategies).  
- **Defined input parameters** derived from real robotic control variables.  
- A **data-driven backbone** that can be connected to a tutoring interface.  
- A **hardware-agnostic** and therefore *transferable* design: only the `evaluate()` method depends on the specific robot.

---

## 2. High-Level Description
The optimizer uses a **(μ, λ) Evolution Strategy (ES)** — a simple form of evolutionary algorithm.  
It repeatedly proposes new parameter sets, tests them, and keeps the best performers.  
Each “trial” corresponds to one robot run (real or simulated).  

The system can later feed the resulting data to the AI Tutor, which interprets results pedagogically (e.g., hinting to the student why a setting worked better).

---

## 3. Optimization Parameters
These are generic control parameters found in most small wheeled robots:

| Parameter | Range | Description |
|------------|--------|-------------|
| **motor_pwm** | 0.30 – 1.00 | Normalized motor duty cycle (throttle). |
| **accel_ramp_s** | 0.00 – 2.00 | Time to ramp from 0 → target speed; limits slip. |
| **pid_p** | 0.00 – 5.00 | Proportional term in velocity control loop. |
| **pid_d** | 0.00 – 1.00 | Derivative term for damping oscillations. |
| **gear_choice** | {1, 2, 3} | Discrete “mode” (e.g., gear ratio or wheel size). |

They represent a balance between **speed, stability, and safety** — the same trade-offs students manage manually in the classroom.

---

## 4. Code Structure

### 4.1 `evaluate(params: dict) -> dict`
**Purpose:** Runs a single experiment on the robot or simulator.  
**Inputs:** Dictionary with all parameter names above.  
**Outputs:** Dictionary with measured results:
```python
{
  "speed_mps": float,        # measured average speed
  "overcurrent": bool,       # True if electrical current too high
  "overtemp": bool,          # True if motor overheated
  "timeout": bool            # True if test aborted or stalled
}
```
In this prototype, the body is a **synthetic physics model** with noise.  
In real use, you replace the internal equations with actual hardware commands and sensor readings.

---

### 4.2 `es_optimize_for_gear(gear_choice, trials, mu, lam, init_sigma, seed, log_csv)`
**Purpose:** Runs an Evolution Strategy for one discrete gear mode.  
**Key arguments:**  
- `gear_choice`: integer 1–3 representing a robot configuration.  
- `trials`: number of test runs (e.g., 40).  
- `mu`: number of elites kept each generation (default 4).  
- `lam`: offspring per generation (default 12).  
- `init_sigma`: initial mutation step size (default 0.15).  
- `seed`: random seed for reproducibility.  
- `log_csv`: path to CSV file for logging.  

**Process:**  
1. Sample λ new parameter sets from a Gaussian distribution.  
2. Evaluate each with `evaluate()`.  
3. Compute penalized fitness = `speed – penalty`.  
4. Select μ best individuals and update the mean + step size.  
5. Iterate until the trial budget is reached.  

**Output:** A dictionary containing the best configuration for that gear.

---

### 4.3 `optimize_all_gears(total_trials, seed)`
Loops over all `gear_choice` values, calling `es_optimize_for_gear()` for each.  
Returns both the global best configuration and per-gear summaries.

---

### 4.4 Utility Functions
- `clip(x, lo, hi)` → bounds a value.  
- `sample_candidate(mean_vec, sigma)` → generates one parameter vector.

---

## 5. Outputs
1. **Console summary:**
```text
=== BEST CONFIG ===
{'gear_choice': 2, 'params': {...}, 'speed_mps': 2.37, 'fitness': 2.34}
```
…showing the best parameter set and predicted speed.

2. **CSV log (`trials_log.csv`):**  
Each row records a single trial with:  
`t, gear_choice, motor_pwm, accel_ramp_s, pid_p, pid_d, speed_mps, penalized_fitness, overcurrent, overtemp, timeout`  

This file is the main artifact for analysis and visualization.

---

## 6. How to Run
1. Install Python ≥ 3.8.  
2. Save both `optimizer.py` and this README in the same folder.  
3. Open a terminal and execute:
```bash
python optimizer.py
```
4. Wait 1–2 minutes; the script will simulate ≈120 trials.  
5. Inspect the printed best configuration and the generated `trials_log.csv`.

*(No external libraries beyond the Python standard library are required.)*

---

## 7. Integration Path / Transferable Use
Because all robot-specific logic is contained in `evaluate()`, the same optimization core can:
- Be connected to **any robot** that exposes basic control variables.  
- Run in a **simulated environment** for pre-training or classroom demo.  
- Feed its logs to the **AI Tutor** layer, which interprets results and gives feedback to students.  
- Serve as a **benchmark module** for comparing physical robots with different imperfections.

This aligns with the project’s shift toward a **transferable, modular solution** rather than a single-robot implementation.

---

## 8. Relation to Project Plan
| Project Plan Section | Link to Prototype |
|-----------------------|-------------------|
| **1.4 Concept & Approach** | Demonstrates a concrete optimization backend using evolutionary strategies and realistic control parameters. |
| **1.5 Work so far** | Establishes feasibility of a robot-agnostic optimizer; groundwork for integration with computer-vision and tutoring modules. |
| **1.6 Envisioned Prototypes** | This code will become the optimization sub-system of the full AI Tutor prototype. |
| **1.7 Ambition** | Provides a novel combination of performance optimization and educational feedback. |
| **1.8 Related Work** | Directly implements concepts from Hart et al. (2021) and Bayesian/Evolutionary optimization literature, but simplified for classroom use. |

---

## 9. Example Interpretation
- **Input:** ranges and gear list defined above.  
- **Process:** Evolution Strategy tests multiple parameter sets per gear, learning which produce higher speed without safety violations.  
- **Output:** Best parameter set + log of all results.  

In a real AI-Tutor (with an arbitrary robot with a speed task) system, the same data could be used to tell the student (of course the following would be made simpler for someone in secondary school):  
> “Your robot reached 2.3 m/s — that’s 15 % below the optimal 2.7 m/s. Try increasing acceleration ramp slightly and lowering P gain.”

---

## 10. Next Steps
- Replace simulated `evaluate()` with physical robot interface.  
- Add camera-based motion tracking (OpenCV/ArUco).  
- Feed log data to emotion-adaptive tutoring module.  
- Perform classroom validation on multiple robot types to prove transferability.
