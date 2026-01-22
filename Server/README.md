# AI_TUTOR Server (EDMO ServerVNext + Tutor)

This folder contains the customized EDMO server and UI with tutor hints, stuck-state detection, and A/B hint modes for user studies.

Repository: https://github.com/MKBasaran/AI_TUTOR

## What’s new in our version

The following additions were implemented on top of the original EDMO ServerVNext codebase:

- **Tutor backend** (C#) integrated into `ServerCore/Tutor`
  - Rule-based stuck detection via `stuck_detector.py`
  - Three-tier hint scaffold (Reflection → Micro → Pattern)
  - Per-user and global voting hint modes
  - Hint budgets, cooldowns, and non-disclosure safeguards
  - Per-session tutor logs (`tutor_trials.jsonl`, `tutor_stuck_reports.jsonl`)
- **Tutor UI panel** embedded in the controller page
  - Shows status, hints, budget, and hint controls
  - “Get hint” and “Mark stuck” actions
- **Demo controller page** for testing without hardware

Original EDMO functionality (robot communication, sessions, and baseline UI) remains unchanged.

## Repo layout (server side)

- `ServerVNext/EDMOFrontend` — Blazor server + UI
- `ServerVNext/ServerCore` — EDMO comms, sessions, tutor engine
- `stuck_detector.py` — Python stuck-state detector

---

# Running the server (development)

## Requirements

- .NET 9 SDK  
- Node.js + npm (for frontend assets)  
- Python 3.12 (for stuck detector)

## Install frontend dependencies (one time)

```powershell
cd Server/ServerVNext/EDMOFrontend/npm
npm install
```

## Start server

```powershell
cd Server/ServerVNext
dotnet run --project EDMOFrontend/EDMOFrontend.csproj
```

The server runs on **http://localhost:8080** by default.

---

# UI entry points

## Normal UI (lab use)

Open:

```
http://localhost:8080/
```

Follow the controller setup flow on each tablet.

## Demo UI (no hardware required)

```
http://localhost:8080/demo?session_id=demo-session&leg_id=leg-0
```

Parameters:

- `session_id`: any string (used for logging and session state)
- `leg_id`: `leg-0`, `leg-1`, `leg-2`, `leg-3` (controls per-leg tutor state)

Example:

```
http://localhost:8080/demo?session_id=test-session&leg_id=leg-2
```

---

# Tutor configuration

Tutor settings are in:

- `Server/ServerVNext/EDMOFrontend/appsettings.json`
- `Server/ServerVNext/EDMOFrontend/appsettings.Development.json`

Example:

```json
"Tutor": {
  "StuckDetectorPath": "..\\..\\..\\..\\..\\stuck_detector.py",
  "StuckWindow": 5,
  "HintBudget": 6,
  "HistoryLimit": 25,
  "EscalationTrials": 3,
  "ProgressThreshold": 0.02,
  "ParamDeltaEpsilon": 0.05,
  "IncludeDiagnostics": false,
  "HintMode": "per_leg",
  "HintVoteThreshold": 3,
  "HintVoteTotal": 4
}
```

Hint modes:

- `per_leg`: each tablet has its own hint budget
- `global_majority`: hints unlock when vote threshold is met (e.g., 3/4)

For a biped study, set `HintVoteTotal = 2` and `HintVoteThreshold = 2`.

---

# Stuck detector (Python.NET)

The tutor calls `stuck_detector.py`. If Python.NET can’t find Python:

- Ensure Python 3.12 is on PATH, or
- Set `PYTHONNET_PYDLL` to your `python312.dll`

---

# Logs

Logs are written per server run and per session:

```
EDMOFrontend/bin/Debug/net9.0/Logs/<run>/
  runtime.log
  Sessions/<yyyyMMdd>/<SessionId>/
    tutor_trials.jsonl
    tutor_stuck_reports.jsonl
    session.log
    imu.log
    oscillator*.log
    user*.log
```

`tutor_trials.jsonl` includes `hint_mode` so you can track A/B condition.

---

# Notes

- If UI styles are missing, re-run `npm install` and restart the server.
- Demo mode does not require hardware.

---

# License

See `Server/ServerVNext/LICENCE`.
