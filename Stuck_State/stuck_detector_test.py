import unittest
from stuck_detector import StuckDetector


class TestStuckDetector(unittest.TestCase):
    def setUp(self):
        self.detector = StuckDetector(N=5)

    def create_run(self, speed, params, overcurrent=False, overtemp=False, timeout=False):
        return {
            'speed_mps': speed,
            'params': params,
            'overcurrent': overcurrent,
            'overtemp': overtemp,
            'timeout': timeout
        }

    def test_not_stuck_productive(self):
        # speed increasing
        history = []
        for i in range(5):
            history.append(self.create_run(1.0 + i * 0.1, {'p': i}))
        self.assertFalse(self.detector.detect_stuck(history))

    def test_stuck_repeated_failures(self):
        # all runs fail
        history = []
        for _ in range(5):
            history.append(self.create_run(0.0, {'p': 1}, overcurrent=True))
        self.assertTrue(self.detector.detect_stuck(history))

    def test_stuck_no_progress_plateau(self):
        # speed high but flat
        history = []
        for i in range(5):
            history.append(self.create_run(2.0, {'p': 1.0 + i * 0.01}))  # tiny param change
        self.assertTrue(self.detector.detect_stuck(history))

    def test_stuck_no_param_change(self):
        # speed low and params fixed
        history = []
        for _ in range(5):
            history.append(self.create_run(0.5, {'p': 1.0}))
        self.assertTrue(self.detector.detect_stuck(history))

    def test_stuck_oscillation(self):
        # flip between two configs
        history = []
        for i in range(5):
            p = 1.0 if i % 2 == 0 else 2.0
            history.append(self.create_run(1.0, {'p': p}))
        self.assertTrue(self.detector.detect_stuck(history))

    def test_stuck_consistently_low_speed(self):
        # all speeds very low
        history = []
        for i in range(5):
            history.append(self.create_run(0.2, {'p': i}))
        self.assertTrue(self.detector.detect_stuck(history))

    def test_variable_window_size(self):
        # larger window: no progress
        det = StuckDetector(N=10)
        history = []
        for i in range(10):
            history.append(self.create_run(1.0, {'p': i}))
        self.assertTrue(det.detect_stuck(history))
        
        # smaller window: clear progress
        det = StuckDetector(N=4)
        history = []
        for i in range(4):
            history.append(self.create_run(1.0 + i * 0.2, {'p': i}))
        self.assertFalse(det.detect_stuck(history))

    def test_regression(self):
        # start fast, end slow
        history = []
        speeds = [2.0, 1.8, 1.5, 1.2, 1.0]
        for s in speeds:
            history.append(self.create_run(s, {'p': 1}))
        self.assertTrue(self.detector.detect_stuck(history))

    def test_not_stuck_too_few_runs(self):
        # fewer than N runs
        history = []
        for i in range(3):
            history.append(self.create_run(1.0 + i * 0.1, {'p': i}))
        self.assertFalse(self.detector.detect_stuck(history))

    def test_not_stuck_with_small_noise_and_trend_up(self):
        # noisy but trending up
        speeds = [1.0, 1.05, 0.98, 1.1, 1.2]
        history = []
        for i, s in enumerate(speeds):
            history.append(self.create_run(s, {'p': i}))
        self.assertFalse(self.detector.detect_stuck(history))

    def test_not_stuck_after_escape_from_plateau(self):
        # first flat, then improvement
        history = []
        for _ in range(3):
            history.append(self.create_run(1.0, {'p': 1.0}))
        history.append(self.create_run(1.3, {'p': 1.5}))
        history.append(self.create_run(1.6, {'p': 2.0}))
        self.assertFalse(self.detector.detect_stuck(history))

    def test_three_distinct_configs_not_oscillation(self):
        # three configs, not just 1–2
        history = []
        configs = [1.0, 2.0, 3.0, 1.0, 2.0]
        for i, c in enumerate(configs):
            # add small noise to avoid plateau
            s = 1.0 + (i % 2) * 0.1
            history.append(self.create_run(s, {'p': c}))
        self.assertFalse(self.detector.detect_stuck(history))

    def test_sensitivity_improvement(self):
        # improvement threshold is 0.05
        # <= 0.05 -> stuck
        for imp in [0.0, 0.04, 0.05]:
            history = []
            for _ in range(4):
                history.append(self.create_run(1.0, {'p': 1}))
            history.append(self.create_run(1.0 + imp, {'p': 2}))
            self.assertTrue(
                self.detector.detect_stuck(history),
                f"should be stuck with improvement {imp}"
            )
            
        # > 0.05 -> not stuck
        for imp in [0.051, 0.06, 0.1]:
            history = []
            for _ in range(4):
                history.append(self.create_run(1.0, {'p': 1}))
            history.append(self.create_run(1.0 + imp, {'p': 2}))
            self.assertFalse(
                self.detector.detect_stuck(history),
                f"should not be stuck with improvement {imp}"
            )

    def test_sensitivity_plateau(self):
        # plateau stdev threshold is 0.02
        # low noise -> plateau
        history = []
        noise = 0.01
        for i in range(5):
            s = 1.0 + (1 if i % 2 == 0 else -1) * noise
            history.append(self.create_run(s, {'p': 1.0 + i * 0.1}))
        self.assertTrue(
            self.detector.detect_stuck(history),
            "should be stuck with low noise"
        )

        # higher noise -> not plateau
        history = []
        noise = 0.03
        for i in range(5):
            s = 1.0 + (1 if i % 2 == 0 else -1) * noise
            history.append(self.create_run(s, {'p': 1.0 + i * 0.1}))
        self.assertFalse(
            self.detector.detect_stuck(history),
            "should not be stuck with higher noise"
        )


if __name__ == '__main__':
    unittest.main()