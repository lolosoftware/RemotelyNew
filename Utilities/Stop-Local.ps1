[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (Get-Command docker -ErrorAction SilentlyContinue) {
    Push-Location $repo
    try { docker compose -p remotely-local -f docker-compose/compose.local.yml down } finally { Pop-Location }
} else {
    wsl.exe -d Remotely-Dev -u root --cd $repo --exec docker compose -p remotely-local -f docker-compose/compose.local.yml down
}
if ($LASTEXITCODE) { throw 'Could not stop the local deployment.' }
Get-CimInstance Win32_Process -Filter "name='wsl.exe'" | Where-Object { $_.CommandLine -like '*--distribution Remotely-Dev --exec sleep infinity*' } | ForEach-Object { Stop-Process -Id $_.ProcessId }
Write-Output 'Stopped. Database, configuration and keys were retained in the named volume.'
