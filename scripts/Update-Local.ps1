# Builds your local BrimDeck and swaps it into the installed copy, then restarts it.
# Fast path: incremental publish of the main app only, no installer, no Sensors rebuild.
param([switch]$NoStart)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$install = Join-Path $env:LOCALAPPDATA 'Programs\BrimDeck'
$out = Join-Path $root '.artifacts\local-update'
$sensors = Join-Path $root 'artifacts\releases\BrimDeck.Sensors.exe'

function Get-CrashTail([string]$path) {
  if (-not (Test-Path $path)) { return '(no crash.log)' }
  return ((Get-Content $path -Tail 40) -join "`n")
}

if (-not (Test-Path (Join-Path $install 'BrimDeck.exe'))) { throw "BrimDeck is not installed at $install. Run the Setup once first." }

$sw = [Diagnostics.Stopwatch]::StartNew()
Write-Host 'Building...' -ForegroundColor Cyan
$dotnetArgs = @('publish', (Join-Path $root 'src\BrimDeck\BrimDeck.csproj'), '-c', 'Release', '-r', 'win-x64',
  '--self-contained', 'true', '-p:PublishSingleFile=false', '-o', $out, '--nologo', '-v', 'q')
if (Test-Path $sensors) { $dotnetArgs += "-p:SensorsSha256=$((Get-FileHash $sensors -Algorithm SHA256).Hash.ToLowerInvariant())" }
& dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed, installed BrimDeck left untouched.' }

Write-Host 'Swapping in new build...' -ForegroundColor Cyan
Get-Process BrimDeck -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
robocopy $out $install /E /NFL /NDL /NJH /NJS /NP /R:3 /W:1 | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copy failed (robocopy exit $LASTEXITCODE)." }

if (-not $NoStart) {
  $exe = Join-Path $install 'BrimDeck.exe'
  $crash = Join-Path $env:LOCALAPPDATA 'BrimDeck\crash.log'
  $crashBefore = if (Test-Path $crash) { (Get-Item $crash).LastWriteTimeUtc } else { [datetime]::MinValue }
  $proc = Start-Process $exe -PassThru
  Write-Host 'Checking startup...' -ForegroundColor Cyan
  Start-Sleep -Seconds 3
  if ($proc.HasExited) {
    Write-Host (Get-CrashTail $crash) -ForegroundColor Red
    throw "BrimDeck exited immediately (code $($proc.ExitCode)). See crash.log above."
  }
  if (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) {
    Write-Host (Get-CrashTail $crash) -ForegroundColor Red
    throw "BrimDeck failed to stay running. See crash.log above."
  }
  # UI-thread faults are logged and the process may keep running with a broken panel.
  if ((Test-Path $crash) -and (Get-Item $crash).LastWriteTimeUtc -gt $crashBefore) {
    Write-Host (Get-CrashTail $crash) -ForegroundColor Red
    throw "BrimDeck wrote crash.log after start (UI fault). See above."
  }
}
Write-Host ("Done in {0:N0}s" -f $sw.Elapsed.TotalSeconds) -ForegroundColor Green
