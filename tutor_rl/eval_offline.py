"""
Offline evaluation for the CQL hint policy.

Computes metrics without environment interaction:
- Mean predicted Q for chosen actions
- Action distribution comparison (learned vs behavior policy)
- Off-Policy Evaluation (OPE) using FQE or importance sampling
"""

from __future__ import annotations

import argparse
import json
import logging
from collections import Counter
from pathlib import Path
from typing import Any, Optional

import numpy as np

from tutor_rl.config import TutorRLConfig, load_config
from tutor_rl.data import (
    build_transitions,
    load_dataset,
    load_scaler,
    load_feature_schema,
    split_by_session,
)
from tutor_rl.reward import compute_rewards

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
)
logger = logging.getLogger(__name__)


def load_cql_model(model_dir: str | Path):
    """
    Load trained CQL model from directory.
    
    Args:
        model_dir: Directory containing model artifacts
        
    Returns:
        Loaded CQL model
    """
    import pickle
    import io
    
    model_dir = Path(model_dir)
    model_path = None
    
    # Try various model file locations (d3rlpy v2.x auto-saves to subdirectory)
    possible_paths = [
        model_dir / "model.pt",
        model_dir / "model.d3",
    ]
    
    # Also check d3rlpy auto-save directory
    cql_dir = model_dir / "cql_hint_policy"
    if cql_dir.exists():
        d3_files = list(cql_dir.glob("model_*.d3"))
        if d3_files:
            d3_files.sort(key=lambda x: int(x.stem.split("_")[1]) if "_" in x.stem else 0)
            possible_paths.insert(0, d3_files[-1])
    
    for alt_path in possible_paths:
        if alt_path.exists():
            model_path = alt_path
            break
    
    if model_path is None:
        raise FileNotFoundError(f"No model file found in {model_dir}")
    
    logger.info(f"Loading model from {model_path}")
    
    try:
        from d3rlpy.algos import DiscreteCQL
        
        # Find params.json
        params_path = model_path.parent / "params.json"
        
        if model_path.suffix == ".d3" and params_path.exists():
            # d3rlpy v2.x: manually load .d3 file
            with open(model_path, 'rb') as f:
                d3_data = pickle.load(f)
            
            model = DiscreteCQL.from_json(str(params_path))
            
            torch_data = d3_data['torch']
            buffer = io.BytesIO(torch_data)
            model._impl.load_model(buffer)
            
            logger.info("Loaded model from .d3 file with manual unpacking")
            return model
        
        # Fallback to standard loading
        if params_path.exists():
            model = DiscreteCQL.from_json(str(params_path))
            model.load_model(str(model_path))
            return model
            
        raise RuntimeError(f"Could not load model from {model_path}")
        
    except Exception as e:
        logger.error(f"Failed to load model: {e}")
        raise


def _get_all_q_values(model, observations: np.ndarray, num_actions: int) -> np.ndarray:
    """
    Get Q-values for all actions given observations.
    
    d3rlpy v2.x predict() returns actions, not Q-values.
    We use predict_value() with all actions to get Q-values.
    
    Args:
        model: CQL model
        observations: Array of shape (N, obs_dim)
        num_actions: Number of discrete actions
        
    Returns:
        Q-values array of shape (N, num_actions)
    """
    n_obs = len(observations)
    all_q = np.zeros((n_obs, num_actions), dtype=np.float32)
    
    for action_id in range(num_actions):
        actions = np.full(n_obs, action_id, dtype=np.int64)
        all_q[:, action_id] = model.predict_value(observations, actions)
    
    return all_q


def compute_q_metrics(
    model,
    transitions,
    config: TutorRLConfig,
) -> dict[str, float]:
    """
    Compute Q-value based metrics.
    
    Args:
        model: Trained CQL model
        transitions: Transition data
        config: Configuration
        
    Returns:
        Dictionary of Q-value metrics
    """
    metrics = {}
    
    try:
        # Q-value for chosen actions
        q_chosen = model.predict_value(
            transitions.observations,
            transitions.actions,
        )
        metrics["mean_q_chosen"] = float(np.mean(q_chosen))
        metrics["std_q_chosen"] = float(np.std(q_chosen))
        metrics["min_q_chosen"] = float(np.min(q_chosen))
        metrics["max_q_chosen"] = float(np.max(q_chosen))
        
        # Q-values for all actions (d3rlpy v2.x compatible)
        all_q = _get_all_q_values(model, transitions.observations, config.action.num_actions)
        
        # Max Q
        max_q = np.max(all_q, axis=1)
        metrics["mean_q_max"] = float(np.mean(max_q))
        
        # Q advantage of chosen vs max
        advantage = q_chosen - max_q
        metrics["mean_q_advantage"] = float(np.mean(advantage))
        
        # Percentage of times behavior policy chose optimal action
        predicted_actions = np.argmax(all_q, axis=1)
        optimal_rate = np.mean(predicted_actions == transitions.actions)
        metrics["behavior_optimal_rate"] = float(optimal_rate)
        
    except Exception as e:
        logger.warning(f"Could not compute Q metrics: {e}")
    
    return metrics


