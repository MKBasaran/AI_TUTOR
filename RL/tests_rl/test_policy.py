"""
Tests for policy inference.
"""

import numpy as np
import pytest

from tutor_rl.policy import HintPolicy, SafetyContext, create_mock_policy
from tutor_rl.action_space import HintAction


@pytest.fixture
def mock_policy() -> HintPolicy:
    """Create a mock policy for testing."""
    return create_mock_policy(num_features=18, num_actions=8, seed=42)


@pytest.fixture
def sample_features() -> dict[str, float]:
    """Create sample features for testing."""
    return {f"feature_{i}": np.random.uniform(-1, 1) for i in range(18)}


class TestHintPolicyPrediction:
    """Tests for action prediction."""
    
    def test_predict_returns_valid_action(self, mock_policy, sample_features):
        """Prediction should return valid action ID."""
        action = mock_policy.predict_action(sample_features)
        
        assert isinstance(action, int)
        assert 0 <= action < mock_policy.action_space.num_actions
    
    def test_predict_with_q_values(self, mock_policy, sample_features):
        """Should return Q-values when requested."""
        action, q_values = mock_policy.predict_action(
            sample_features,
            return_q_values=True,
        )
        
        assert isinstance(q_values, np.ndarray)
        assert len(q_values) == mock_policy.action_space.num_actions
    
    def test_deterministic_with_seed(self):
        """Same seed should produce same predictions."""
        policy1 = create_mock_policy(seed=42)
        policy2 = create_mock_policy(seed=42)
        
        features = {f"feature_{i}": 0.5 for i in range(18)}
        
        action1 = policy1.predict_action(features)
        action2 = policy2.predict_action(features)
        
        assert action1 == action2
    
    def test_missing_feature_raises(self, mock_policy):
        """Missing feature should raise ValueError."""
        incomplete_features = {f"feature_{i}": 0.5 for i in range(10)}  # Missing some
        
        with pytest.raises(ValueError, match="Missing features"):
            mock_policy.predict_action(incomplete_features)


class TestSafetyWrapper:
    """Tests for safety wrapper functionality."""
    
    def test_budget_exceeded_forces_no_hint(self, mock_policy, sample_features):
        """When budget is exhausted, should return NO_HINT."""
        # Set budget to 10, hints_used to 10
        safety_ctx = SafetyContext(hint_budget_used=10)
        
        action = mock_policy.predict_action(sample_features, safety_context=safety_ctx)
        
        assert action == mock_policy.action_space.no_hint_action
    
    def test_budget_not_exceeded_allows_hints(self, mock_policy, sample_features):
        """When budget not exhausted, hints should be allowed."""
        safety_ctx = SafetyContext(hint_budget_used=5)  # Under budget
        
        # Run multiple times - at least some should be hints
        actions = [
            mock_policy.predict_action_stochastic(
                sample_features, 
                temperature=0.5,
                safety_context=safety_ctx,
            )
            for _ in range(20)
        ]
        
        # Should have some non-NO_HINT actions
        non_hint_actions = [a for a in actions if a != mock_policy.action_space.no_hint_action]
        assert len(non_hint_actions) > 0, "Should allow hints when budget not exceeded"
    
    def test_disallowed_actions_masked(self, sample_features):
        """Disallowed actions should never be selected."""
        from tutor_rl.config import SafetyConfig
        from tutor_rl.action_space import ActionSpace
        
        # Create policy with actions 2 and 3 disallowed
        policy = create_mock_policy(num_actions=8, seed=42)
        policy.action_space.disallowed_actions = [2, 3]
        
        # Sample many times
        actions = [
            policy.predict_action_stochastic(sample_features, temperature=1.0)
            for _ in range(100)
        ]
        
        assert 2 not in actions, "Action 2 should be disallowed"
        assert 3 not in actions, "Action 3 should be disallowed"


class TestHintGeneration:
    """Tests for hint text generation."""
    
    def test_no_hint_returns_empty(self, mock_policy):
        """NO_HINT action should return empty string."""
        hint = mock_policy.generate_hint(HintAction.NO_HINT)
        assert hint == ""
    
    def test_hint_actions_return_text(self, mock_policy):
        """Non-NO_HINT actions should return hint text."""
        for action_id in range(1, 8):
            hint = mock_policy.generate_hint(action_id)
            assert len(hint) > 0, f"Action {action_id} should return hint text"
    
    def test_contextual_hint_includes_context(self, mock_policy):
        """Contextual hints should incorporate context."""
        context = {
            "trial_count": 15,
            "hint_count": 3,
            "stuck": True,
            "improving": False,
        }
        
        hint = mock_policy.generate_hint(HintAction.SUGGEST_EXPLORE_AMP, context)
        
        # Should include some context reference
        assert "stuck" in hint.lower() or len(hint) > 50


class TestDeterministicInference:
    """Tests for reproducibility."""
    
    def test_set_seed_changes_rng(self, mock_policy, sample_features):
        """Setting seed should reset RNG state."""
        mock_policy.set_seed(123)
        action1 = mock_policy.predict_action_stochastic(sample_features)
        
        mock_policy.set_seed(123)
        action2 = mock_policy.predict_action_stochastic(sample_features)
        
        assert action1 == action2
    
    def test_greedy_is_deterministic(self, mock_policy, sample_features):
        """Greedy prediction should be deterministic regardless of seed."""
        action1 = mock_policy.predict_action(sample_features)
        action2 = mock_policy.predict_action(sample_features)
        
        assert action1 == action2

