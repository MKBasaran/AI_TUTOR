"""
Reward computation for the tutor RL system.

Computes rewards from raw interaction logs based on:
- Speed improvement in next K trials
- Escaping stuck states
- Penalty for hint usage
- Penalty for budget exceeded / unsafe actions
"""

from __future__ import annotations

import logging
from typing import Optional

import numpy as np
import pandas as pd

from tutor_rl.config import RewardConfig, DataConfig

logger = logging.getLogger(__name__)


def compute_improvement_reward(
    df: pd.DataFrame,
    config: RewardConfig,
    data_config: DataConfig,
    speed_column: str = "recent_speed",
) -> np.ndarray:
    """
    Compute reward based on speed improvement over next K trials.
    
    For each decision point, looks ahead K steps within the same session
    and computes the maximum improvement in speed.
    
    Args:
        df: DataFrame with interaction logs
        config: Reward configuration
        data_config: Data configuration
        speed_column: Column name for speed metric
        
    Returns:
        Array of improvement rewards
    """
    df = df.copy()
    df = df.sort_values([data_config.session_id_column, data_config.step_idx_column])
    
    rewards = np.zeros(len(df), dtype=np.float32)
    
    for session_id in df[data_config.session_id_column].unique():
        mask = df[data_config.session_id_column] == session_id
        session_df = df[mask]
        indices = np.where(mask)[0]
        speeds = session_df[speed_column].values
        
        for i, idx in enumerate(indices):
            current_speed = speeds[i]
            # Look ahead K steps
            future_speeds = speeds[i + 1:i + 1 + config.lookahead_k]
            
            if len(future_speeds) > 0:
                max_improvement = np.max(future_speeds) - current_speed
                rewards[idx] = max(0, max_improvement) * config.improvement_weight
    
    return rewards


def compute_stuck_escape_reward(
    df: pd.DataFrame,
    config: RewardConfig,
    data_config: DataConfig,
    stuck_column: str = "stuck_prob",
) -> np.ndarray:
    """
    Compute bonus reward for escaping stuck states.
    
    If stuck_prob drops below threshold in the next step, award bonus.
    
    Args:
        df: DataFrame with interaction logs
        config: Reward configuration
        data_config: Data configuration
        stuck_column: Column name for stuck probability
        
    Returns:
        Array of stuck escape rewards
    """
    df = df.copy()
    df = df.sort_values([data_config.session_id_column, data_config.step_idx_column])
    
    rewards = np.zeros(len(df), dtype=np.float32)
    
    for session_id in df[data_config.session_id_column].unique():
        mask = df[data_config.session_id_column] == session_id
        session_df = df[mask]
        indices = np.where(mask)[0]
        stuck_probs = session_df[stuck_column].values
        
        for i, idx in enumerate(indices[:-1]):  # Exclude last step
            current_stuck = stuck_probs[i]
            next_stuck = stuck_probs[i + 1]
            
            # Was stuck, now not stuck
            if current_stuck >= config.stuck_threshold and next_stuck < config.stuck_threshold:
                rewards[idx] = config.stuck_escape_bonus
    
    return rewards


def compute_hint_penalty(
    df: pd.DataFrame,
    config: RewardConfig,
    data_config: DataConfig,
    no_hint_action: int = 0,
) -> np.ndarray:
    """
    Compute penalty for using hints (to encourage minimal hint usage).
    
    Args:
        df: DataFrame with interaction logs
        config: Reward configuration
        data_config: Data configuration
        no_hint_action: Action ID for no hint
        
    Returns:
        Array of hint penalties (negative values)
    """
    actions = df[data_config.action_column].values
    penalties = np.where(actions != no_hint_action, config.hint_penalty, 0.0)
    return penalties.astype(np.float32)


