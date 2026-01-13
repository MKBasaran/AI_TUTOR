"""
Pytest configuration and shared fixtures.
"""

import numpy as np
import pandas as pd
import pytest

from tutor_rl.config import DataConfig, TutorRLConfig


@pytest.fixture
def default_config() -> TutorRLConfig:
    """Create default configuration for testing."""
    return TutorRLConfig()


@pytest.fixture
def data_config() -> DataConfig:
    """Create default data config for testing."""
    return DataConfig()


@pytest.fixture
def sample_dataframe(data_config: DataConfig) -> pd.DataFrame:
    """Create a sample DataFrame with all required columns."""
    np.random.seed(42)
    n_sessions = 5
    steps_per_session = 10
    
    data = []
    for session_idx in range(n_sessions):
        for step_idx in range(steps_per_session):
            row = {
                "session_id": f"session_{session_idx}",
                "step_idx": step_idx,
                "timestamp": f"2024-01-01 12:{step_idx:02d}:00",
            }
            
            # Add feature columns
            for col in data_config.feature_columns:
                if "freq" in col:
                    row[col] = np.random.uniform(0.5, 2.0)
                elif "amp" in col:
                    row[col] = np.random.uniform(0.1, 1.0)
                elif "offset" in col:
                    row[col] = np.random.uniform(-0.5, 0.5)
                elif "phase" in col:
                    row[col] = np.random.uniform(0, 2 * np.pi)
                elif col == "recent_speed":
                    row[col] = np.random.uniform(0, 1)
                elif col == "recent_speed_delta":
                    row[col] = np.random.uniform(-0.2, 0.2)
                elif col == "stuck_prob":
                    row[col] = np.random.uniform(0, 1)
                elif col == "trial_count":
                    row[col] = step_idx
                elif col == "hint_count":
                    row[col] = np.random.randint(0, 5)
                elif col == "time_in_session":
                    row[col] = step_idx * 30
                else:
                    row[col] = np.random.uniform(-1, 1)
            
            # RL columns
            row["action"] = np.random.randint(0, 8)
            row["reward"] = np.random.uniform(-1, 1)
            row["done"] = step_idx == steps_per_session - 1
            
            data.append(row)
    
    return pd.DataFrame(data)


@pytest.fixture
def sample_features() -> dict[str, float]:
    """Create sample feature dictionary for policy testing."""
    return {f"feature_{i}": np.random.uniform(-1, 1) for i in range(18)}

