"""
Tests for data loading and transition building.
"""

import numpy as np
import pandas as pd
import pytest

from tutor_rl.config import DataConfig
from tutor_rl.data import (
    build_transitions,
    validate_dataframe,
    _compute_next_features,
    split_by_session,
)


@pytest.fixture
def sample_dataframe() -> pd.DataFrame:
    """Create a sample DataFrame for testing."""
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
                # Feature columns
                "freq_0": np.random.uniform(0.5, 2.0),
                "amp_0": np.random.uniform(0.1, 1.0),
                "offset_0": np.random.uniform(-0.5, 0.5),
                "phase_0": np.random.uniform(0, 2 * np.pi),
                "freq_1": np.random.uniform(0.5, 2.0),
                "amp_1": np.random.uniform(0.1, 1.0),
                "offset_1": np.random.uniform(-0.5, 0.5),
                "phase_1": np.random.uniform(0, 2 * np.pi),
                "freq_2": np.random.uniform(0.5, 2.0),
                "amp_2": np.random.uniform(0.1, 1.0),
                "offset_2": np.random.uniform(-0.5, 0.5),
                "phase_2": np.random.uniform(0, 2 * np.pi),
                "recent_speed": np.random.uniform(0, 1),
                "recent_speed_delta": np.random.uniform(-0.2, 0.2),
                "stuck_prob": np.random.uniform(0, 1),
                "trial_count": step_idx,
                "hint_count": np.random.randint(0, 5),
                "time_in_session": step_idx * 30,
                # RL columns
                "action": np.random.randint(0, 8),
                "reward": np.random.uniform(-1, 1),
                "done": step_idx == steps_per_session - 1,
            }
            data.append(row)
    
    return pd.DataFrame(data)


@pytest.fixture
def data_config() -> DataConfig:
    """Create default data config for testing."""
    return DataConfig()


class TestValidateDataframe:
    """Tests for dataframe validation."""
    
    def test_valid_dataframe_passes(self, sample_dataframe, data_config):
        """Valid dataframe should pass validation."""
        validate_dataframe(sample_dataframe, data_config)
    
    def test_missing_required_column_fails(self, sample_dataframe, data_config):
        """Missing required column should raise ValueError."""
        df = sample_dataframe.drop(columns=["action"])
        
        with pytest.raises(ValueError, match="Missing required columns"):
            validate_dataframe(df, data_config)
    
    def test_missing_feature_column_fails(self, sample_dataframe, data_config):
        """Missing feature column should raise ValueError."""
        df = sample_dataframe.drop(columns=["freq_0"])
        
        with pytest.raises(ValueError, match="Missing feature columns"):
            validate_dataframe(df, data_config)
    
    def test_nan_in_required_column_fails(self, sample_dataframe, data_config):
        """NaN in required column should raise ValueError."""
        df = sample_dataframe.copy()
        df.loc[0, "action"] = np.nan
        
        with pytest.raises(ValueError, match="has .* NaN values"):
            validate_dataframe(df, data_config)


class TestComputeNextFeatures:
    """Tests for next feature computation."""
    
    def test_next_features_shift_correctly(self, sample_dataframe, data_config):
        """Next features should be shifted by 1 step within session."""
        df = _compute_next_features(sample_dataframe, data_config)
        
        # Check a specific session
        session_df = df[df["session_id"] == "session_0"].sort_values("step_idx")
        
        for i in range(len(session_df) - 1):
            current_row = session_df.iloc[i]
            next_row = session_df.iloc[i + 1]
            
            # next_freq_0 at step i should equal freq_0 at step i+1
            assert np.isclose(
                current_row[f"{data_config.next_feature_prefix}freq_0"],
                next_row["freq_0"],
            ), f"Next feature mismatch at step {i}"
    
    def test_last_step_next_features_equal_current(self, sample_dataframe, data_config):
        """Last step's next features should equal current features (terminal)."""
        df = _compute_next_features(sample_dataframe, data_config)
        
        # Get last step of each session
        for session_id in df["session_id"].unique():
            session_df = df[df["session_id"] == session_id].sort_values("step_idx")
            last_row = session_df.iloc[-1]
            
            for col in data_config.feature_columns:
                next_col = f"{data_config.next_feature_prefix}{col}"
                assert np.isclose(
                    last_row[col],
                    last_row[next_col],
                ), f"Terminal next feature mismatch for {col}"


