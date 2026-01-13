"""
Command-line interface for tutor_rl package.

Provides unified CLI for training, evaluation, and demo.
"""

from __future__ import annotations

import argparse
import json
import logging
import sys
from pathlib import Path

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
)
logger = logging.getLogger(__name__)


def cmd_train(args: argparse.Namespace) -> int:
    """Train CQL model."""
    from tutor_rl.config import load_config
    from tutor_rl.train_cql import train_cql
    
    config = load_config(args.config)
    
    result = train_cql(
        config=config,
        data_path=args.data,
        output_dir=args.output,
    )
    
    print(f"\nTraining complete!")
    print(f"Artifacts saved to: {result['run_dir']}")
    print(f"Metrics: {json.dumps(result['metrics'], indent=2)}")
    
    return 0


def cmd_eval(args: argparse.Namespace) -> int:
    """Evaluate model offline."""
    from tutor_rl.eval_offline import evaluate_offline
    
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
    
    return 0


def cmd_demo(args: argparse.Namespace) -> int:
    """Demo inference with a single observation."""
    from tutor_rl.policy import HintPolicy, SafetyContext, create_mock_policy
    
    # Parse features from command line
    features = {}
    if args.row:
        for item in args.row.split(","):
            if "=" in item:
                key, val = item.split("=", 1)
                try:
                    features[key.strip()] = float(val.strip())
                except ValueError:
                    print(f"Warning: Could not parse '{item}' as float")
    
    # Store original features for context (before potential remapping)
    original_features = features.copy()
    
    # Load or create policy
    if args.model:
        policy = HintPolicy.load(args.model, seed=args.seed)
        
        # Fill in missing features with defaults for trained models
        missing_features = set(policy.feature_columns) - set(features.keys())
        if missing_features:
            print(f"Note: Filling {len(missing_features)} missing features with default value 0.0")
            for col in policy.feature_columns:
                if col not in features:
                    features[col] = 0.0
    else:
        print("No model specified, using mock policy for demo")
        policy = create_mock_policy(num_features=18, seed=args.seed)
        # For mock policy, fill in all required features with defaults
        mock_features = {f"feature_{i}": 0.0 for i in range(18)}
        # Map provided features to mock feature indices
        for i, (key, val) in enumerate(features.items()):
            if i < 18:
                mock_features[f"feature_{i}"] = val
        features = mock_features
        print(f"Original inputs: {original_features}")
    
    # For safety context, use original input values
    original_stuck = original_features.get("stuck_prob", 0.0)
    original_delta = original_features.get("recent_speed_delta", 0.0)
    original_trials = original_features.get("trial_count", 0)
    
    # Create safety context
    safety_ctx = SafetyContext(
        hint_budget_used=args.hints_used,
        stuck_prob=original_stuck,
        recent_improvement=original_delta,
    )
    
    # Predict action
    action, q_values = policy.predict_action(
        features,
        safety_context=safety_ctx,
        return_q_values=True,
    )
    
    # Generate hint
    context = {
        "trial_count": original_trials,
        "hint_count": args.hints_used,
        "stuck": original_stuck > 0.7,
        "improving": original_delta > 0,
    }
    hint = policy.generate_hint(action, context)
    
    # Print results
    print("\n" + "=" * 60)
    print("HINT POLICY DEMO")
    print("=" * 60)
    print(f"\nInput features: {features}")
    print(f"Safety context: hints_used={args.hints_used}")
    print(f"\nQ-values:")
    for i, q in enumerate(q_values):
        marker = " <-- CHOSEN" if i == action else ""
        print(f"  Action {i} ({policy.action_space.action_name(i)}): {q:.4f}{marker}")
    
    print(f"\nChosen action: {action} ({policy.action_space.action_name(action)})")
    print(f"\nGenerated hint:")
    if hint:
        print(f'  "{hint}"')
    else:
        print("  (No hint - action is NO_HINT)")
    
    return 0


def main() -> int:
    """Main CLI entry point."""
    parser = argparse.ArgumentParser(
        prog="tutor_rl",
        description="Offline RL Hint-Generation Policy using CQL",
    )
    subparsers = parser.add_subparsers(dest="command", help="Available commands")
    
    # Train command
    train_parser = subparsers.add_parser("train", help="Train CQL model")
    train_parser.add_argument(
        "--config", "-c",
        type=str,
        required=True,
        help="Path to config YAML file",
    )
    train_parser.add_argument(
        "--data", "-d",
        type=str,
        required=True,
        help="Path to data file (CSV or Parquet)",
    )
    train_parser.add_argument(
        "--output", "-o",
        type=str,
        default=None,
        help="Output directory for artifacts",
    )
    
    # Eval command
    eval_parser = subparsers.add_parser("eval", help="Evaluate model offline")
    eval_parser.add_argument(
        "--model", "-m",
        type=str,
        required=True,
        help="Path to model directory",
    )
    eval_parser.add_argument(
        "--data", "-d",
        type=str,
        required=True,
        help="Path to data file",
    )
    eval_parser.add_argument(
        "--config", "-c",
        type=str,
        default=None,
        help="Path to config YAML (optional)",
    )
    eval_parser.add_argument(
        "--output", "-o",
        type=str,
        default=None,
        help="Path for output report JSON",
    )
    
    # Demo command
    demo_parser = subparsers.add_parser("demo", help="Demo single inference")
    demo_parser.add_argument(
        "--model", "-m",
        type=str,
        default=None,
        help="Path to model directory (optional, uses mock if not provided)",
    )
    demo_parser.add_argument(
        "--row", "-r",
        type=str,
        default="recent_speed_delta=0.1,stuck_prob=0.2,trial_count=5",
        help="Feature values as key=value,key=value",
    )
    demo_parser.add_argument(
        "--hints-used",
        type=int,
        default=0,
        help="Number of hints already used in session",
    )
    demo_parser.add_argument(
        "--seed", "-s",
        type=int,
        default=42,
        help="Random seed for deterministic inference",
    )
    
    args = parser.parse_args()
    
    if args.command is None:
        parser.print_help()
        return 1
    
    if args.command == "train":
        return cmd_train(args)
    elif args.command == "eval":
        return cmd_eval(args)
    elif args.command == "demo":
        return cmd_demo(args)
    else:
        parser.print_help()
        return 1


if __name__ == "__main__":
    sys.exit(main())

