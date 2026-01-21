"""
Offline RL Hint-Generation Policy using Conservative Q-Learning (CQL).

This package implements an AI tutor that helps students tune robot gait parameters
by providing discrete hint actions (never full solutions).
"""

__version__ = "0.1.0"

from tutor_rl.config import TutorRLConfig
from tutor_rl.policy import HintPolicy
from tutor_rl.action_space import ActionSpace, HintAction

__all__ = [
    "TutorRLConfig",
    "HintPolicy", 
    "ActionSpace",
    "HintAction",
]

