"""
Sample data generator for testing and development.

Generates synthetic student interaction logs that mimic
realistic tutoring sessions.
"""

from __future__ import annotations

import argparse
import logging
from pathlib import Path
from typing import Optional

import numpy as np
import pandas as pd

from tutor_rl.config import DataConfig, TutorRLConfig

logger = logging.getLogger(__name__)


def generate_sample_data(
    n_sessions: int = 100,
    steps_per_session: tuple[int, int] = (5, 20),
    config: Optional[DataConfig] = None,
    seed: int = 42,
) -> pd.DataFrame:
    """
    Generate synthetic student interaction data.
    
    Simulates students trying to tune robot gait parameters with:
    - Random exploration of parameters
    - Occasional stuck states
    - Varying hint usage patterns
    - Realistic reward structure
    
    Args:
        n_sessions: Number of tutoring sessions to generate
        steps_per_session: (min, max) steps per session
        config: Data configuration
        seed: Random seed
        
    Returns:
        DataFrame with synthetic interaction logs
    """
    if config is None:
        config = DataConfig()
    
    rng = np.random.default_rng(seed)
    
    data = []
    
    for session_idx in range(n_sessions):
        session_id = f"session_{session_idx:04d}"
        n_steps = rng.integers(steps_per_session[0], steps_per_session[1] + 1)
        
        # Initialize oscillator parameters
        params = {
            "freq_0": rng.uniform(0.5, 2.0),
            "amp_0": rng.uniform(0.2, 0.8),
            "offset_0": rng.uniform(-0.3, 0.3),
            "phase_0": rng.uniform(0, np.pi),
            "freq_1": rng.uniform(0.5, 2.0),
            "amp_1": rng.uniform(0.2, 0.8),
            "offset_1": rng.uniform(-0.3, 0.3),
            "phase_1": rng.uniform(0, np.pi),
            "freq_2": rng.uniform(0.5, 2.0),
            "amp_2": rng.uniform(0.2, 0.8),
            "offset_2": rng.uniform(-0.3, 0.3),
            "phase_2": rng.uniform(0, np.pi),
        }
        
        # Initialize state
        current_speed = rng.uniform(0.1, 0.3)  # Start with low speed
        hint_count = 0
        stuck_counter = 0
        
        for step_idx in range(n_steps):
            # Simulate speed based on parameters (simplified dynamics)
            optimal_freq = 1.2  # Pretend optimal
            optimal_amp = 0.5
            
            freq_error = sum(abs(params[f"freq_{i}"] - optimal_freq) for i in range(3))
            amp_error = sum(abs(params[f"amp_{i}"] - optimal_amp) for i in range(3))
            
            speed = 1.0 - 0.1 * freq_error - 0.15 * amp_error + rng.normal(0, 0.05)
            speed = np.clip(speed, 0, 1)
            
            speed_delta = speed - current_speed
            
            # Stuck probability (higher if not improving)
            if speed_delta < 0.01:
                stuck_counter += 1
            else:
                stuck_counter = 0
            
            stuck_prob = min(0.9, stuck_counter * 0.15 + rng.uniform(0, 0.2))
            
            # Decide action (behavior policy - somewhat random with stuck-based hints)
            if stuck_prob > 0.6 and hint_count < 8:
                # More likely to give hint when stuck
                action_probs = [0.2, 0.15, 0.15, 0.15, 0.15, 0.1, 0.05, 0.05]
                action = rng.choice(8, p=action_probs)
            else:
                # More conservative
                action_probs = [0.6, 0.08, 0.08, 0.08, 0.08, 0.04, 0.02, 0.02]
                action = rng.choice(8, p=action_probs)
            
            if action != 0:
                hint_count += 1
            
            # Compute reward
            reward = 0.0
            reward += max(0, speed_delta) * 2.0  # Improvement bonus
            
            if stuck_prob > 0.7 and speed_delta > 0.1:
                reward += 1.5  # Stuck escape bonus
            
            if action != 0:
                reward -= 0.1  # Hint penalty
            
            # Episode done
            done = step_idx == n_steps - 1
            
            # Build row
            row = {
                "session_id": session_id,
                "step_idx": step_idx,
                "timestamp": f"2024-01-01 {10 + session_idx % 10}:{step_idx:02d}:00",
                **params,
                "recent_speed": speed,
                "recent_speed_delta": speed_delta,
                "stuck_prob": stuck_prob,
                "trial_count": step_idx,
                "hint_count": hint_count,
                "time_in_session": step_idx * 30,
                "action": action,
                "reward": reward,
                "done": done,
            }
            data.append(row)
            
            # Update state for next step
            current_speed = speed
            
            # Student adjusts parameters (simulated learning)
            if action != 0 or rng.random() < 0.7:
                # Make some adjustment
                param_to_adjust = rng.choice([
                    "freq_0", "amp_0", "freq_1", "amp_1", "freq_2", "amp_2"
                ])
                adjustment = rng.normal(0, 0.1)
                
                # If improving, more likely to continue in same direction
                if speed_delta > 0:
                    adjustment *= rng.choice([0.5, 1.5])
                
                params[param_to_adjust] = np.clip(
                    params[param_to_adjust] + adjustment,
                    0.1 if "freq" in param_to_adjust or "amp" in param_to_adjust else -1,
                    3.0 if "freq" in param_to_adjust else 1.0,
                )
    
    df = pd.DataFrame(data)
    logger.info(f"Generated {len(df)} rows across {n_sessions} sessions")
    
    return df


def main():
    """CLI for generating sample data."""
    parser = argparse.ArgumentParser(description="Generate sample tutoring data")
    parser.add_argument(
        "--output", "-o",
        type=str,
        default="sample_data.csv",
        help="Output file path",
    )
    parser.add_argument(
        "--sessions", "-n",
        type=int,
        default=100,
        help="Number of sessions to generate",
    )
    parser.add_argument(
        "--seed", "-s",
        type=int,
        default=42,
        help="Random seed",
    )
    
    args = parser.parse_args()
    
    logging.basicConfig(level=logging.INFO)
    
    df = generate_sample_data(
        n_sessions=args.sessions,
        seed=args.seed,
    )
    
    output_path = Path(args.output)
    
    if output_path.suffix == ".parquet":
        df.to_parquet(output_path, index=False)
    else:
        df.to_csv(output_path, index=False)
    
    print(f"Generated {len(df)} rows to {output_path}")
    print(f"\nSample:")
    print(df.head())
    print(f"\nAction distribution:")
    print(df["action"].value_counts().sort_index())


if __name__ == "__main__":
    main()

