[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9./:_-]+$')][string]$Image,
    [switch]$Push
)
$ErrorActionPreference = 'Stop'
$release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$server = Join-Path $release 'Server'
if (!(Test-Path -LiteralPath (Join-Path $server 'Remotely_Server.dll'))) { throw 'Use the output of Build-Release.ps1.' }
foreach ($file in @('Remotely-Setup-win-x64.zip', 'Remotely-Win-x64.zip')) {
    foreach ($suffix in @('', '.manifest.json', '.sig')) {
        if (!(Test-Path -LiteralPath (Join-Path $server ('wwwroot/Content/' + $file + $suffix)))) { throw ('Missing signed installer artifact: ' + $file + $suffix) }
    }
}
$repo = Split-Path $PSScriptRoot -Parent
Copy-Item -LiteralPath (Join-Path $repo 'Server/Dockerfile.release') -Destination (Join-Path $release 'Dockerfile')
$dockerIgnore = @'
*
!Server
!Server/**
Server/AppData
Server/**/*.private.pem
Server/**/*.db
'@
[System.IO.File]::WriteAllText((Join-Path $release '.dockerignore'), $dockerIgnore)
$nativeDocker = Get-Command docker -ErrorAction SilentlyContinue
if ($nativeDocker) { docker build --tag $Image $release }
else { wsl.exe -d Remotely-Dev -u root --cd $release --exec docker build --tag $Image . }
if ($LASTEXITCODE) { throw 'Image build failed.' }
if ($Push) {
    if ($nativeDocker) { docker push $Image }
    else { wsl.exe -d Remotely-Dev -u root --exec docker push $Image }
    if ($LASTEXITCODE) { throw 'Image push failed.' }
}
Write-Output ('Use REMOTELY_IMAGE=' + $Image + ' in docker-compose/.env.')
