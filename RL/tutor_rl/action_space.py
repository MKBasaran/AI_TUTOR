"""
Action space definitions and hint templates.

Actions are discrete hint types. Hints are micro-hints or reflection prompts
that NEVER include exact parameter values or optimal solutions.
"""

from __future__ import annotations

import logging
import re
from dataclasses import dataclass
from enum import IntEnum
from typing import Any, Callable, Optional

logger = logging.getLogger(__name__)


class HintAction(IntEnum):
    """
    Discrete hint actions for the tutor.
    
    Action 0 is always NO_HINT. Other actions map to safe hint templates.
    """
    NO_HINT = 0
    REFLECT_DIRECTION = 1      # Ask student to reflect on parameter direction
    SUGGEST_EXPLORE_FREQ = 2   # Suggest exploring frequency changes
    SUGGEST_EXPLORE_AMP = 3    # Suggest exploring amplitude changes
    SUGGEST_EXPLORE_OFFSET = 4 # Suggest exploring offset changes
    SUGGEST_EXPLORE_PHASE = 5  # Suggest exploring phase changes
    ENCOURAGE_SMALL_STEPS = 6  # Encourage smaller parameter changes
    SUGGEST_RESET = 7          # Suggest trying a different approach


# Template ID to hint text mapping
# IMPORTANT: Templates must NOT contain numeric values or solutions
HINT_TEMPLATES: dict[int, str] = {
    HintAction.NO_HINT: "",
    
    HintAction.REFLECT_DIRECTION: (
        "Take a moment to reflect: when you made your last change, "
        "did the robot move faster or slower? What does that tell you "
        "about which direction to adjust?"
    ),
    
    HintAction.SUGGEST_EXPLORE_FREQ: (
        "Consider exploring how the frequency parameter affects the gait. "
        "Try making a small adjustment and observe the result."
    ),
    
    HintAction.SUGGEST_EXPLORE_AMP: (
        "The amplitude parameter controls how far each joint moves. "
        "What happens if you experiment with this value?"
    ),
    
    HintAction.SUGGEST_EXPLORE_OFFSET: (
        "The offset shifts the center position of the oscillation. "
        "Consider how this might affect the robot's posture."
    ),
    
    HintAction.SUGGEST_EXPLORE_PHASE: (
        "Phase shift affects the timing relationship between oscillators. "
        "Think about how the legs coordinate - could timing be the issue?"
    ),
    
    HintAction.ENCOURAGE_SMALL_STEPS: (
        "You're making good progress! Try making smaller adjustments now "
        "to fine-tune your results. Small steps can reveal patterns."
    ),
    
    HintAction.SUGGEST_RESET: (
        "Sometimes it helps to step back and try a different approach. "
        "What if you focus on a different parameter for now?"
    ),
}


# Extended templates with context placeholders (still no numeric values)
HINT_TEMPLATES_CONTEXTUAL: dict[int, Callable[[dict[str, Any]], str]] = {
    HintAction.NO_HINT: lambda ctx: "",
    
    HintAction.REFLECT_DIRECTION: lambda ctx: (
        f"You've been working on this for {ctx.get('trial_count', 'some')} trials. "
        "Reflect on your recent changes: which direction seemed most promising? "
        "Trust your observations."
    ),
    
    HintAction.SUGGEST_EXPLORE_FREQ: lambda ctx: (
        "Consider exploring the frequency parameter. "
        f"{'You seem to be making progress - ' if ctx.get('improving', False) else ''}"
        "A small frequency change might reveal interesting patterns."
    ),
    
    HintAction.SUGGEST_EXPLORE_AMP: lambda ctx: (
        "The amplitude controls movement range. "
        f"{'Since you have been stuck for a while, ' if ctx.get('stuck', False) else ''}"
        "try adjusting amplitude and see how the gait changes."
    ),
    
    HintAction.SUGGEST_EXPLORE_OFFSET: lambda ctx: (
        "Offset affects the neutral position. "
        "Consider how the robot's posture might benefit from offset adjustments."
    ),
    
    HintAction.SUGGEST_EXPLORE_PHASE: lambda ctx: (
        "Phase controls timing between oscillators. "
        f"{'You have used {0} hints so far - '.format(ctx.get('hint_count', 0)) if ctx.get('hint_count', 0) > 0 else ''}"
        "Think about leg coordination."
    ),
    
    HintAction.ENCOURAGE_SMALL_STEPS: lambda ctx: (
        f"{'Great work so far! ' if ctx.get('improving', False) else ''}"
        "Try smaller adjustments now to fine-tune. "
        "What's the smallest change you could make to test your hypothesis?"
    ),
    
    HintAction.SUGGEST_RESET: lambda ctx: (
        "It might help to try a fresh approach. "
        f"{'You have been exploring for a while. ' if ctx.get('trial_count', 0) > 10 else ''}"
        "Consider focusing on a different aspect of the gait."
    ),
}