def compute_budget_penalty(
    df: pd.DataFrame,
    config: RewardConfig,
    data_config: DataConfig,
    hint_count_column: str = "hint_count",
    budget_max: int = 10,
    no_hint_action: int = 0,
) -> np.ndarray:
    """
    Compute penalty for exceeding hint budget.
    
    If hint_count >= budget_max and a hint is given, apply large penalty.
    
    Args:
        df: DataFrame with interaction logs
        config: Reward configuration  
        data_config: Data configuration
        hint_count_column: Column for cumulative hint count
        budget_max: Maximum allowed hints
        no_hint_action: Action ID for no hint
        
    Returns:
        Array of budget penalties
    """
    if hint_count_column not in df.columns:
        logger.warning(f"Column '{hint_count_column}' not found, skipping budget penalty")
        return np.zeros(len(df), dtype=np.float32)
    
    hint_counts = df[hint_count_column].values
    actions = df[data_config.action_column].values
    
    # Penalty when over budget AND giving a hint
    over_budget = hint_counts >= budget_max
    is_hint = actions != no_hint_action
    
    penalties = np.where(over_budget & is_hint, config.budget_exceeded_penalty, 0.0)
    return penalties.astype(np.float32)


def compute_unsafe_action_penalty(
    df: pd.DataFrame,
    config: RewardConfig,
    data_config: DataConfig,
    disallowed_actions: list[int],
) -> np.ndarray:
    """
    Compute penalty for unsafe/disallowed actions.
    
    Args:
        df: DataFrame with interaction logs
        config: Reward configuration
        data_config: Data configuration
        disallowed_actions: List of action IDs that are disallowed
        
    Returns:
        Array of unsafe action penalties
    """
    if not disallowed_actions:
        return np.zeros(len(df), dtype=np.float32)
    
    actions = df[data_config.action_column].values
    is_unsafe = np.isin(actions, disallowed_actions)
    
    penalties = np.where(is_unsafe, config.unsafe_action_penalty, 0.0)
    return penalties.astype(np.float32)


def compute_rewards(
    df: pd.DataFrame,
    reward_config: RewardConfig,
    data_config: DataConfig,
    action_config: Optional["ActionConfig"] = None,
    speed_column: str = "recent_speed",
    stuck_column: str = "stuck_prob",
    hint_count_column: str = "hint_count",
    budget_max: int = 10,
) -> pd.DataFrame:
    """
    Compute full reward signal from raw logs.
    
    Combines all reward components:
    1. Improvement reward (positive)
    2. Stuck escape bonus (positive)
    3. Hint usage penalty (negative)
    4. Budget exceeded penalty (negative)
    5. Unsafe action penalty (negative)
    
    Args:
        df: DataFrame with interaction logs (reward column can be missing)
        reward_config: Reward configuration
        data_config: Data configuration
        action_config: Optional action configuration for disallowed actions
        speed_column: Column name for speed metric
        stuck_column: Column name for stuck probability
        hint_count_column: Column for cumulative hint count
        budget_max: Maximum allowed hints
        
    Returns:
        DataFrame with added 'reward' column
    """
    from tutor_rl.config import ActionConfig
    
    if action_config is None:
        action_config = ActionConfig()
    
    df = df.copy()
    
    logger.info("Computing rewards from raw logs...")
    
    # Compute each component
    improvement = compute_improvement_reward(df, reward_config, data_config, speed_column)
    stuck_escape = compute_stuck_escape_reward(df, reward_config, data_config, stuck_column)
    hint_penalty = compute_hint_penalty(df, reward_config, data_config, action_config.no_hint_action_id)
    budget_penalty = compute_budget_penalty(
        df, reward_config, data_config, hint_count_column, budget_max, action_config.no_hint_action_id
    )
    unsafe_penalty = compute_unsafe_action_penalty(
        df, reward_config, data_config, action_config.disallowed_actions
    )
    
    # Combine
    total_reward = improvement + stuck_escape + hint_penalty + budget_penalty + unsafe_penalty
    
    df[data_config.reward_column] = total_reward
    
    # Log statistics
    logger.info(f"Reward stats: mean={total_reward.mean():.3f}, std={total_reward.std():.3f}, "
                f"min={total_reward.min():.3f}, max={total_reward.max():.3f}")
    logger.info(f"  Improvement: mean={improvement.mean():.3f}")
    logger.info(f"  Stuck escape: mean={stuck_escape.mean():.3f}")
    logger.info(f"  Hint penalty: mean={hint_penalty.mean():.3f}")
    logger.info(f"  Budget penalty: mean={budget_penalty.mean():.3f}")
    logger.info(f"  Unsafe penalty: mean={unsafe_penalty.mean():.3f}")
    
    return df

