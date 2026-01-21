"""
Tests for safety-related functionality.

Ensures hints never contain solutions or numeric parameter values.
"""

import re

import pytest

from tutor_rl.action_space import (
    ActionSpace,
    HintAction,
    HINT_TEMPLATES,
    HINT_TEMPLATES_CONTEXTUAL,
    validate_hint_no_solution,
)
from tutor_rl.policy import create_mock_policy


class TestHintSafetyValidation:
    """Tests for hint content safety validation."""
    
    def test_static_hints_contain_no_numeric_values(self):
        """Static hint templates should not contain numeric parameter values."""
        for action_id, template in HINT_TEMPLATES.items():
            if action_id == HintAction.NO_HINT:
                continue
            
            # Check for floating point numbers
            floats = re.findall(r'\b\d+\.\d+\b', template)
            
            # Filter out acceptable numbers (like "3 trials" context)
            suspicious_floats = [
                f for f in floats 
                if 0 < float(f) < 10 and float(f) != int(float(f))
            ]
            
            assert len(suspicious_floats) == 0, (
                f"Action {action_id} hint contains suspicious float: {suspicious_floats}\n"
                f"Hint: {template}"
            )
    
    def test_static_hints_contain_no_parameter_assignments(self):
        """Static hints should not have parameter=value patterns."""
        assignment_pattern = r'\b(?:freq|amp|offset|phase)\s*[=:]\s*[\d.]+'
        
        for action_id, template in HINT_TEMPLATES.items():
            if action_id == HintAction.NO_HINT:
                continue
            
            matches = re.findall(assignment_pattern, template, re.IGNORECASE)
            assert len(matches) == 0, (
                f"Action {action_id} hint contains parameter assignment: {matches}\n"
                f"Hint: {template}"
            )
    
    def test_static_hints_contain_no_vectors(self):
        """Static hints should not contain vector patterns like [1.0, 2.0]."""
        vector_pattern = r'\[[\d.,\s]+\]|\([\d.,\s]+\)'
        
        for action_id, template in HINT_TEMPLATES.items():
            if action_id == HintAction.NO_HINT:
                continue
            
            matches = re.findall(vector_pattern, template)
            assert len(matches) == 0, (
                f"Action {action_id} hint contains vector pattern: {matches}\n"
                f"Hint: {template}"
            )
    
    def test_contextual_hints_with_various_contexts(self):
        """Contextual hints should be safe with various contexts."""
        test_contexts = [
            {"trial_count": 5, "hint_count": 2, "stuck": True, "improving": False},
            {"trial_count": 20, "hint_count": 10, "stuck": False, "improving": True},
            {"trial_count": 0, "hint_count": 0, "stuck": False, "improving": False},
        ]
        
        for action_id, template_fn in HINT_TEMPLATES_CONTEXTUAL.items():
            if action_id == HintAction.NO_HINT:
                continue
            
            for ctx in test_contexts:
                hint = template_fn(ctx)
                assert validate_hint_no_solution(hint), (
                    f"Action {action_id} contextual hint failed safety check\n"
                    f"Context: {ctx}\n"
                    f"Hint: {hint}"
                )
    
    def test_action_space_validates_hints(self):
        """ActionSpace should validate generated hints."""
        action_space = ActionSpace()
        
        for action_id in range(action_space.num_actions):
            hint = action_space.generate_hint(action_id)
            assert validate_hint_no_solution(hint), (
                f"Action {action_id} hint failed validation: {hint}"
            )


