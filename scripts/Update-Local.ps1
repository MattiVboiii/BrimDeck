# Builds your local BrimDeck and swaps it into the installed copy, then restarts it.
# Default: publish without ReadyToRun, then copy only the app assemblies into the installed self-contained tree.
# -Full: replace the whole self-contained tree (first refresh, or after runtime/package changes).
param([switch]$NoStart, [switch]$Full)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$install = Join-Path $env:LOCALAPPDATA 'Programs\BrimDeck'
$out = Join-Path $root '.artifacts\local-update'
$sensors = Join-Path $root 'artifacts\releases\BrimDeck.Sensors.exe'
$project = Join-Path $root 'src\BrimDeck\BrimDeck.csproj'

function Get-CrashTail([string]$path) {
  if (-not (Test-Path $path)) { return '(no crash.log)' }
  return ((Get-Content $path -Tail 40) -join "`n")
}

function Stop-BrimDeck {
  Get-Process BrimDeck -ErrorAction SilentlyContinue | Stop-Process -Force
  $deadline = [Diagnostics.Stopwatch]::StartNew()
  while (Get-Process BrimDeck -ErrorAction SilentlyContinue) {
    if ($deadline.ElapsedMilliseconds -gt 5000) { throw 'BrimDeck did not exit after stop.' }
    Start-Sleep -Milliseconds 100
  }
}

function Copy-AppAssemblies([string]$from) {
  # Keep the installed self-contained host, runtime and deps.json; only refresh managed app code.
  foreach ($name in @('BrimDeck.dll', 'BrimDeck.pdb', 'BrimDeck.Core.dll', 'BrimDeck.Core.pdb')) {
    $src = Join-Path $from $name
    if (Test-Path $src) { Copy-Item $src (Join-Path $install $name) -Force }
  }
}

if (-not (Test-Path (Join-Path $install 'BrimDeck.exe'))) { throw "BrimDeck is not installed at $install. Run the Setup once first." }

$selfContained = (Test-Path (Join-Path $install 'coreclr.dll')) -or (Test-Path (Join-Path $install 'hostfxr.dll'))
$publishFull = $Full -or -not $selfContained

$sw = [Diagnostics.Stopwatch]::StartNew()
$dotnetProps = @()
if (Test-Path $sensors) { $dotnetProps += "-p:SensorsSha256=$((Get-FileHash $sensors -Algorithm SHA256).Hash.ToLowerInvariant())" }

Write-Host $(if ($publishFull) { 'Publishing...' } else { 'Building...' }) -ForegroundColor Cyan
# Local installs skip ReadyToRun: it dominates publish time and the next edit throws it away.
$dotnetArgs = @('publish', $project, '-c', 'Release', '-r', 'win-x64',
  '--self-contained', 'true', '-p:PublishSingleFile=false', '-p:PublishReadyToRun=false',
  '-o', $out, '--nologo', '-v', 'q') + $dotnetProps
& dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed, installed BrimDeck left untouched.' }

Write-Host 'Swapping in new build...' -ForegroundColor Cyan
Stop-BrimDeck
if ($publishFull) {
  robocopy $out $install /E /NFL /NDL /NJH /NJS /NP /R:3 /W:1 | Out-Null
  if ($LASTEXITCODE -ge 8) { throw "Copy failed (robocopy exit $LASTEXITCODE)." }
}
else {
  Copy-AppAssemblies $out
}

if (-not $NoStart) {
  $exe = Join-Path $install 'BrimDeck.exe'
  $crash = Join-Path $env:LOCALAPPDATA 'BrimDeck\crash.log'
  $crashBefore = if (Test-Path $crash) { (Get-Item $crash).LastWriteTimeUtc } else { [datetime]::MinValue }
  $proc = Start-Process $exe -PassThru
  Write-Host 'Checking startup...' -ForegroundColor Cyan
  $startup = [Diagnostics.Stopwatch]::StartNew()
  $stableMs = 800
  while ($startup.ElapsedMilliseconds -lt 8000) {
    Start-Sleep -Milliseconds 100
    if ($proc.HasExited) {
      Write-Host (Get-CrashTail $crash) -ForegroundColor Red
      throw "BrimDeck exited immediately (code $($proc.ExitCode)). See crash.log above."
    }
    if (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) {
      Write-Host (Get-CrashTail $crash) -ForegroundColor Red
      throw "BrimDeck failed to stay running. See crash.log above."
    }
    if ((Test-Path $crash) -and (Get-Item $crash).LastWriteTimeUtc -gt $crashBefore) {
      Write-Host (Get-CrashTail $crash) -ForegroundColor Red
      throw "BrimDeck wrote crash.log after start (UI fault). See above."
    }
    if ($startup.ElapsedMilliseconds -ge $stableMs) { break }
  }
}
Write-Host ("Done in {0:N0}s" -f $sw.Elapsed.TotalSeconds) -ForegroundColor Green
