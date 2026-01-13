"""
CQL Training script for the offline RL hint policy.

Uses d3rlpy's DiscreteCQL (DQN-based CQL for discrete actions).

d3rlpy API Notes:
- v2.x uses `DiscreteCQL` class
- v1.x used `DiscreteCQL` as well but with different API
- We implement version-robust code with fallbacks
"""

from __future__ import annotations

import argparse
import json
import logging
import sys
from datetime import datetime
from pathlib import Path
from typing import Any, Optional

import numpy as np

from tutor_rl.config import TutorRLConfig, load_config
from tutor_rl.data import (
    build_transitions,
    load_dataset,
    save_feature_schema,
    save_scaler,
    split_by_session,
)
from tutor_rl.reward import compute_rewards
from tutor_rl.action_space import ActionSpace

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
)
logger = logging.getLogger(__name__)


def _get_d3rlpy_version() -> tuple[int, int, int]:
    """Get d3rlpy version as tuple."""
    try:
        import d3rlpy
        version = d3rlpy.__version__
        parts = version.split(".")
        return tuple(int(p) for p in parts[:3])
    except Exception:
        return (2, 0, 0)  # Assume v2 as default


def _create_cql_algorithm(config: TutorRLConfig, obs_dim: int, n_actions: int):
    """
    Create CQL algorithm with version-robust code.
    
    d3rlpy v2.x API:
        DiscreteCQL(learning_rate=..., batch_size=..., gamma=..., alpha=...)
    
    For discrete actions, we use DiscreteCQL which is based on DQN.
    CQL adds a conservative regularization term to prevent overestimation
    of Q-values for out-of-distribution actions, which is crucial for
    offline RL where we can't explore.
    """
    try:
        from d3rlpy.algos import DiscreteCQLConfig, DiscreteCQL
        
        # d3rlpy v2.x API
        logger.info("Using d3rlpy v2.x API with DiscreteCQL")
        
        cql_config = DiscreteCQLConfig(
            learning_rate=config.training.learning_rate,
            batch_size=config.training.batch_size,
            gamma=config.training.gamma,
            alpha=config.training.alpha,
        )
        
        return cql_config.create()
        
    except ImportError:
        try:
            # Try v1.x API
            from d3rlpy.algos import DiscreteCQL
            
            logger.info("Using d3rlpy v1.x API with DiscreteCQL")
            
            return DiscreteCQL(
                learning_rate=config.training.learning_rate,
                batch_size=config.training.batch_size,
                gamma=config.training.gamma,
                alpha=config.training.alpha,
                use_gpu=False,  # Can be configured
            )
            
        except ImportError as e:
            logger.error(
                "Failed to import DiscreteCQL from d3rlpy. "
                "Please install d3rlpy: pip install d3rlpy"
            )
            raise ImportError(
                "d3rlpy not found. Install with: pip install d3rlpy>=2.0.0"
            ) from e


def _create_d3rlpy_dataset(transitions, config: TutorRLConfig):
    """
    Create d3rlpy dataset from transitions.
    
    d3rlpy v2.x uses `create_fifo_replay_buffer` or direct MDPDataset.
    """
    try:
        # d3rlpy v2.x
        from d3rlpy.dataset import MDPDataset
        
        dataset = MDPDataset(
            observations=transitions.observations,
            actions=transitions.actions,
            rewards=transitions.rewards,
            terminals=transitions.terminals,
        )

        # logger.info(f"Created MDPDataset with {len(dataset)} transitions")
        return dataset
        
    except ImportError:
        try:
            # d3rlpy v1.x
            from d3rlpy.dataset import MDPDataset
            
            dataset = MDPDataset(
                observations=transitions.observations,
                actions=transitions.actions.reshape(-1, 1),
                rewards=transitions.rewards,
                terminals=transitions.terminals,
            )
            
            return dataset
            
        except ImportError as e:
            raise ImportError("Could not create d3rlpy dataset") from e


