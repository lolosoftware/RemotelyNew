[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (Get-Command docker -ErrorAction SilentlyContinue) {
    Push-Location $repo
    try { docker compose -p remotely-local -f docker-compose/compose.local.yml up -d --wait } finally { Pop-Location }
} else {
    # systemd services alone do not keep WSL alive after its last Windows client exits.
    $keepalive = Get-CimInstance Win32_Process -Filter "name='wsl.exe'" | Where-Object { $_.CommandLine -like '*--distribution Remotely-Dev --exec sleep infinity*' }
    if (!$keepalive) {
        Start-Process -FilePath 'wsl.exe' -ArgumentList @('--distribution','Remotely-Dev','--exec','sleep','infinity') -WindowStyle Hidden
    }
    wsl.exe -d Remotely-Dev -u root --cd $repo --exec docker compose -p remotely-local -f docker-compose/compose.local.yml up -d --wait
}
if ($LASTEXITCODE) { throw 'Local deployment failed. Check Docker and the image build.' }
Write-Output 'Local recipe: http://localhost:5080 — create your account and enroll MFA on first use.'