@dataclass
class ActionSpace:
    """
    Manages the discrete action space for hint generation.
    
    Attributes:
        num_actions: Total number of actions (including NO_HINT)
        no_hint_action: Action ID for no hint
        disallowed_actions: List of action IDs that are currently disallowed
    """
    
    num_actions: int = 8
    no_hint_action: int = 0
    disallowed_actions: list[int] = None
    
    def __post_init__(self) -> None:
        if self.disallowed_actions is None:
            self.disallowed_actions = []
        
        if self.num_actions > len(HintAction):
            logger.warning(
                f"num_actions ({self.num_actions}) > defined HintActions ({len(HintAction)}). "
                "Extra actions will map to empty hints."
            )
    
    def is_valid_action(self, action_id: int) -> bool:
        """Check if action ID is valid (in range and not disallowed)."""
        return 0 <= action_id < self.num_actions and action_id not in self.disallowed_actions
    
    def get_valid_actions(self) -> list[int]:
        """Get list of currently valid action IDs."""
        return [i for i in range(self.num_actions) if i not in self.disallowed_actions]
    
    def get_hint_template(self, action_id: int) -> str:
        """
        Get the hint template for an action.
        
        Args:
            action_id: The action ID
            
        Returns:
            Hint template string (empty for NO_HINT or unknown actions)
        """
        if action_id in HINT_TEMPLATES:
            return HINT_TEMPLATES[action_id]
        return ""
    
    def generate_hint(
        self,
        action_id: int,
        context: Optional[dict[str, Any]] = None,
        use_contextual: bool = True,
    ) -> str:
        """
        Generate a hint string for the given action.
        
        Args:
            action_id: The action ID
            context: Optional context dictionary for contextual hints
            use_contextual: Whether to use contextual templates when available
            
        Returns:
            Generated hint string
        """
        if action_id == self.no_hint_action:
            return ""
        
        if use_contextual and context and action_id in HINT_TEMPLATES_CONTEXTUAL:
            hint = HINT_TEMPLATES_CONTEXTUAL[action_id](context)
        else:
            hint = self.get_hint_template(action_id)
        
        # Safety check: ensure no numeric parameter values leaked
        if not self._validate_hint_safety(hint):
            logger.error(f"Hint safety validation failed for action {action_id}!")
            return self.get_hint_template(action_id)  # Fall back to static template
        
        return hint
    
    @staticmethod
    def _validate_hint_safety(hint: str) -> bool:
        """
        Validate that a hint doesn't contain numeric parameter values.
        
        Checks for patterns that look like parameter assignments or
        specific numeric values that could be solutions.
        
        Args:
            hint: The hint string to validate
            
        Returns:
            True if hint is safe, False if it contains suspicious content
        """
        if not hint:
            return True
        
        # Pattern to detect numeric parameter values (e.g., "frequency=1.5", "set to 0.8")
        suspicious_patterns = [
            r'\b(?:freq|amp|offset|phase|frequency|amplitude)\s*[=:]\s*[\d.]+',  # param=value
            r'\bset\s+(?:to|at)\s+[\d.]+',  # "set to 0.5"
            r'\buse\s+[\d.]+',  # "use 1.5"
            r'\btry\s+[\d.]+',  # "try 0.8" (specific value)
            r'\b(?:optimal|best|correct|right)\s+(?:value|setting|parameter)\s+(?:is|=)\s*[\d.]+',
            r'[\d.]+\s*(?:Hz|rad|deg)',  # specific values with units
        ]
        
        for pattern in suspicious_patterns:
            if re.search(pattern, hint, re.IGNORECASE):
                logger.warning(f"Suspicious pattern found in hint: {pattern}")
                return False
        
        return True
    
    def action_name(self, action_id: int) -> str:
        """Get human-readable name for an action."""
        if action_id < len(HintAction):
            return HintAction(action_id).name
        return f"UNKNOWN_ACTION_{action_id}"
    
    def __repr__(self) -> str:
        return f"ActionSpace(num_actions={self.num_actions}, disallowed={self.disallowed_actions})"


def validate_hint_no_solution(hint: str) -> bool:
    """
    Validate that a hint does not contain a full solution or numeric values.
    
    This is a stricter validation for testing purposes.
    
    Args:
        hint: The hint string to validate
        
    Returns:
        True if hint is safe (no solution), False otherwise
    """
    if not hint:
        return True
    
    # Check for any floating point numbers (potential parameter values)
    float_pattern = r'\b\d+\.\d+\b'
    floats = re.findall(float_pattern, hint)
    
    # Allow some numbers in context (e.g., "3 trials") but flag parameter-like values
    for f in floats:
        val = float(f)
        # Parameter values are typically in ranges like 0-10 or -1 to 1
        # Be suspicious of these
        if -10 <= val <= 10 and val != int(val):
            logger.warning(f"Potential parameter value in hint: {f}")
            return False
    
    # Check for vector-like patterns that could be full solutions
    vector_pattern = r'\[[\d.,\s]+\]|\([\d.,\s]+\)'
    if re.search(vector_pattern, hint):
        logger.warning("Vector pattern found in hint")
        return False
    
    return ActionSpace._validate_hint_safety(hint)

