"""
Hint policy for inference with safety wrapper.

Provides the HintPolicy class that:
1. Loads trained CQL model
2. Predicts actions with safety constraints
3. Generates hint text from action IDs
"""

from __future__ import annotations

import json
import logging
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Optional

import numpy as np
from sklearn.preprocessing import StandardScaler

from tutor_rl.action_space import ActionSpace, HINT_TEMPLATES, HINT_TEMPLATES_CONTEXTUAL
from tutor_rl.config import SafetyConfig, TutorRLConfig, load_config
from tutor_rl.data import load_scaler, load_feature_schema

logger = logging.getLogger(__name__)


@dataclass
class SafetyContext:
    """Context for safety wrapper decisions."""
    
    hint_budget_used: int = 0
    stuck_prob: float = 0.0
    recent_improvement: float = 0.0
    session_hints: list[int] = field(default_factory=list)


class HintPolicy:
    """
    Production-ready hint policy with safety wrapper.
    
    Features:
    - Loads trained CQL model for action prediction
    - Applies safety rules to constrain actions
    - Generates contextual hint text
    - Supports deterministic inference with seed
    
    Example:
        >>> policy = HintPolicy.load("artifacts/run_20240101_120000")
        >>> features = {"freq_0": 1.5, "amp_0": 0.8, ...}
        >>> action = policy.predict_action(features, safety_context=ctx)
        >>> hint = policy.generate_hint(action, context)
    """
    
    def __init__(
        self,
        model,
        scaler: StandardScaler,
        feature_columns: list[str],
        action_space: ActionSpace,
        safety_config: SafetyConfig,
        seed: Optional[int] = None,
    ):
        """
        Initialize HintPolicy.
        
        Args:
            model: Trained CQL model
            scaler: Fitted StandardScaler for features
            feature_columns: List of feature column names in order
            action_space: Action space configuration
            safety_config: Safety wrapper configuration
            seed: Random seed for deterministic inference
        """
        self.model = model
        self.scaler = scaler
        self.feature_columns = feature_columns
        self.action_space = action_space
        self.safety_config = safety_config
        self._rng = np.random.default_rng(seed)
        self._seed = seed
    
    @classmethod
    def load(cls, model_dir: str | Path, seed: Optional[int] = None) -> "HintPolicy":
        """
        Load policy from saved artifacts directory.
        
        Args:
            model_dir: Directory containing model artifacts
            seed: Random seed for deterministic inference
            
        Returns:
            Loaded HintPolicy instance
        """
        model_dir = Path(model_dir)
        
        if not model_dir.exists():
            raise FileNotFoundError(f"Model directory not found: {model_dir}")
        
        # Load config
        config_path = model_dir / "config.yml"
        if config_path.exists():
            config = load_config(config_path)
        else:
            logger.warning("No config.yml found, using defaults")
            config = TutorRLConfig()
        
        # Load model
        model = cls._load_model(model_dir)
        
        # Load scaler
        scaler = load_scaler(model_dir / "scaler.joblib")
        
        # Load feature schema
        feature_columns = load_feature_schema(model_dir / "feature_schema.json")
        
        # Load action map (for validation)
        action_map_path = model_dir / "action_map.json"
        if action_map_path.exists():
            with open(action_map_path, "r") as f:
                action_map = json.load(f)
            num_actions = len(action_map)
        else:
            num_actions = config.action.num_actions
        
        # Create action space
        action_space = ActionSpace(
            num_actions=num_actions,
            no_hint_action=config.action.no_hint_action_id,
            disallowed_actions=config.action.disallowed_actions,
        )
        
        logger.info(f"Loaded policy from {model_dir}")
        logger.info(f"  Features: {len(feature_columns)}")
        logger.info(f"  Actions: {num_actions}")
        
        return cls(
            model=model,
            scaler=scaler,
            feature_columns=feature_columns,
            action_space=action_space,
            safety_config=config.safety,
            seed=seed,
        )
    
    @staticmethod
    def _load_model(model_dir: Path):
        """Load CQL model from directory."""
        model_path = None
        
        # Try various model file locations (d3rlpy v2.x auto-saves to subdirectory)
        possible_paths = [
            model_dir / "model.pt",
            model_dir / "model.d3",
            model_dir / "model.zip",
            model_dir / "model",
        ]
        
        # Also check d3rlpy auto-save directory
        cql_dir = model_dir / "cql_hint_policy"
        if cql_dir.exists():
            # Find latest model file
            import glob
            d3_files = list(cql_dir.glob("model_*.d3"))
            if d3_files:
                # Sort by step number and get latest
                d3_files.sort(key=lambda x: int(x.stem.split("_")[1]) if "_" in x.stem else 0)
                possible_paths.insert(0, d3_files[-1])
        
        for alt_path in possible_paths:
            if alt_path.exists():
                model_path = alt_path
                break
        
        if model_path is None:
            raise FileNotFoundError(f"No model file found in {model_dir}")
        
        logger.info(f"Loading model from {model_path}")
        
        try:
            import pickle
            import io
            from d3rlpy.algos import DiscreteCQL
            
            # d3rlpy v2.x saves .d3 files as pickle with {'torch', 'config', 'version'}
            # We need to manually unpack due to PyTorch 2.6 weights_only default
            params_path = model_path.parent / "params.json"
            
            if model_path.suffix == ".d3":
                try:
                    # Load the d3 file manually
                    with open(model_path, 'rb') as f:
                        d3_data = pickle.load(f)
                    
                    # Create model from params.json
                    if params_path.exists():
                        model = DiscreteCQL.from_json(str(params_path))
                    else:
                        # Fall back to config from d3 file
                        from d3rlpy.algos import DiscreteCQLConfig
                        config = DiscreteCQLConfig()
                        model = config.create()
                    
                    # Extract torch data - it's a bytes buffer
                    torch_data = d3_data['torch']
                    buffer = io.BytesIO(torch_data)
                    
                    # Use impl.load_model which takes a BinaryIO
                    model._impl.load_model(buffer)
                    
                    logger.info(f"Loaded model from .d3 file with manual unpacking")
                    return model
                    
                except Exception as e:
                    logger.warning(f"Manual d3 load failed: {e}")
            
            # Fallback for other formats
            if params_path.exists():
                try:
                    model = DiscreteCQL.from_json(str(params_path))
                    model.load_model(str(model_path))
                    logger.info("Loaded model using from_json + load_model")
                    return model
                except Exception as e:
                    logger.warning(f"Standard load failed: {e}")
            
            raise RuntimeError(f"Could not load model from {model_path}")
            
        except Exception as e:
            logger.error(f"Failed to load model: {e}")
            raise
    
    def _features_to_array(self, features: dict[str, float]) -> np.ndarray:
        """
        Convert feature dictionary to normalized array.
        
        Args:
            features: Dictionary of feature name -> value
            
        Returns:
            Normalized feature array of shape (1, n_features)
        """
        # Validate all required features present
        missing = set(self.feature_columns) - set(features.keys())
        if missing:
            raise ValueError(f"Missing features: {missing}")
        
        # Build array in correct order
        arr = np.array([[features[col] for col in self.feature_columns]], dtype=np.float32)
        
        # Normalize
        arr = self.scaler.transform(arr)
        
        return arr
    
    def predict_action(
        self,
        features: dict[str, float],
        safety_context: Optional[SafetyContext] = None,
        return_q_values: bool = False,
    ) -> int | tuple[int, np.ndarray]:
        """
        Predict hint action with safety wrapper applied.
        
        Args:
            features: Dictionary of feature values
            safety_context: Optional safety context for constraints
            return_q_values: Whether to also return Q-values
            
        Returns:
            Chosen action ID, optionally with Q-values array
        """
        # Get observation array
        obs = self._features_to_array(features)
        
        # Get Q-values for all actions
        q_values = self._get_all_q_values_array(obs)
        
        # Apply safety wrapper
        action = self._apply_safety_wrapper(q_values, features, safety_context)
        
        if return_q_values:
            return action, q_values
        return action
    
    def _get_all_q_values_array(self, obs: np.ndarray) -> np.ndarray:
        """
        Get Q-values for all actions given observation.
        
        d3rlpy v2.x predict() returns actions, not Q-values.
        We use predict_value() with all actions to get Q-values.
        
        Args:
            obs: Observation array of shape (1, obs_dim)
            
        Returns:
            Q-values array of shape (num_actions,)
        """
        n_actions = self.action_space.num_actions
        
        # Expand observation to match all actions
        obs_expanded = np.tile(obs, (n_actions, 1))
        
        # Get all action indices
        all_actions = np.arange(n_actions)
        
        # Get Q-values for all actions
        q_values = self.model.predict_value(obs_expanded, all_actions)
        
        return q_values
    
    def _apply_safety_wrapper(
        self,
        q_values: np.ndarray,
        features: dict[str, float],
        safety_context: Optional[SafetyContext],
    ) -> int:
        """
        Apply safety rules to select final action.
        
        Rules (in priority order):
        1. If hint_budget_used >= budget_max => force NO_HINT
        2. If action is in disallowed_actions => mask it out
        3. If stuck_prob < threshold AND recent_improvement > 0 => 
           prefer NO_HINT unless model strongly prefers hint
        
        Args:
            q_values: Q-values for all actions
            features: Feature dictionary
            safety_context: Safety context
            
        Returns:
            Safe action ID
        """
        no_hint = self.action_space.no_hint_action
        
        # Create mask for valid actions
        valid_mask = np.ones(len(q_values), dtype=bool)
        
        # Rule 1: Budget constraint
        if safety_context and safety_context.hint_budget_used >= self.safety_config.hint_budget_max:
            logger.debug("Budget exhausted, forcing NO_HINT")
            return no_hint
        
        # Rule 2: Disallowed actions
        for action in self.action_space.disallowed_actions:
            if action < len(valid_mask):
                valid_mask[action] = False
        
        # Get stuck_prob from features or context
        stuck_prob = features.get("stuck_prob", 0.0)
        if safety_context:
            stuck_prob = max(stuck_prob, safety_context.stuck_prob)
        
        # Get improvement from features or context
        improvement = features.get("recent_speed_delta", 0.0)
        if safety_context:
            improvement = max(improvement, safety_context.recent_improvement)
        
        # Rule 3: Prefer NO_HINT if not stuck and improving
        if (stuck_prob < self.safety_config.stuck_prob_threshold and 
            improvement > self.safety_config.improvement_threshold):
            
            # Check if model strongly prefers a hint
            no_hint_q = q_values[no_hint]
            best_hint_q = np.max(q_values[valid_mask & (np.arange(len(q_values)) != no_hint)])
            
            # Only give hint if Q-value margin is significant
            if best_hint_q <= no_hint_q + self.safety_config.q_value_margin:
                logger.debug("Student improving and not stuck, preferring NO_HINT")
                return no_hint
        
        # Select best valid action
        masked_q = np.where(valid_mask, q_values, -np.inf)
        action = int(np.argmax(masked_q))
        
        return action
    
    def predict_action_stochastic(
        self,
        features: dict[str, float],
        temperature: float = 1.0,
        safety_context: Optional[SafetyContext] = None,
    ) -> int:
        """
        Sample action stochastically using softmax of Q-values.
        
        Useful for exploration or getting diverse hints.
        
        Args:
            features: Dictionary of feature values
            temperature: Softmax temperature (higher = more random)
            safety_context: Optional safety context
            
        Returns:
            Sampled action ID
        """
        obs = self._features_to_array(features)
        q_values = self._get_all_q_values_array(obs)
        
        # Apply safety masking
        valid_mask = np.ones(len(q_values), dtype=bool)
        for action in self.action_space.disallowed_actions:
            if action < len(valid_mask):
                valid_mask[action] = False
        
        # Budget check
        if safety_context and safety_context.hint_budget_used >= self.safety_config.hint_budget_max:
            return self.action_space.no_hint_action
        
        # Softmax with temperature
        masked_q = np.where(valid_mask, q_values, -np.inf)
        exp_q = np.exp((masked_q - np.max(masked_q)) / temperature)
        probs = exp_q / exp_q.sum()
        
        # Sample
        action = self._rng.choice(len(probs), p=probs)
        
        return int(action)
    
    def generate_hint(
        self,
        action_id: int,
        context: Optional[dict[str, Any]] = None,
    ) -> str:
        """
        Generate hint text for an action.
        
        Args:
            action_id: The action ID to generate hint for
            context: Optional context for contextual hints
            
        Returns:
            Hint text string (empty for NO_HINT)
        """
        return self.action_space.generate_hint(action_id, context)
    
    def get_all_q_values(self, features: dict[str, float]) -> dict[str, float]:
        """
        Get Q-values for all actions (for debugging/analysis).
        
        Args:
            features: Dictionary of feature values
            
        Returns:
            Dictionary of action_name -> Q-value
        """
        obs = self._features_to_array(features)
        q_values = self.model.predict(obs)[0]
        
        return {
            self.action_space.action_name(i): float(q_values[i])
            for i in range(len(q_values))
        }
    
    def set_seed(self, seed: int) -> None:
        """Set random seed for deterministic inference."""
        self._seed = seed
        self._rng = np.random.default_rng(seed)
    
    def __repr__(self) -> str:
        return (
            f"HintPolicy(features={len(self.feature_columns)}, "
            f"actions={self.action_space.num_actions}, "
            f"seed={self._seed})"
        )


