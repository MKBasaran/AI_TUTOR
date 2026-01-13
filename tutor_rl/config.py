"""
Configuration management for the tutor RL system.

Uses Pydantic for validation and YAML for file-based config.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Optional

import yaml

logger = logging.getLogger(__name__)


@dataclass
class DataConfig:
    """Configuration for data loading and preprocessing."""
    
    feature_columns: list[str] = field(default_factory=lambda: [
        "freq_0", "amp_0", "offset_0", "phase_0",
        "freq_1", "amp_1", "offset_1", "phase_1",
        "freq_2", "amp_2", "offset_2", "phase_2",
        "recent_speed", "recent_speed_delta", "stuck_prob",
        "trial_count", "hint_count", "time_in_session"
    ])
    session_id_column: str = "session_id"
    step_idx_column: str = "step_idx"
    action_column: str = "action"
    reward_column: str = "reward"
    done_column: str = "done"
    next_feature_prefix: str = "next_"
    train_ratio: float = 0.7
    val_ratio: float = 0.15
    test_ratio: float = 0.15
    random_seed: int = 42


@dataclass
class RewardConfig:
    """Configuration for reward computation."""
    
    # Reward weights
    improvement_weight: float = 1.0
    stuck_escape_bonus: float = 2.0
    hint_penalty: float = -0.1
    budget_exceeded_penalty: float = -5.0
    unsafe_action_penalty: float = -10.0
    
    # Lookahead for improvement calculation
    lookahead_k: int = 3
    
    # Thresholds
    stuck_threshold: float = 0.7
    improvement_threshold: float = 0.05


@dataclass 
class ActionConfig:
    """Configuration for action space."""
    
    num_actions: int = 8
    no_hint_action_id: int = 0
    disallowed_actions: list[int] = field(default_factory=list)


@dataclass
class TrainingConfig:
    """Configuration for CQL training."""
    
    # CQL hyperparameters
    learning_rate: float = 3e-4
    batch_size: int = 256
    n_epochs: int = 100
    gamma: float = 0.99
    
    # CQL-specific
    alpha: float = 1.0  # Conservative penalty weight
    
    # Model architecture
    hidden_sizes: list[int] = field(default_factory=lambda: [256, 256])
    
    # Logging and checkpointing
    eval_frequency: int = 10
    save_frequency: int = 20
    log_to_tensorboard: bool = True


@dataclass
class SafetyConfig:
    """Configuration for safety wrapper."""
    
    hint_budget_max: int = 10
    stuck_prob_threshold: float = 0.3
    improvement_threshold: float = 0.0
    q_value_margin: float = 0.5  # Model must prefer hint by this margin


@dataclass
class TutorRLConfig:
    """Main configuration container."""
    
    data: DataConfig = field(default_factory=DataConfig)
    reward: RewardConfig = field(default_factory=RewardConfig)
    action: ActionConfig = field(default_factory=ActionConfig)
    training: TrainingConfig = field(default_factory=TrainingConfig)
    safety: SafetyConfig = field(default_factory=SafetyConfig)
    
    # Paths
    artifacts_dir: str = "artifacts"
    
    @classmethod
    def from_yaml(cls, path: str | Path) -> TutorRLConfig:
        """Load configuration from a YAML file."""
        path = Path(path)
        if not path.exists():
            raise FileNotFoundError(f"Config file not found: {path}")
        
        with open(path, "r") as f:
            raw = yaml.safe_load(f)
        
        return cls.from_dict(raw)
    
    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> TutorRLConfig:
        """Create config from a dictionary."""
        return cls(
            data=DataConfig(**d.get("data", {})),
            reward=RewardConfig(**d.get("reward", {})),
            action=ActionConfig(**d.get("action", {})),
            training=TrainingConfig(**d.get("training", {})),
            safety=SafetyConfig(**d.get("safety", {})),
            artifacts_dir=d.get("artifacts_dir", "artifacts"),
        )
    
    def to_dict(self) -> dict[str, Any]:
        """Convert config to a dictionary."""
        import dataclasses
        
        def _asdict_recursive(obj: Any) -> Any:
            if dataclasses.is_dataclass(obj):
                return {k: _asdict_recursive(v) for k, v in dataclasses.asdict(obj).items()}
            return obj
        
        return {
            "data": _asdict_recursive(self.data),
            "reward": _asdict_recursive(self.reward),
            "action": _asdict_recursive(self.action),
            "training": _asdict_recursive(self.training),
            "safety": _asdict_recursive(self.safety),
            "artifacts_dir": self.artifacts_dir,
        }
    
    def save_yaml(self, path: str | Path) -> None:
        """Save configuration to a YAML file."""
        path = Path(path)
        path.parent.mkdir(parents=True, exist_ok=True)
        
        with open(path, "w") as f:
            yaml.dump(self.to_dict(), f, default_flow_style=False, sort_keys=False)
        
        logger.info(f"Saved config to {path}")


def load_config(path: Optional[str | Path] = None) -> TutorRLConfig:
    """Load config from path or return defaults."""
    if path is None:
        logger.info("Using default configuration")
        return TutorRLConfig()
    return TutorRLConfig.from_yaml(path)