class TestValidateHintNoSolution:
    """Tests for the validate_hint_no_solution function."""
    
    def test_empty_hint_is_safe(self):
        """Empty hint should pass validation."""
        assert validate_hint_no_solution("")
        assert validate_hint_no_solution(None)
    
    def test_safe_hint_passes(self):
        """Normal educational hints should pass."""
        safe_hints = [
            "Try adjusting the frequency and observe the results.",
            "What happens when you change the amplitude?",
            "Consider how the phase affects leg coordination.",
            "You've made good progress in your last few trials.",
        ]
        
        for hint in safe_hints:
            assert validate_hint_no_solution(hint), f"Safe hint failed: {hint}"
    
    def test_numeric_value_hint_fails(self):
        """Hints with specific numeric values should fail."""
        unsafe_hints = [
            "Set the frequency to 1.5",
            "Try amplitude=0.8 for better results",
            "The optimal offset is 0.3",
            "Use phase shift of 1.57",
        ]
        
        for hint in unsafe_hints:
            assert not validate_hint_no_solution(hint), (
                f"Unsafe hint passed validation: {hint}"
            )
    
    def test_vector_hint_fails(self):
        """Hints with parameter vectors should fail."""
        unsafe_hints = [
            "Use parameters [1.5, 0.8, 0.3, 1.57]",
            "Set oscillator to (1.2, 0.9, 0.0, 0.0)",
        ]
        
        for hint in unsafe_hints:
            assert not validate_hint_no_solution(hint), (
                f"Vector hint passed validation: {hint}"
            )
    
    def test_integer_context_is_allowed(self):
        """Integer counts in context should be allowed."""
        allowed_hints = [
            "You've tried 5 different configurations.",
            "After 10 trials, consider a new approach.",
            "You've used 3 hints so far.",
        ]
        
        for hint in allowed_hints:
            assert validate_hint_no_solution(hint), (
                f"Context integer hint failed: {hint}"
            )


class TestBudgetEnforcement:
    """Tests for hint budget enforcement."""
    
    def test_budget_zero_always_no_hint(self):
        """With budget_max=0, should always return NO_HINT."""
        from tutor_rl.policy import SafetyContext
        from tutor_rl.config import SafetyConfig
        
        policy = create_mock_policy()
        policy.safety_config = SafetyConfig(hint_budget_max=0)
        
        features = {f"feature_{i}": 0.5 for i in range(18)}
        safety_ctx = SafetyContext(hint_budget_used=0)
        
        action = policy.predict_action(features, safety_context=safety_ctx)
        assert action == policy.action_space.no_hint_action
    
    def test_budget_at_limit_no_hint(self):
        """When hint_budget_used == budget_max, should return NO_HINT."""
        from tutor_rl.policy import SafetyContext
        
        policy = create_mock_policy()
        features = {f"feature_{i}": 0.5 for i in range(18)}
        
        # Set hints_used equal to max (default 10)
        safety_ctx = SafetyContext(hint_budget_used=10)
        
        action = policy.predict_action(features, safety_context=safety_ctx)
        assert action == policy.action_space.no_hint_action
    
    def test_budget_over_limit_no_hint(self):
        """When hint_budget_used > budget_max, should return NO_HINT."""
        from tutor_rl.policy import SafetyContext
        
        policy = create_mock_policy()
        features = {f"feature_{i}": 0.5 for i in range(18)}
        
        safety_ctx = SafetyContext(hint_budget_used=15)  # Over limit
        
        action = policy.predict_action(features, safety_context=safety_ctx)
        assert action == policy.action_space.no_hint_action


class TestNoFullSolutionOutput:
    """Ensure the system never outputs a full solution."""
    
    def test_policy_never_outputs_parameter_vector(self):
        """Policy generate_hint should never output a parameter vector."""
        policy = create_mock_policy()
        
        # Test all actions with various contexts
        for action_id in range(policy.action_space.num_actions):
            for _ in range(10):  # Multiple random contexts
                context = {
                    "trial_count": 5,
                    "hint_count": 2,
                    "stuck": True,
                    "improving": False,
                }
                
                hint = policy.generate_hint(action_id, context)
                
                # Check for vector patterns
                vector_pattern = r'\[[\d.,\s]+\]|\([\d.,\s]+\)'
                assert not re.search(vector_pattern, hint), (
                    f"Action {action_id} output a parameter vector: {hint}"
                )
                
                # Check for multiple float values (potential solution)
                floats = re.findall(r'\b\d+\.\d+\b', hint)
                assert len(floats) < 3, (
                    f"Action {action_id} output too many floats (potential solution): {hint}"
                )
    
    def test_action_names_dont_reveal_solution(self):
        """Action names should be pedagogical, not solution-revealing."""
        action_space = ActionSpace()
        
        for action_id in range(action_space.num_actions):
            name = action_space.action_name(action_id)
            
            # Should not contain specific values
            assert not re.search(r'\d+\.\d+', name), (
                f"Action name contains float: {name}"
            )
            
            # Should be descriptive, not a command
            assert "=" not in name, f"Action name contains assignment: {name}"

