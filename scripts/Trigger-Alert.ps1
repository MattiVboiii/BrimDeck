# Triggers a device warning notice. With Settings › Expand warning notices on, opens the expanded card panel;
# otherwise uses the upstream collapsed notice. BrimDeck must already be running.
$ErrorActionPreference = 'Stop'
$name = "Local\BrimDeck.QuotaAlert.$env:USERNAME"
try {
  $handle = [System.Threading.EventWaitHandle]::OpenExisting($name)
} catch [System.Threading.WaitHandleCannotBeOpenedException] {
  throw "BrimDeck is not running (or this build has no alert trigger). Start it, then run trigger-alert.cmd again."
}
try {
  [void]$handle.Set()
  Write-Host "Alert triggered." -ForegroundColor Green
} finally {
  $handle.Dispose()
}
