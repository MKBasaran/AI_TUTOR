import math
from statistics import mean, stdev


class StuckDetector:
    def __init__(self, N=5):
        # number of recent runs to look at
        if not isinstance(N, int) or isinstance(N, bool) or N <= 0:
            raise ValueError("N must be a positive integer.")
        self.N = N
        
        # thresholds
        self.min_imp = 0.05  # minimum speed improvement
        self.min_diff = 0.02  # minimum parameter change
        self.sim_thresh = 0.05  # similarity threshold
        self.plat_thresh = 0.02  # low variance threshold


    def detect_stuck(self, history):
        """
        Analyses recent history to determine if the optimiser is stuck.
        Returns True if intervention is required.
        """
        self._validate_history(history)
        # main entry point
        if len(history) < self.N:
            return False

        recent = history[-self.N:]

        # immediate stuck case
        if self.check_fails(recent):
            return True
        
        # if no progress, try extra checks
        if self.check_prog(recent):
            if self.check_params(recent):
                return True
            if self.check_osc(recent):
                return True
            if self.check_plat(recent):
                return True
            if self.check_low(recent):
                return True
            if self.check_regression(recent):
                return True

        return False


    def _validate_history(self, history):
        if not isinstance(history, list):
            raise TypeError("History must be a list of run dicts.")

        for idx, run in enumerate(history):
            if not isinstance(run, dict):
                raise TypeError(f"Run at index {idx} must be a dict.")

            if 'speed_mps' not in run or 'params' not in run:
                raise ValueError(f"Run at index {idx} missing required keys.")

            speed = run['speed_mps']
            if not isinstance(speed, (int, float)) or isinstance(speed, bool):
                raise TypeError(f"Run at index {idx} speed_mps must be a number.")
            if not math.isfinite(speed):
                raise ValueError(f"Run at index {idx} speed_mps must be finite.")
            if speed < 0:
                raise ValueError(f"Run at index {idx} speed_mps must be non-negative.")

            params = run['params']
            if not isinstance(params, dict):
                raise TypeError(f"Run at index {idx} params must be a dict.")
            if len(params) == 0:
                raise ValueError(f"Run at index {idx} params must not be empty.")

            for key, value in params.items():
                if not isinstance(value, (int, float)) or isinstance(value, bool):
                    raise TypeError(
                        f"Run at index {idx} param '{key}' must be a number."
                    )
                if not math.isfinite(value):
                    raise ValueError(
                        f"Run at index {idx} param '{key}' must be finite."
                    )

            for flag in ('overcurrent', 'overtemp', 'timeout'):
                if flag in run and not isinstance(run[flag], bool):
                    raise TypeError(
                        f"Run at index {idx} flag '{flag}' must be boolean."
                    )


    def check_fails(self, recent):
        """
        Checks if the robot has consistently failed (overcurrent/temp/timeout).
        """
        # true if all recent runs failed
        fails = 0
        for r in recent:
            if r.get('overcurrent') or r.get('overtemp') or r.get('timeout'):
                fails += 1
        return fails == len(recent)


    def check_prog(self, recent):
        """
        Checks if speed is failing to improve significantly.
        """
        # true if no real speed improvement
        speeds = [r['speed_mps'] for r in recent]
        
        # split window
        mid = len(speeds) // 2
        first = speeds[:mid]
        last = speeds[mid:]
        
        if not last:
            return False 
        
        # compare best values
        return max(last) <= max(first) + self.min_imp


    def check_low(self, recent):
        """
        Checks if absolute speed remains very low.
        """
        # true if speeds are very low
        speeds = [r['speed_mps'] for r in recent]
        return all(s < 0.5 for s in speeds)


    def check_params(self, recent):
        """
        Checks if parameters are stagnant (minimal exploration).
        """
        # true if params barely change
        params = [r['params'] for r in recent]
        keys = params[0].keys()
        
        for k in keys:
            vals = [p[k] for p in params]
            if isinstance(vals[0], int):
                if len(set(vals)) > 1:
                    return False
            else:
                if max(vals) - min(vals) > self.min_diff:
                    return False
                    
        return True


    def check_osc(self, recent):
        """
        Checks if the optimiser is oscillating between two similar configurations.
        """
        # true if flipping between same configs
        params = [r['params'] for r in recent]
        configs = []
        for p in params:
            new_conf = True
            for c in configs:
                if self._sim(p, c):
                    new_conf = False
                    break
            if new_conf:
                configs.append(p)
                
        return 1 < len(configs) <= 2


    def _sim(self, p1, p2):
        """
        Helper to measure similarity between two parameter sets.
        """
        for k in p1:
            v1 = p1[k]
            v2 = p2[k]
            if isinstance(v1, (int, float)) and isinstance(v2, (int, float)):
                if abs(v1 - v2) > self.sim_thresh:
                    return False
            elif v1 != v2:
                return False
        return True


    def check_plat(self, recent):
        """
        Checks if performance has plateaued (low variance in speed).
        """
        # true if speeds barely vary
        speeds = [r['speed_mps'] for r in recent]
        if len(speeds) < 2:
            return False
        return stdev(speeds) < self.plat_thresh


    def check_regression(self, recent):
        """
        Checks if performance is actively degrading (regression).
        """
        # true if performance is actively getting worse
        speeds = [r['speed_mps'] for r in recent]
        mid = len(speeds) // 2
        first = speeds[:mid]
        last = speeds[mid:]
        
        if not first or not last:
            return False
            
        # check if average speed dropped significantly
        return mean(last) < mean(first) * 0.9
