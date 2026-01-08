param(
    [string]$BaseUrl = "http://localhost:8080",
    [string]$SessionId = "demo-session",
    [string]$LegId = "leg-0",
    [switch]$AppendTimestamp,
    [int]$DelayMs = 250
)

if ($AppendTimestamp) {
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $SessionId = "$SessionId-$timestamp"
}

Write-Host "Using session id: $SessionId"

function Send-Trial {
    param(
        [string]$RunId,
        [double]$Speed,
        [double]$Range,
        [double]$Baseline,
        [double]$Relation,
        [hashtable]$Safety = @{}
    )

    $body = @{
        session_id = $SessionId
        run_id = $RunId
        timestamp = (Get-Date).ToUniversalTime().ToString("o")
        params = @{
            speed = $Speed
            range = $Range
            baseline_position = $Baseline
            relation = $Relation
        }
        limb_speed = $Speed
        leg_id = $LegId
        safety = $Safety
        goal_type = "speed"
    }

    $response = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/trials" -Body ($body | ConvertTo-Json -Depth 4) -ContentType "application/json"
    $tier = if ($response.hint) { $response.hint.tier } else { "" }
    Write-Host "$RunId: stuck=$($response.stuck) tier=$tier budget=$($response.hint_budget_remaining)"
    if ($response.hint) {
        Write-Host "  hint: $($response.hint.text)"
    }
}

$warmup = @(
    @{ id = "warm-1"; speed = 0.2; range = 0; baseline = 70; relation = 0 },
    @{ id = "warm-2"; speed = 0.2; range = 0; baseline = 90; relation = 0 },
    @{ id = "warm-3"; speed = 0.2; range = 0; baseline = 105; relation = 0 },
    @{ id = "warm-4"; speed = 0.2; range = 0; baseline = 112; relation = 0 }
)

$plateau = @(
    @{ id = "plat-1"; speed = 0.2; range = 0; baseline = 112; relation = 0 },
    @{ id = "plat-2"; speed = 0.2; range = 0; baseline = 112; relation = 0 },
    @{ id = "plat-3"; speed = 0.2; range = 0; baseline = 112; relation = 0 },
    @{ id = "plat-4"; speed = 0.2; range = 0; baseline = 112; relation = 0 }
)

$safety = @(
    @{ id = "safe-1"; speed = 0.2; range = 0; baseline = 112; relation = 0; safety = @{ overtemp = $true } }
)

$recovery = @(
    @{ id = "rec-1"; speed = 0.2; range = 0; baseline = 115; relation = 0 },
    @{ id = "rec-2"; speed = 0.2; range = 10; baseline = 115; relation = 0 }
)

Write-Host "-- Warmup --"
foreach ($trial in $warmup) {
    Send-Trial -RunId $trial.id -Speed $trial.speed -Range $trial.range -Baseline $trial.baseline -Relation $trial.relation
    Start-Sleep -Milliseconds $DelayMs
}

Write-Host "-- Plateau --"
foreach ($trial in $plateau) {
    Send-Trial -RunId $trial.id -Speed $trial.speed -Range $trial.range -Baseline $trial.baseline -Relation $trial.relation
    Start-Sleep -Milliseconds $DelayMs
}

Write-Host "-- Safety --"
foreach ($trial in $safety) {
    Send-Trial -RunId $trial.id -Speed $trial.speed -Range $trial.range -Baseline $trial.baseline -Relation $trial.relation -Safety $trial.safety
    Start-Sleep -Milliseconds $DelayMs
}

Write-Host "-- Recovery --"
foreach ($trial in $recovery) {
    Send-Trial -RunId $trial.id -Speed $trial.speed -Range $trial.range -Baseline $trial.baseline -Relation $trial.relation
    Start-Sleep -Milliseconds $DelayMs
}

$status = Invoke-RestMethod -Uri "$BaseUrl/api/tutor/status?session_id=$SessionId&leg_id=$LegId"
Write-Host "Status: stuck=$($status.stuck) budget=$($status.hint_budget_remaining)"