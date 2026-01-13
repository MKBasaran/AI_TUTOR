# Tutor RL: Offline RL Hint-Generation Policy

An AI tutor that helps students tune robot gait parameters (frequency, amplitude, offset, phase shift) using **Conservative Q-Learning (CQL)** for discrete hint actions.

## Key Features

- **Offline RL**: Trains only from logged interaction data (no online environment)
- **Discrete Hints**: Chooses from micro-hints and reflection prompts
- **Safety Constraints**: Never outputs full solutions or parameter values
- **Production Ready**: Type hints, logging, configuration, and comprehensive tests

## Quick Start

### Installation

```bash
pip install -r requirements.txt
```

### Training

```bash
python -m tutor_rl train --config config.yml --data data.csv
```

### Evaluation

```bash
python -m tutor_rl eval --model artifacts/run_YYYYMMDD_HHMMSS --data data.csv
```

### Demo Inference

```bash
python -m tutor_rl demo --model artifacts/run_YYYYMMDD_HHMMSS \
    --row "recent_speed_delta=0.1,stuck_prob=0.3,trial_count=5"
```

## Project Structure

```
tutor_rl/
├── __init__.py          # Package exports
├── __main__.py          # CLI entry point
├── config.py            # Configuration with pydantic-style dataclasses
├── data.py              # Dataset loading and transition building
├── reward.py            # Reward computation from raw logs
├── action_space.py      # Discrete actions and hint templates
├── train_cql.py         # CQL training with d3rlpy
├── eval_offline.py      # Offline policy evaluation (OPE)
├── policy.py            # Inference with safety wrapper
└── cli.py               # Command-line interface

tests/
├── test_data.py         # Data pipeline tests
├── test_policy.py       # Policy inference tests
└── test_safety.py       # Safety constraint tests

config.yml               # Sample configuration
requirements.txt         # Dependencies
```

## Data Format

The system expects a CSV or Parquet file with:

| Column | Type | Description |
|--------|------|-------------|
| `session_id` | str | Unique session identifier |
| `step_idx` | int | Decision point index within session |
| `freq_0`, `amp_0`, ... | float | Oscillator parameters (features) |
| `recent_speed` | float | Current robot speed metric |
| `recent_speed_delta` | float | Speed change from previous |
| `stuck_prob` | float | Probability of being stuck (0-1) |
| `action` | int | Hint action taken (0-7) |
| `reward` | float | Reward (optional, can be computed) |
| `done` | bool | Episode terminal flag |

If `reward` is not present, it will be computed from:
- Speed improvement over next K trials
- Escaping stuck states
- Hint usage penalty
- Budget/safety penalties

## Action Space

| ID | Action | Description |
|----|--------|-------------|
| 0 | NO_HINT | Don't provide a hint |
| 1 | REFLECT_DIRECTION | Ask to reflect on parameter direction |
| 2 | SUGGEST_EXPLORE_FREQ | Suggest exploring frequency |
| 3 | SUGGEST_EXPLORE_AMP | Suggest exploring amplitude |
| 4 | SUGGEST_EXPLORE_OFFSET | Suggest exploring offset |
| 5 | SUGGEST_EXPLORE_PHASE | Suggest exploring phase |
| 6 | ENCOURAGE_SMALL_STEPS | Encourage smaller adjustments |
| 7 | SUGGEST_RESET | Suggest trying different approach |

**Safety**: Hints are pedagogical prompts that NEVER include:
- Specific parameter values
- Optimal solutions
- Parameter vectors

## Configuration

See `config.yml` for all options. Key settings:

```yaml
training:
  learning_rate: 0.0003
  batch_size: 256
  n_epochs: 100
  alpha: 1.0  # CQL conservatism (higher = more conservative)

safety:
  hint_budget_max: 10
  stuck_prob_threshold: 0.3
  q_value_margin: 0.5
```

## CQL Implementation Notes

We use **DiscreteCQL** from d3rlpy, which is DQN-based Conservative Q-Learning for discrete action spaces.

### Why CQL?

CQL is designed for offline RL where:
1. We can't explore (safety constraints in tutoring)
2. Standard Q-learning overestimates OOD actions
3. We need conservative value estimates

CQL adds a regularization term that penalizes Q-values for actions not in the dataset, producing more reliable policies.

### d3rlpy Version Compatibility

The code handles both d3rlpy v1.x and v2.x APIs:

```python
# v2.x
from d3rlpy.algos import DiscreteCQLConfig
cql = DiscreteCQLConfig(...).create()

# v1.x (fallback)
from d3rlpy.algos import DiscreteCQL
cql = DiscreteCQL(...)
```

## Safety Wrapper

The `HintPolicy` class includes safety rules:

1. **Budget Constraint**: If `hint_budget_used >= budget_max`, force NO_HINT
2. **Disallowed Actions**: Mask out any actions in the disallowed list
3. **Progress Check**: If student is improving and not stuck, prefer NO_HINT unless model strongly favors a hint

```python
from tutor_rl import HintPolicy, SafetyContext

policy = HintPolicy.load("artifacts/run_...")

# Create safety context
ctx = SafetyContext(hint_budget_used=5, stuck_prob=0.2)

# Get safe action
action = policy.predict_action(features, safety_context=ctx)
hint_text = policy.generate_hint(action, context)
```

## Offline Evaluation

The evaluation module computes:

- **Q-value metrics**: Mean Q for chosen actions, Q advantage
- **Action distribution**: KL divergence between learned and behavior policy
- **OPE estimates**: Self-normalized importance sampling (SNIPS)

```bash
python -m tutor_rl eval --model artifacts/run_... --data test_data.csv
```

Output is saved as `eval_report.json`.

## Testing

```bash
# Run all tests_rl
pytest tests_rl/ -v

# Run with coverage
pytest tests_rl/ --cov=tutor_rl --cov-report=html
```

Key test categories:
- `test_data.py`: Next-feature shifting, normalization, session splitting
- `test_policy.py`: Action prediction, deterministic inference
- `test_safety.py`: Budget enforcement, hint content validation

## Extending

### Adding New Hint Types

1. Add to `HintAction` enum in `action_space.py`
2. Add template to `HINT_TEMPLATES`
3. Optionally add contextual template to `HINT_TEMPLATES_CONTEXTUAL`
4. Update `num_actions` in config

### Custom Reward Function

Modify `reward.py` or add new component functions:

```python
def compute_custom_reward(df, config, data_config):
    # Your reward logic
    return rewards_array
```

### Different RL Algorithm

Replace `DiscreteCQL` in `train_cql.py` with another d3rlpy algorithm:

```python
from d3rlpy.algos import DiscreteBCQ, DiscreteSAC
```

## License

MIT License - See LICENSE file.

## Citation

If you use this code in research, please cite:

```bibtex
@software{tutor_rl,
  title={Tutor RL: Offline RL Hint-Generation Policy},
  author={Your Name},
  year={2024},
  url={https://github.com/your-repo}
}
```