def compute_action_distribution(
    model,
    transitions,
    config: TutorRLConfig,
) -> dict[str, Any]:
    """
    Compare action distributions between learned policy and behavior policy.
    
    Args:
        model: Trained CQL model
        transitions: Transition data
        config: Configuration
        
    Returns:
        Dictionary with action distribution comparison
    """
    metrics = {}
    
    try:
        # Behavior policy distribution
        behavior_counts = Counter(transitions.actions.tolist())
        total = sum(behavior_counts.values())
        behavior_dist = {
            int(k): v / total for k, v in behavior_counts.items()
        }
        
        # Learned policy distribution (greedy)
        all_q = _get_all_q_values(model, transitions.observations, config.action.num_actions)
        learned_actions = np.argmax(all_q, axis=1)
        learned_counts = Counter(learned_actions.tolist())
        learned_dist = {
            int(k): v / total for k, v in learned_counts.items()
        }
        
        metrics["behavior_action_dist"] = behavior_dist
        metrics["learned_action_dist"] = learned_dist
        
        # KL divergence (behavior || learned)
        kl_div = 0.0
        for action in set(behavior_dist.keys()) | set(learned_dist.keys()):
            p = behavior_dist.get(action, 1e-10)
            q = learned_dist.get(action, 1e-10)
            kl_div += p * np.log(p / q)
        metrics["kl_divergence_behavior_learned"] = float(kl_div)
        
        # Total variation distance
        tv_dist = 0.5 * sum(
            abs(behavior_dist.get(a, 0) - learned_dist.get(a, 0))
            for a in set(behavior_dist.keys()) | set(learned_dist.keys())
        )
        metrics["total_variation_distance"] = float(tv_dist)
        
        # No-hint rate comparison
        no_hint = config.action.no_hint_action_id
        metrics["behavior_no_hint_rate"] = behavior_dist.get(no_hint, 0)
        metrics["learned_no_hint_rate"] = learned_dist.get(no_hint, 0)
        
    except Exception as e:
        logger.warning(f"Could not compute action distribution: {e}")
    
    return metrics


def compute_fqe_estimate(
    model,
    transitions,
    config: TutorRLConfig,
    n_iterations: int = 100,
) -> dict[str, float]:
    """
    Compute Fitted Q Evaluation (FQE) estimate of policy value.
    
    FQE is an off-policy evaluation method that learns Q-values for
    the target policy using the offline data.
    
    NOTE: This is a simplified implementation. For production use,
    consider using d3rlpy's built-in FQE evaluator.
    
    Args:
        model: Trained CQL model (target policy)
        transitions: Transition data
        config: Configuration
        n_iterations: Number of FQE iterations
        
    Returns:
        Dictionary with FQE metrics
    """
    metrics = {}
    
    try:
        # Try to use d3rlpy's FQE if available
        try:
            from d3rlpy.ope import FQE, FQEConfig
            
            # d3rlpy v2.x
            fqe_config = FQEConfig(
                learning_rate=1e-4,
                gamma=config.training.gamma,
            )
            fqe = fqe_config.create()
            
            # This would require the full dataset
            logger.info("d3rlpy FQE available but requires full fit - using simple estimate")
            
        except ImportError:
            logger.info("d3rlpy FQE not available, using simple Monte Carlo estimate")
        
        # Simple importance sampling estimate
        # This is a basic implementation - see TODO below
        
        # Get behavior policy probabilities (from data)
        behavior_counts = Counter(transitions.actions.tolist())
        total = len(transitions.actions)
        
        # Get target policy action probabilities (softmax of Q-values)
        all_q = _get_all_q_values(model, transitions.observations, config.action.num_actions)
        
        # Use softmax for stochastic policy
        temperature = 1.0
        exp_q = np.exp((all_q - np.max(all_q, axis=1, keepdims=True)) / temperature)
        target_probs = exp_q / exp_q.sum(axis=1, keepdims=True)
        
        # Importance weights
        target_action_probs = target_probs[np.arange(len(transitions.actions)), transitions.actions]
        behavior_action_probs = np.array([
            behavior_counts[a] / total for a in transitions.actions
        ])
        
        # Clipped importance weights
        importance_weights = np.clip(
            target_action_probs / (behavior_action_probs + 1e-10),
            0.0,
            10.0,  # Clip for stability
        )
        
        # Weighted reward estimate
        weighted_rewards = importance_weights * transitions.rewards
        metrics["is_policy_value_estimate"] = float(np.mean(weighted_rewards))
        metrics["is_estimate_std"] = float(np.std(weighted_rewards) / np.sqrt(len(weighted_rewards)))
        
        # Self-normalized importance sampling
        normalized_weights = importance_weights / importance_weights.sum()
        snips_estimate = np.sum(normalized_weights * transitions.rewards)
        metrics["snips_policy_value_estimate"] = float(snips_estimate)
        
        # Effective sample size
        ess = 1.0 / np.sum(normalized_weights ** 2)
        metrics["effective_sample_size"] = float(ess)
        metrics["ess_ratio"] = float(ess / len(transitions.rewards))
        
    except Exception as e:
        logger.warning(f"Could not compute FQE estimate: {e}")
        metrics["fqe_error"] = str(e)
    
    # TODO: Implement full FQE with Q-function fitting
    # This would involve:
    # 1. Initialize Q_fqe randomly
    # 2. For each iteration:
    #    - Compute targets: r + gamma * Q_fqe(s', pi(s'))
    #    - Update Q_fqe to minimize MSE with targets
    # 3. Return mean Q_fqe(s, pi(s)) over initial states
    
    return metrics


