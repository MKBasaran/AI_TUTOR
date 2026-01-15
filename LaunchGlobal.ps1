$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$serverRoot = Join-Path $repoRoot "Server\ServerVNext"
$npmDir = Join-Path $serverRoot "EDMOFrontend\npm"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue))
{
    Write-Error "dotnet SDK not found. Install .NET 9 SDK first."
}

if (-not (Get-Command npm -ErrorAction SilentlyContinue))
{
    Write-Error "npm not found. Install Node.js first."
}

if (-not (Test-Path (Join-Path $npmDir "node_modules")))
{
    Push-Location $npmDir
    npm install
    Pop-Location
}

Push-Location $serverRoot

dotnet run --project EDMOFrontend/EDMOFrontend.csproj -- `
  --Tutor:HintMode=global_majority --Tutor:HintVoteThreshold=3 --Tutor:HintVoteTotal=4

Pop-Location