def train_cql(
    config: TutorRLConfig,
    data_path: str | Path,
    output_dir: Optional[str | Path] = None,
) -> dict[str, Any]:
    """
    Train CQL model on offline data.
    
    Args:
        config: Training configuration
        data_path: Path to data file
        output_dir: Output directory for artifacts (default: config.artifacts_dir)
        
    Returns:
        Dictionary with training results and metrics
    """
    output_dir = Path(output_dir or config.artifacts_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    run_dir = output_dir / f"run_{timestamp}"
    run_dir.mkdir(parents=True, exist_ok=True)
    
    logger.info(f"Starting CQL training, output: {run_dir}")
    
    # Load data
    df = load_dataset(data_path)
    
    # Compute rewards if not present
    if config.data.reward_column not in df.columns:
        logger.info("Reward column not found, computing from raw logs")
        df = compute_rewards(
            df,
            config.reward,
            config.data,
            config.action,
        )
    
    # Split by session
    train_df, val_df, test_df = split_by_session(df, config.data)
    
    # Build transitions
    train_transitions, scaler = build_transitions(train_df, config.data, fit_scaler=True)
    val_transitions, _ = build_transitions(val_df, config.data, scaler=scaler, fit_scaler=False)
    
    # Save scaler and feature schema
    save_scaler(scaler, run_dir / "scaler.joblib")
    save_feature_schema(config.data.feature_columns, run_dir / "feature_schema.json")
    
    # Create datasets
    train_dataset = _create_d3rlpy_dataset(train_transitions, config)
    val_dataset = _create_d3rlpy_dataset(val_transitions, config)
    
    # Create CQL algorithm
    cql = _create_cql_algorithm(
        config,
        obs_dim=train_transitions.obs_dim,
        n_actions=config.action.num_actions,
    )
    
    # Setup logging directory
    tensorboard_dir = run_dir / "tensorboard" if config.training.log_to_tensorboard else None
    
    # Train
    logger.info(f"Training for {config.training.n_epochs} epochs...")
    
    # Calculate training steps
    n_transitions = train_transitions.num_transitions
    steps_per_epoch = max(1, n_transitions // config.training.batch_size)
    total_steps = config.training.n_epochs * steps_per_epoch
    
    logger.info(f"Training: {n_transitions} transitions, {steps_per_epoch} steps/epoch, {total_steps} total steps")
    
    try:
        # d3rlpy v2.x API - setup logger adapter
        from d3rlpy.logging import FileAdapterFactory
        logger_adapter = FileAdapterFactory(root_dir=str(run_dir))
        
        if tensorboard_dir and config.training.log_to_tensorboard:
            try:
                from d3rlpy.logging import TensorboardAdapterFactory
                # Check if tensorboardX is available
                import tensorboardX
                logger_adapter = TensorboardAdapterFactory(root_dir=str(run_dir))
                logger.info("TensorBoard logging enabled")
            except ImportError as e:
                logger.warning(f"TensorBoard logging disabled: {e}")
        
        # Fit the model
        cql.fit(
            train_dataset,
            n_steps=total_steps,
            n_steps_per_epoch=steps_per_epoch,
            experiment_name="cql_hint_policy",
            with_timestamp=False,
            save_interval=max(1, config.training.save_frequency * steps_per_epoch),
            logger_adapter=logger_adapter,
        )
    except TypeError as e:
        logger.warning(f"d3rlpy v2.x fit failed: {e}, trying alternative API")
        # Try simpler API without optional arguments
        try:
            cql.fit(
                train_dataset,
                n_steps=total_steps,
                n_steps_per_epoch=steps_per_epoch,
            )
        except TypeError:
            # Last resort: even simpler call
            cql.fit(train_dataset, n_steps=total_steps)
    except Exception as e:
        logger.error(f"Training failed: {e}")
        raise
    
    # Save model (d3rlpy v2.x saves to .pt format)
    model_path = run_dir / "model.pt"
    try:
        # d3rlpy v2.x uses save_policy for the model
        cql.save_policy(str(model_path))
        logger.info(f"Saved policy to {model_path}")
    except AttributeError:
        try:
            cql.save(str(model_path))
            logger.info(f"Saved model to {model_path}")
        except Exception:
            try:
                cql.save_model(str(model_path))
                logger.info(f"Saved model to {model_path}")
            except Exception as e:
                logger.warning(f"Could not save model directly: {e}")
    except Exception as e:
        logger.warning(f"Could not save policy directly: {e}")
    
    # d3rlpy v2 also saves model automatically during training - note the location
    d3rlpy_model_dir = run_dir / "cql_hint_policy"
    if d3rlpy_model_dir.exists():
        logger.info(f"d3rlpy auto-saved model to {d3rlpy_model_dir}")
    
    # Save config snapshot
    config.save_yaml(run_dir / "config.yml")
    
    # Save action space mapping
    action_space = ActionSpace(
        num_actions=config.action.num_actions,
        no_hint_action=config.action.no_hint_action_id,
        disallowed_actions=config.action.disallowed_actions,
    )
    action_map = {
        i: action_space.action_name(i)
        for i in range(config.action.num_actions)
    }
    with open(run_dir / "action_map.json", "w") as f:
        json.dump(action_map, f, indent=2)
    
    # Compute final metrics
    metrics = _compute_training_metrics(cql, train_transitions, val_transitions, config)
    
    with open(run_dir / "metrics.json", "w") as f:
        json.dump(metrics, f, indent=2)
    
    logger.info(f"Training complete. Metrics: {metrics}")
    
    return {
        "run_dir": str(run_dir),
        "metrics": metrics,
        "model_path": str(model_path),
    }


def _compute_training_metrics(
    cql,
    train_transitions,
    val_transitions,
    config: TutorRLConfig,
) -> dict[str, float]:
    """Compute training metrics on train and validation sets."""
    metrics = {}
    
    try:
        # Mean reward (always available)
        metrics["train_mean_reward"] = float(np.mean(train_transitions.rewards))
        metrics["val_mean_reward"] = float(np.mean(val_transitions.rewards))
        
        # Try to get Q-values - API varies by version
        try:
            # d3rlpy v2.x: predict returns Q-values for all actions
            train_all_q = cql.predict(train_transitions.observations)
            # Get Q-value for chosen actions
            train_q_values = train_all_q[np.arange(len(train_transitions.actions)), train_transitions.actions]
            metrics["train_mean_q"] = float(np.mean(train_q_values))
            metrics["train_std_q"] = float(np.std(train_q_values))
            
            val_all_q = cql.predict(val_transitions.observations)
            val_q_values = val_all_q[np.arange(len(val_transitions.actions)), val_transitions.actions]
            metrics["val_mean_q"] = float(np.mean(val_q_values))
            metrics["val_std_q"] = float(np.std(val_q_values))
            
        except Exception:
            # Try predict_value if predict didn't work as expected
            try:
                train_q_values = cql.predict_value(
                    train_transitions.observations,
                    train_transitions.actions,
                )
                metrics["train_mean_q"] = float(np.mean(train_q_values))
                metrics["train_std_q"] = float(np.std(train_q_values))
                
                val_q_values = cql.predict_value(
                    val_transitions.observations,
                    val_transitions.actions,
                )
                metrics["val_mean_q"] = float(np.mean(val_q_values))
                metrics["val_std_q"] = float(np.std(val_q_values))
            except Exception as e2:
                logger.warning(f"Could not compute Q-value metrics: {e2}")
        
    except Exception as e:
        logger.warning(f"Could not compute some metrics: {e}")
    
    return metrics


def main():
    """CLI entry point for training."""
    parser = argparse.ArgumentParser(description="Train CQL hint policy")
    parser.add_argument(
        "--config", "-c",
        type=str,
        required=True,
        help="Path to config YAML file",
    )
    parser.add_argument(
        "--data", "-d",
        type=str,
        required=True,
        help="Path to data file (CSV or Parquet)",
    )
    parser.add_argument(
        "--output", "-o",
        type=str,
        default=None,
        help="Output directory for artifacts",
    )
    
    args = parser.parse_args()
    
    config = load_config(args.config)
    
    result = train_cql(
        config=config,
        data_path=args.data,
        output_dir=args.output,
    )
    
    print(f"\nTraining complete!")
    print(f"Artifacts saved to: {result['run_dir']}")
    print(f"Metrics: {json.dumps(result['metrics'], indent=2)}")


if __name__ == "__main__":
    main()