def evaluate_offline(
    model_dir: str | Path,
    data_path: str | Path,
    config_path: Optional[str | Path] = None,
    output_path: Optional[str | Path] = None,
) -> dict[str, Any]:
    """
    Run full offline evaluation.
    
    Args:
        model_dir: Directory containing trained model
        data_path: Path to evaluation data
        config_path: Path to config (default: use config from model_dir)
        output_path: Path to save evaluation report (default: model_dir/eval_report.json)
        
    Returns:
        Dictionary with all evaluation metrics
    """
    model_dir = Path(model_dir)
    
    # Load config
    if config_path:
        config = load_config(config_path)
    else:
        config_file = model_dir / "config.yml"
        if config_file.exists():
            config = load_config(config_file)
        else:
            logger.warning("No config found, using defaults")
            config = TutorRLConfig()
    
    # Load model
    logger.info(f"Loading model from {model_dir}")
    model = load_cql_model(model_dir)
    
    # Load scaler
    scaler = load_scaler(model_dir / "scaler.joblib")
    
    # Load data
    df = load_dataset(data_path)
    
    # Compute rewards if needed
    if config.data.reward_column not in df.columns:
        df = compute_rewards(df, config.reward, config.data, config.action)
    
    # Split data
    _, _, test_df = split_by_session(df, config.data)
    
    # Build transitions
    test_transitions, _ = build_transitions(
        test_df, config.data, scaler=scaler, fit_scaler=False
    )
    
    logger.info(f"Evaluating on {test_transitions.num_transitions} test transitions")
    
    # Compute metrics
    report = {
        "model_dir": str(model_dir),
        "data_path": str(data_path),
        "n_transitions": test_transitions.num_transitions,
        "obs_dim": test_transitions.obs_dim,
    }
    
    # Q-value metrics
    q_metrics = compute_q_metrics(model, test_transitions, config)
    report["q_metrics"] = q_metrics
    
    # Action distribution
    action_metrics = compute_action_distribution(model, test_transitions, config)
    report["action_distribution"] = action_metrics
    
    # FQE estimate
    fqe_metrics = compute_fqe_estimate(model, test_transitions, config)
    report["ope_metrics"] = fqe_metrics
    
    # Summary statistics
    report["summary"] = {
        "mean_reward": float(np.mean(test_transitions.rewards)),
        "mean_q_chosen": q_metrics.get("mean_q_chosen", None),
        "policy_value_estimate": fqe_metrics.get("snips_policy_value_estimate", None),
        "behavior_optimal_rate": q_metrics.get("behavior_optimal_rate", None),
    }
    
    # Save report
    if output_path is None:
        output_path = model_dir / "eval_report.json"
    
    output_path = Path(output_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    
    with open(output_path, "w") as f:
        json.dump(report, f, indent=2, default=str)
    
    logger.info(f"Saved evaluation report to {output_path}")
    
    return report


def main():
    """CLI entry point for evaluation."""
    parser = argparse.ArgumentParser(description="Evaluate CQL hint policy offline")
    parser.add_argument(
        "--model", "-m",
        type=str,
        required=True,
        help="Path to model directory (artifacts/run_*)",
    )
    parser.add_argument(
        "--data", "-d",
        type=str,
        required=True,
        help="Path to data file for evaluation",
    )
    parser.add_argument(
        "--config", "-c",
        type=str,
        default=None,
        help="Path to config YAML (default: use from model dir)",
    )
    parser.add_argument(
        "--output", "-o",
        type=str,
        default=None,
        help="Path to save evaluation report JSON",
    )
    
    args = parser.parse_args()
    
    report = evaluate_offline(
        model_dir=args.model,
        data_path=args.data,
        config_path=args.config,
        output_path=args.output,
    )
    
    print("\n" + "=" * 60)
    print("OFFLINE EVALUATION REPORT")
    print("=" * 60)
    print(f"\nSummary:")
    for k, v in report["summary"].items():
        print(f"  {k}: {v}")
    
    print(f"\nFull report saved to: {args.output or 'model_dir/eval_report.json'}")


if __name__ == "__main__":
    main()

