# Publishes self-contained Linux build from this repo (run in PowerShell at repo root).
# Use the output folder app-out for rsync/scp to the droplet, or run deploy\vm-deploy.sh on the server.
param(
    [string]$OutDir = "app-out",
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"
Set-Location $RepoRoot

$csproj = Join-Path $RepoRoot "newEbook.csproj"
if (-not (Test-Path $csproj)) { throw "Missing newEbook.csproj at $csproj" }

Write-Host "[publish] Repo: $RepoRoot"
Write-Host "[publish] Output: $OutDir (linux-x64, self-contained)"

dotnet publish $csproj -c Release -r linux-x64 --self-contained true -o (Join-Path $RepoRoot $OutDir)

$outPath = Join-Path $RepoRoot $OutDir
$exe = Join-Path $outPath "EBookDashboard"
if (Test-Path $exe) { Write-Host "[publish] OK: $exe" } else { Write-Host "[publish] Check DLL: $(Join-Path $RepoRoot $OutDir 'EBookDashboard.dll')" }
Write-Host "[publish] Upload $OutDir to server, or run deploy/vm-deploy.sh on Ubuntu."
