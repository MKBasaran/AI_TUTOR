# AI_TUTOR

AI_TUTOR is a customized EDMO server + UI with tutor hints, stuck-state detection, and A/B hint modes for user studies.

Repository: https://github.com/MKBasaran/AI_TUTOR

## What runs what

- `EDMOFrontend` is the web UI + server (Blazor).
- `ServerCore` contains the robot/session logic and the tutor engine.
- `../stuck_detector.py` is the Python stuck-state detector used by the tutor.

## Requirements (development)

- .NET 9 SDK
- Node.js + npm (for frontend assets)
- Python 3.12 (for the stuck detector)

## Run (development)

From the repo root:

```powershell
cd Server/ServerVNext

# one-time frontend deps
cd EDMOFrontend/npm
npm install
cd ..\..\

# run the server
 dotnet run --project EDMOFrontend/EDMOFrontend.csproj
```

Open:
- `http://localhost:8080/` for the normal UI
- `http://localhost:8080/demo?session_id=demo-session&leg_id=leg-0` for the demo page

## Lab usage

1) Start the server as above on the host laptop.
2) On tablets/clients, open `http://<host-ip>:8080/`.
3) Use the controller setup screen to join a robot session.

> The server currently binds to port 8080 (see `EDMOFrontend/Program.cs`).

## Tutor configuration (A/B hint modes)

Tutor settings live in `EDMOFrontend/appsettings.json` and `EDMOFrontend/appsettings.Development.json` under the `Tutor` section.

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

- `per_leg` (default): each tablet has its own hint budget.
- `global_majority`: hints are released only after a vote threshold is met (e.g., 3/4). The button shows `Get hint (x/y)`.
  - For a biped study, set `HintVoteTotal` to 2 and `HintVoteThreshold` to 2.

## Stuck detector path

The tutor calls `stuck_detector.py`. Use an absolute path if needed.

If Python.NET cannot find Python:
- Ensure Python 3.12 is on PATH, or
- Set `PYTHONNET_PYDLL` to your `python312.dll`.

## Logs

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

`tutor_trials.jsonl` and `tutor_stuck_reports.jsonl` include a `hint_mode` field so you can identify A/B condition during analysis.

## Notes

- If the UI loads without styling, run `npm install` in `EDMOFrontend/npm` and restart.
- The demo page does not require hardware.

## License

See `LICENCE` in this repo.
