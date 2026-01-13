"""
Data loading and preprocessing for offline RL.

Handles loading from CSV/Parquet, building transitions, normalization,
and validation.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Optional, Tuple

import numpy as np
import pandas as pd
from sklearn.preprocessing import StandardScaler
import joblib

from tutor_rl.config import DataConfig, TutorRLConfig

logger = logging.getLogger(__name__)


@dataclass
class TransitionData:
    """Container for transition data in RL format."""
    
    observations: np.ndarray  # (N, obs_dim)
    actions: np.ndarray       # (N,)
    rewards: np.ndarray       # (N,)
    next_observations: np.ndarray  # (N, obs_dim)
    terminals: np.ndarray     # (N,) boolean
    
    def __post_init__(self) -> None:
        """Validate shapes."""
        n = len(self.observations)
        assert len(self.actions) == n, f"Actions length mismatch: {len(self.actions)} vs {n}"
        assert len(self.rewards) == n, f"Rewards length mismatch: {len(self.rewards)} vs {n}"
        assert len(self.next_observations) == n, f"Next obs length mismatch: {len(self.next_observations)} vs {n}"
        assert len(self.terminals) == n, f"Terminals length mismatch: {len(self.terminals)} vs {n}"
        assert self.observations.shape[1] == self.next_observations.shape[1], "Obs dim mismatch"
    
    @property
    def num_transitions(self) -> int:
        return len(self.observations)
    
    @property
    def obs_dim(self) -> int:
        return self.observations.shape[1]


def load_dataset(path: str | Path) -> pd.DataFrame:
    """
    Load student interaction logs from CSV or Parquet.
    
    Args:
        path: Path to the data file (.csv or .parquet)
        
    Returns:
        DataFrame with interaction logs
        
    Raises:
        FileNotFoundError: If file doesn't exist
        ValueError: If file format is unsupported
    """
    path = Path(path)
    
    if not path.exists():
        raise FileNotFoundError(f"Data file not found: {path}")
    
    suffix = path.suffix.lower()
    
    if suffix == ".csv":
        df = pd.read_csv(path)
    elif suffix in (".parquet", ".pq"):
        df = pd.read_parquet(path)
    else:
        raise ValueError(f"Unsupported file format: {suffix}. Use .csv or .parquet")
    
    logger.info(f"Loaded {len(df)} rows from {path}")
    return df


def validate_dataframe(
    df: pd.DataFrame,
    config: DataConfig,
    require_reward: bool = True,
) -> None:
    """
    Validate that DataFrame has required columns and no critical missing values.
    
    Args:
        df: DataFrame to validate
        config: Data configuration
        require_reward: Whether reward column is required
        
    Raises:
        ValueError: If validation fails
    """
    required_cols = [
        config.session_id_column,
        config.step_idx_column,
        config.action_column,
        config.done_column,
    ]
    
    if require_reward:
        required_cols.append(config.reward_column)
    
    # Check required columns exist
    missing = set(required_cols) - set(df.columns)
    if missing:
        raise ValueError(f"Missing required columns: {missing}")
    
    # Check feature columns
    missing_features = set(config.feature_columns) - set(df.columns)
    if missing_features:
        raise ValueError(f"Missing feature columns: {missing_features}")
    
    # Check for NaN in critical columns
    for col in required_cols:
        nan_count = df[col].isna().sum()
        if nan_count > 0:
            raise ValueError(f"Column '{col}' has {nan_count} NaN values")
    
    # Check feature NaN (allow some but warn)
    for col in config.feature_columns:
        nan_count = df[col].isna().sum()
        if nan_count > 0:
            logger.warning(f"Feature '{col}' has {nan_count} NaN values ({nan_count/len(df)*100:.1f}%)")
    
    # Check action values are non-negative integers
    if not pd.api.types.is_integer_dtype(df[config.action_column]):
        raise ValueError(f"Action column must be integer type")
    
    if (df[config.action_column] < 0).any():
        raise ValueError("Action values must be non-negative")
    
    logger.info("DataFrame validation passed")


def _has_next_features(df: pd.DataFrame, config: DataConfig) -> bool:
    """Check if next_* feature columns are present."""
    next_cols = [f"{config.next_feature_prefix}{col}" for col in config.feature_columns]
    return all(col in df.columns for col in next_cols)


def _compute_next_features(df: pd.DataFrame, config: DataConfig) -> pd.DataFrame:
    """
    Compute next features by shifting within each session.
    
    For the last step in each session, next_features = current features
    (terminal state, won't be used for bootstrapping anyway).
    """
    df = df.copy()
    df = df.sort_values([config.session_id_column, config.step_idx_column])
    
    for col in config.feature_columns:
        next_col = f"{config.next_feature_prefix}{col}"
        # Shift within each session
        df[next_col] = df.groupby(config.session_id_column)[col].shift(-1)
        # Fill last step's next_feature with current feature
        df[next_col] = df[next_col].fillna(df[col])
    
    return df


def build_transitions(
    df: pd.DataFrame,
    config: DataConfig,
    scaler: Optional[StandardScaler] = None,
    fit_scaler: bool = True,
) -> Tuple[TransitionData, StandardScaler]:
    """
    Build transition tuples from DataFrame for offline RL.
    
    Args:
        df: DataFrame with interaction logs
        config: Data configuration
        scaler: Optional pre-fitted scaler. If None and fit_scaler=True, a new one is fitted.
        fit_scaler: Whether to fit the scaler on this data
        
    Returns:
        Tuple of (TransitionData, fitted StandardScaler)
    """
    validate_dataframe(df, config)
    
    # Compute next features if not present
    if not _has_next_features(df, config):
        logger.info("Computing next features by shifting within sessions")
        df = _compute_next_features(df, config)
    
    # Extract arrays
    observations = df[config.feature_columns].values.astype(np.float32)
    
    next_feature_cols = [f"{config.next_feature_prefix}{col}" for col in config.feature_columns]
    next_observations = df[next_feature_cols].values.astype(np.float32)
    
    actions = df[config.action_column].values.astype(np.int64)
    rewards = df[config.reward_column].values.astype(np.float32)
    terminals = df[config.done_column].values.astype(bool)
    
    # Handle any remaining NaN by filling with 0 (after warning)
    nan_mask = np.isnan(observations)
    if nan_mask.any():
        logger.warning(f"Filling {nan_mask.sum()} NaN values in observations with 0")
        observations = np.nan_to_num(observations, nan=0.0)
    
    nan_mask = np.isnan(next_observations)
    if nan_mask.any():
        logger.warning(f"Filling {nan_mask.sum()} NaN values in next_observations with 0")
        next_observations = np.nan_to_num(next_observations, nan=0.0)
    
    # Normalize observations
    if scaler is None:
        scaler = StandardScaler()
    
    if fit_scaler:
        scaler.fit(observations)
        logger.info("Fitted StandardScaler on observations")
    
    observations = scaler.transform(observations).astype(np.float32)
    next_observations = scaler.transform(next_observations).astype(np.float32)
    
    transitions = TransitionData(
        observations=observations,
        actions=actions,
        rewards=rewards,
        next_observations=next_observations,
        terminals=terminals,
    )
    
    logger.info(f"Built {transitions.num_transitions} transitions with obs_dim={transitions.obs_dim}")
    
    return transitions, scaler


def split_by_session(
    df: pd.DataFrame,
    config: DataConfig,
    seed: Optional[int] = None,
) -> Tuple[pd.DataFrame, pd.DataFrame, pd.DataFrame]:
    """
    Split DataFrame into train/val/test by session_id.
    
    This ensures no session appears in multiple splits (prevents data leakage).
    
    Args:
        df: DataFrame to split
        config: Data configuration with split ratios
        seed: Random seed for reproducibility
        
    Returns:
        Tuple of (train_df, val_df, test_df)
    """
    rng = np.random.default_rng(seed or config.random_seed)
    
    session_ids = df[config.session_id_column].unique()
    rng.shuffle(session_ids)
    
    n_sessions = len(session_ids)
    n_train = int(n_sessions * config.train_ratio)
    n_val = int(n_sessions * config.val_ratio)
    
    train_sessions = set(session_ids[:n_train])
    val_sessions = set(session_ids[n_train:n_train + n_val])
    test_sessions = set(session_ids[n_train + n_val:])
    
    train_df = df[df[config.session_id_column].isin(train_sessions)]
    val_df = df[df[config.session_id_column].isin(val_sessions)]
    test_df = df[df[config.session_id_column].isin(test_sessions)]
    
    logger.info(f"Split: train={len(train_df)} ({len(train_sessions)} sessions), "
                f"val={len(val_df)} ({len(val_sessions)} sessions), "
                f"test={len(test_df)} ({len(test_sessions)} sessions)")
    
    return train_df, val_df, test_df


def save_scaler(scaler: StandardScaler, path: str | Path) -> None:
    """Save fitted scaler to disk."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    joblib.dump(scaler, path)
    logger.info(f"Saved scaler to {path}")


def load_scaler(path: str | Path) -> StandardScaler:
    """Load scaler from disk."""
    path = Path(path)
    if not path.exists():
        raise FileNotFoundError(f"Scaler file not found: {path}")
    scaler = joblib.load(path)
    logger.info(f"Loaded scaler from {path}")
    return scaler


def save_feature_schema(feature_columns: list[str], path: str | Path) -> None:
    """Save feature column names for inference."""
    import json
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w") as f:
        json.dump({"feature_columns": feature_columns}, f, indent=2)
    logger.info(f"Saved feature schema to {path}")


def load_feature_schema(path: str | Path) -> list[str]:
    """Load feature column names."""
    import json
    path = Path(path)
    if not path.exists():
        raise FileNotFoundError(f"Feature schema file not found: {path}")
    with open(path, "r") as f:
        data = json.load(f)
    return data["feature_columns"]

