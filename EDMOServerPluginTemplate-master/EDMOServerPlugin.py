import random
import os
import csv
from datetime import datetime

class EDMOPythonPlugin:
    """
    Parameter exploration plugin for EDMO robot.
    Sets random parameters within bounds at session start.
    """
    
    # ============ EDIT THESE BEFORE LAB ============
    FREQ_MIN  = 0.8
    FREQ_MAX  = 1.5
    AMP_MIN   = 30.0
    AMP_MAX   = 70.0
    OFFSET_MIN = 70.0
    OFFSET_MAX = 110.0
    PHASE_MIN  = 0.0
    PHASE_MAX  = 180.0
    # ================================================
    
    def __init__(self, edmoPlugin):
        """
        Minimal initialization - just store reference.
        Heavy work happens in sessionStarted() when everything is ready.
        """
        print("[EDMO Plugin] __init__ started")
        
        try:
            self.edmo = edmoPlugin
            print("[EDMO Plugin] ✓ edmoPlugin reference stored")
            
            # Initialize state flags
            self.initialized = False
            self.session = None
            self.storage_dir = None
            self.csv_path = None
            self.setters_available = False
            
            print("[EDMO Plugin] ✓ __init__ completed successfully")
            
        except Exception as e:
            print(f"[EDMO Plugin] ✗ ERROR in __init__: {e}")
            import traceback
            traceback.print_exc()
            raise
    
    def getName(self):
        print("[EDMO Plugin] getName() called")
        return "ParameterExplorationPlugin"
    
    def sessionStarted(self):
        """
        Called when session is fully initialized.
        This is where we do the heavy lifting.
        """
        print("=" * 70)
        print("[EDMO Plugin] sessionStarted() called")
        print("[EDMO Plugin] ServerVNext has finished initializing the session")
        print("=" * 70)
        
        # Step 1: Initialize session access
        if not self._initialize_session():
            print("[EDMO Plugin] ✗ Session initialization failed, aborting")
            return
        
        # Step 2: Check API availability
        if not self._check_api():
            print("[EDMO Plugin] ✗ API check failed, aborting")
            return
        
        # Step 3: Initialize CSV logging
        if not self._initialize_csv():
            print("[EDMO Plugin] ⚠ CSV initialization failed (non-critical)")
        
        # Step 4: Apply parameters
        self._apply_parameters()
        
        print("=" * 70)
    
    def _initialize_session(self):
        """
        Step 1: Get session and storage directory.
        Returns True if successful, False otherwise.
        """
        print("[EDMO Plugin] Step 1: Initializing session access...")
        
        try:
            # Try to access session
            print("[EDMO Plugin]   → Accessing edmoPlugin.session")
            self.session = self.edmo.session
            print("[EDMO Plugin]   ✓ Session object obtained")
            
            # Try to get storage directory
            print("[EDMO Plugin]   → Getting SessionStorageDirectory")
            self.storage_dir = self.session.SessionStorageDirectory.ToString()
            print(f"[EDMO Plugin]   ✓ Storage directory: {self.storage_dir}")
            
            self.initialized = True
            return True
            
        except AttributeError as e:
            print(f"[EDMO Plugin]   ✗ AttributeError: {e}")
            print("[EDMO Plugin]   → session object not ready or missing attribute")
            return False
            
        except Exception as e:
            print(f"[EDMO Plugin]   ✗ Unexpected error: {e}")
            import traceback
            traceback.print_exc()
            return False
    
    def _check_api(self):
        """
        Step 2: Check if setter methods are available.
        Returns True if all setters available, False otherwise.
        """
        print("[EDMO Plugin] Step 2: Checking API availability...")
        
        try:
            # Check each setter
            checks = {
                'SetFrequency': hasattr(self.edmo, 'SetFrequency') and callable(getattr(self.edmo, 'SetFrequency', None)),
                'SetAmplitude': hasattr(self.edmo, 'SetAmplitude') and callable(getattr(self.edmo, 'SetAmplitude', None)),
                'SetOffset': hasattr(self.edmo, 'SetOffset') and callable(getattr(self.edmo, 'SetOffset', None)),
                'SetPhaseShift': hasattr(self.edmo, 'SetPhaseShift') and callable(getattr(self.edmo, 'SetPhaseShift', None))
            }
            
            # Print results
            for method, available in checks.items():
                status = "✓" if available else "✗"
                print(f"[EDMO Plugin]   {status} {method}: {available}")
            
            self.setters_available = all(checks.values())
            
            if not self.setters_available:
                print("!" * 70)
                print("[EDMO Plugin] ERROR: Not all setter methods are accessible!")
                print("[EDMO Plugin] Available methods on edmoPlugin:")
                for attr in dir(self.edmo):
                    if not attr.startswith('_'):
                        print(f"[EDMO Plugin]     - {attr}")
                print("!" * 70)
                print("[EDMO Plugin] SOLUTION:")
                print("[EDMO Plugin]   1. Check PythonNet protected member access")
                print("[EDMO Plugin]   2. Use C# plugin instead")
                print("!" * 70)
                return False
            
            print("[EDMO Plugin]   ✓ All setter methods available")
            return True
            
        except Exception as e:
            print(f"[EDMO Plugin]   ✗ Error during API check: {e}")
            import traceback
            traceback.print_exc()
            return False
    
    def _initialize_csv(self):
        """
        Step 3: Initialize CSV logging.
        Returns True if successful, False otherwise.
        """
        print("[EDMO Plugin] Step 3: Initializing CSV logging...")
        
        try:
            # Create CSV path
            self.csv_path = os.path.join(self.storage_dir, "plugin_parameters.csv")
            print(f"[EDMO Plugin]   → CSV path: {self.csv_path}")
            
            # Create CSV header
            with open(self.csv_path, 'w', newline='') as f:
                writer = csv.writer(f)
                writer.writerow([
                    'timestamp', 'frequency',
                    'osc0_amp', 'osc0_off', 'osc0_phs',
                    'osc1_amp', 'osc1_off', 'osc1_phs',
                    'osc2_amp', 'osc2_off', 'osc2_phs',
                    'osc3_amp', 'osc3_off', 'osc3_phs'
                ])
            print("[EDMO Plugin]   ✓ CSV file created successfully")
            return True
            
        except Exception as e:
            print(f"[EDMO Plugin]   ✗ CSV initialization failed: {e}")
            print("[EDMO Plugin]   ⚠ Continuing without CSV backup")
            import traceback
            traceback.print_exc()
            return False
    
    def _apply_parameters(self):
        """
        Step 4: Generate and apply random parameters.
        """
        print("[EDMO Plugin] Step 4: Generating and applying parameters...")
        
        try:
            # Generate random parameters
            print("[EDMO Plugin]   → Generating random parameters")
            params = self._generate_random_params()
            print("[EDMO Plugin]   ✓ Parameters generated:")
            print(f"[EDMO Plugin]     Bounds: Freq=[{self.FREQ_MIN}, {self.FREQ_MAX}] Hz")
            print(f"[EDMO Plugin]            Amp=[{self.AMP_MIN}, {self.AMP_MAX}]°")
            print(f"[EDMO Plugin]            Off=[{self.OFFSET_MIN}, {self.OFFSET_MAX}]°")
            print(f"[EDMO Plugin]            Phase=[{self.PHASE_MIN}, {self.PHASE_MAX}]°")
            
            # Set frequency (global)
            freq = params['freq']
            print(f"[EDMO Plugin]   → Setting global frequency: {freq:.2f} Hz")
            self.edmo.SetFrequency(freq)
            print("[EDMO Plugin]   ✓ Frequency set")
            
            # Set individual oscillator parameters
            for i in range(4):
                amp = params[f'osc{i}_amp']
                off = params[f'osc{i}_off']
                phs = params[f'osc{i}_phs']
                
                print(f"[EDMO Plugin]   → Oscillator {i}:")
                print(f"[EDMO Plugin]       Amplitude:  {amp:.1f}°")
                print(f"[EDMO Plugin]       Offset:     {off:.1f}°")
                print(f"[EDMO Plugin]       PhaseShift: {phs:.1f}°")
                
                self.edmo.SetAmplitude(i, amp)
                self.edmo.SetOffset(i, off)
                self.edmo.SetPhaseShift(i, phs)
                
                print(f"[EDMO Plugin]   ✓ Oscillator {i} configured")
            
            # Log to CSV if available
            if self.csv_path:
                print("[EDMO Plugin]   → Logging parameters to CSV")
                self._append_csv(params)
                print("[EDMO Plugin]   ✓ Parameters logged to CSV")
            
            print("[EDMO Plugin] ✓ ALL PARAMETERS APPLIED SUCCESSFULLY!")
            
        except Exception as e:
            print("!" * 70)
            print(f"[EDMO Plugin] ✗ CRITICAL ERROR applying parameters: {e}")
            print("!" * 70)
            import traceback
            traceback.print_exc()
            print("!" * 70)
    
    def sessionEnded(self):
        """
        Called when session ends.
        """
        print("=" * 70)
        print("[EDMO Plugin] sessionEnded() called")
        
        try:
            if self.csv_path:
                print(f"[EDMO Plugin] CSV backup: {self.csv_path}")
            
            if self.storage_dir:
                print(f"[EDMO Plugin] Full logs: {self.storage_dir}")
            
            print("[EDMO Plugin] Session ended successfully")
            
        except Exception as e:
            print(f"[EDMO Plugin] Error in sessionEnded: {e}")
        
        print("=" * 70)
    
    # ============ HELPER METHODS ============
    
    def _generate_random_params(self):
        """Generate random parameters within configured bounds."""
        params = {
            'timestamp': datetime.now().isoformat(),
            'freq': random.uniform(self.FREQ_MIN, self.FREQ_MAX)
        }
        
        for i in range(4):
            params[f'osc{i}_amp'] = random.uniform(self.AMP_MIN, self.AMP_MAX)
            params[f'osc{i}_off'] = random.uniform(self.OFFSET_MIN, self.OFFSET_MAX)
            params[f'osc{i}_phs'] = random.uniform(self.PHASE_MIN, self.PHASE_MAX)
        
        return params
    
    def _append_csv(self, p):
        """Append parameters to CSV log."""
        try:
            with open(self.csv_path, 'a', newline='') as f:
                writer = csv.writer(f)
                writer.writerow([
                    p['timestamp'], p['freq'],
                    p['osc0_amp'], p['osc0_off'], p['osc0_phs'],
                    p['osc1_amp'], p['osc1_off'], p['osc1_phs'],
                    p['osc2_amp'], p['osc2_off'], p['osc2_phs'],
                    p['osc3_amp'], p['osc3_off'], p['osc3_phs']
                ])
        except Exception as e:
            print(f"[EDMO Plugin] Warning: CSV append failed: {e}")