def create_mock_policy(
    num_features: int = 18,
    num_actions: int = 8,
    seed: int = 42,
) -> HintPolicy:
    """
    Create a mock policy for testing without a trained model.
    
    Uses random Q-values for action selection.
    
    Args:
        num_features: Number of features
        num_actions: Number of actions
        seed: Random seed
        
    Returns:
        Mock HintPolicy instance
    """
    
    class MockModel:
        """Mock model that returns deterministic Q-values based on observation."""
        
        def __init__(self, n_actions: int, seed: int):
            self.n_actions = n_actions
            self.seed = seed
            # Fixed weight matrix for deterministic Q-values
            rng = np.random.default_rng(seed)
            self._weights = rng.random((n_actions,))
        
        def predict(self, obs: np.ndarray) -> np.ndarray:
            """Return best action for each observation."""
            q_values = self._compute_q_values(obs)
            return np.argmax(q_values, axis=1)
        
        def predict_value(self, obs: np.ndarray, actions: np.ndarray) -> np.ndarray:
            """Return Q-value for specific actions."""
            q_values = self._compute_q_values(obs)
            # Select Q-value for each action
            return q_values[np.arange(len(actions)), actions]
        
        def _compute_q_values(self, obs: np.ndarray) -> np.ndarray:
            """Compute Q-values for all actions given observations."""
            # Deterministic Q-values: based on obs sum * fixed weights
            obs_hash = np.sum(obs, axis=1, keepdims=True)
            q_values = np.sin(obs_hash * np.arange(1, self.n_actions + 1)) * self._weights
            return q_values
    
    # Create mock scaler with deterministic data
    rng = np.random.default_rng(seed)
    scaler = StandardScaler()
    scaler.fit(rng.standard_normal((100, num_features)))
    
    # Feature columns
    feature_columns = [f"feature_{i}" for i in range(num_features)]
    
    # Action space
    action_space = ActionSpace(num_actions=num_actions)
    
    # Safety config
    safety_config = SafetyConfig()
    
    return HintPolicy(
        model=MockModel(num_actions, seed),
        scaler=scaler,
        feature_columns=feature_columns,
        action_space=action_space,
        safety_config=safety_config,
        seed=seed,
    )