class TestBuildTransitions:
    """Tests for transition building."""
    
    def test_transition_shapes(self, sample_dataframe, data_config):
        """Transitions should have correct shapes."""
        transitions, scaler = build_transitions(sample_dataframe, data_config)
        
        n_transitions = len(sample_dataframe)
        n_features = len(data_config.feature_columns)
        
        assert transitions.observations.shape == (n_transitions, n_features)
        assert transitions.next_observations.shape == (n_transitions, n_features)
        assert transitions.actions.shape == (n_transitions,)
        assert transitions.rewards.shape == (n_transitions,)
        assert transitions.terminals.shape == (n_transitions,)
    
    def test_observations_normalized(self, sample_dataframe, data_config):
        """Observations should be normalized (approximately zero mean, unit std)."""
        transitions, scaler = build_transitions(sample_dataframe, data_config)
        
        # Check normalization (should be close to 0 mean, 1 std after transform)
        mean = np.mean(transitions.observations, axis=0)
        std = np.std(transitions.observations, axis=0)
        
        assert np.allclose(mean, 0, atol=0.1), f"Mean not near zero: {mean}"
        assert np.allclose(std, 1, atol=0.2), f"Std not near one: {std}"
    
    def test_scaler_saved_correctly(self, sample_dataframe, data_config, tmp_path):
        """Scaler should be usable after saving/loading."""
        from tutor_rl.data import save_scaler, load_scaler
        
        transitions, scaler = build_transitions(sample_dataframe, data_config)
        
        scaler_path = tmp_path / "scaler.joblib"
        save_scaler(scaler, scaler_path)
        loaded_scaler = load_scaler(scaler_path)
        
        # Test transform produces same results
        raw_features = sample_dataframe[data_config.feature_columns].values[:5]
        
        original_transform = scaler.transform(raw_features)
        loaded_transform = loaded_scaler.transform(raw_features)
        
        assert np.allclose(original_transform, loaded_transform)


class TestSplitBySession:
    """Tests for session-based splitting."""
    
    def test_no_session_overlap(self, sample_dataframe, data_config):
        """Train/val/test should have no overlapping sessions."""
        train_df, val_df, test_df = split_by_session(sample_dataframe, data_config)
        
        train_sessions = set(train_df["session_id"].unique())
        val_sessions = set(val_df["session_id"].unique())
        test_sessions = set(test_df["session_id"].unique())
        
        assert len(train_sessions & val_sessions) == 0, "Train/val session overlap"
        assert len(train_sessions & test_sessions) == 0, "Train/test session overlap"
        assert len(val_sessions & test_sessions) == 0, "Val/test session overlap"
    
    def test_all_data_included(self, sample_dataframe, data_config):
        """All data should be included in some split."""
        train_df, val_df, test_df = split_by_session(sample_dataframe, data_config)
        
        total_rows = len(train_df) + len(val_df) + len(test_df)
        assert total_rows == len(sample_dataframe), "Some data missing from splits"
    
    def test_reproducible_with_seed(self, sample_dataframe, data_config):
        """Same seed should produce same split."""
        train1, val1, test1 = split_by_session(sample_dataframe, data_config, seed=42)
        train2, val2, test2 = split_by_session(sample_dataframe, data_config, seed=42)
        
        assert set(train1["session_id"].unique()) == set(train2["session_id"].unique())
        assert set(val1["session_id"].unique()) == set(val2["session_id"].unique())
        assert set(test1["session_id"].unique()) == set(test2["session_id"].unique())

