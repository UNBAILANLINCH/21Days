$ErrorActionPreference = 'Stop'
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$hookPayload = [Console]::In.ReadToEnd()
$projectPython = & uv python find
if ($LASTEXITCODE -ne 0) { throw 'Python runtime unavailable; configure uv.' }
$env:PYTHONUTF8 = '1'
$hookPayload | & $projectPython -B (Join-Path $PSScriptRoot 'adapter.py')
exit $LASTEXITCODE
