# Tutor Integration (ServerVNext)

## Run (local)

1) Start the server + UI:

```powershell
cd .\Server\ServerVNext

dotnet run --project EDMOFrontend\EDMOFrontend.csproj
```

2) Open the real controller UI (requires EDMO hardware/session):

```
http://localhost:8080/
```

3) Open the demo UI (no hardware required):

```
http://localhost:8080/demo
```

Optional: pick a leg and session for demo with query params:

```
http://localhost:8080/demo?session_id=demo-session&leg_id=leg-1
```

## API

### POST /api/trials

Request:

```json
{
  "session_id": "demo-session",
  "run_id": "run-1",
  "timestamp": "2026-01-08T10:00:00Z",
  "params": {
    "speed": 0.2,
    "range": 10,
    "baseline_position": 115,
    "relation": 0
  },
  "limb_speed": 0.2,
  "leg_id": "leg-0",
  "goal_type": "speed",
  "safety": {
    "overcurrent": false,
    "overtemp": false,
    "timeout": false
  }
}
```

Response:

```json
{
  "stuck": false,
  "hint": null,
  "hint_budget_remaining": 6,
  "hint_available": false
}
```

### GET /api/tutor/status

```
GET /api/tutor/status?session_id=demo-session&leg_id=leg-0
```

Response:

```json
{
  "stuck": true,
  "last_hint": {
    "tier": "Micro",
    "text": "Try nudging Speed up and watch for smoother motion.",
    "parameter_directions": {
      "speed": "Increase",
      "range": "Keep"
    }
  },
  "hint_budget_remaining": 4,
  "hint_available": true
}
```

### POST /api/tutor/hint

Request:

```json
{
  "session_id": "demo-session",
  "leg_id": "leg-0"
}
```

Response:

```json
{
  "stuck": true,
  "hint": {
    "tier": "Reflection",
    "text": "What changed between recent trials? Try a single adjustment and compare stability with speed.",
    "parameter_directions": {}
  },
  "hint_budget_remaining": 5,
  "hint_available": false
}
```

## Demo Script

A canned sequence to exercise stuck detection and hint escalation:

```powershell
cd .\Server\ServerVNext
powershell -ExecutionPolicy Bypass -File .\scripts\tutor_demo.ps1 -AppendTimestamp
```

## Telemetry Hook Points

- Real robot telemetry should populate `speed_mps` (or a session score) per trial.
- The frontend auto-submits trial params every 2 seconds and sends `limb_speed` as the local speed slider value.
- If you have a true robot-level score, set `speed_mps` in the trial payload and the stuck detector will use it.

## Privacy Note

This integration logs only control parameters, trial scores, and safety flags. It does not capture facial or emotion data.