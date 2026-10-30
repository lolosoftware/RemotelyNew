[CmdletBinding()]
param([ValidatePattern("^[a-z0-9][a-z0-9./:@_-]+$")][string]$Image = "ghcr.io/lolosoftware/remotelynew:0.1.1-dev")
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (Get-Command docker -ErrorAction SilentlyContinue) {
    Push-Location $repo
    $previousImage = $env:REMOTELY_IMAGE
    try { $env:REMOTELY_IMAGE = $Image; docker compose -p remotely-local -f docker-compose/compose.local.yml up -d --wait }
    finally { $env:REMOTELY_IMAGE = $previousImage; Pop-Location }
} else {
    # systemd services alone do not keep WSL alive after its last Windows client exits.
    $keepalive = Get-CimInstance Win32_Process -Filter "name='wsl.exe'" | Where-Object { $_.CommandLine -like '*--distribution Remotely-Dev --exec sleep infinity*' }
    if (!$keepalive) {
        Start-Process -FilePath 'wsl.exe' -ArgumentList @('--distribution','Remotely-Dev','--exec','sleep','infinity') -WindowStyle Hidden
    }
    wsl.exe -d Remotely-Dev -u root --cd $repo --exec env "REMOTELY_IMAGE=$Image" docker compose -p remotely-local -f docker-compose/compose.local.yml up -d --wait
}
if ($LASTEXITCODE) { throw 'Local deployment failed. Check Docker and the image build.' }
Write-Output 'Local recipe: http://localhost:5080 — create your account and enroll MFA on first use.'
