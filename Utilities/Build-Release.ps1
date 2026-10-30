[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PrivateKeyPath,
    [Parameter(Mandatory)][ValidateRange(1, [long]::MaxValue)][long]$ReleaseSequence,
    [ValidateSet('win-x64','win-x86','linux-x64')][string[]]$RuntimeIdentifiers = @('win-x64', 'win-x86', 'linux-x64'),
    [string]$CertificateThumbprint,
    [string]$TimestampServer = "http://timestamp.digicert.com",
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/release')
)
$ErrorActionPreference = 'Stop'
$PrivateKeyPath = (Resolve-Path -LiteralPath $PrivateKeyPath).Path
$repo = Split-Path $PSScriptRoot -Parent
function Sign-OwnedBinaries([string]$directory) {
    if (!$CertificateThumbprint) { return }
    $certificate = Get-Item -LiteralPath ('Cert:/CurrentUser/My/' + $CertificateThumbprint)
    if (!$certificate.HasPrivateKey) { throw 'The code-signing certificate has no private key.' }
    Get-ChildItem -LiteralPath $directory -Recurse -File | Where-Object { $_.Name -like 'Remotely_*.exe' -or $_.Name -like 'Remotely_*.dll' } | ForEach-Object {
        $signed = Set-AuthenticodeSignature -LiteralPath $_.FullName -Certificate $certificate -HashAlgorithm SHA256 -TimestampServer $TimestampServer
        if ($signed.Status -ne 'Valid') { throw ('Authenticode signing failed: ' + $_.Name + ': ' + $signed.Status) }
    }
}
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a new output directory to preserve previous releases.' }
New-Item -ItemType Directory -Path $output | Out-Null
Push-Location $repo
try {
    pnpm --dir Server install --frozen-lockfile --ignore-scripts
    if ($LASTEXITCODE) { throw 'Web dependency restoration failed.' }
    pnpm --dir Server run build
    if ($LASTEXITCODE) { throw 'Web compilation failed.' }
    pnpm --dir Server test
    if ($LASTEXITCODE) { throw 'Browser tests failed.' }
    dotnet restore Remotely.Build.slnx --locked-mode
    if ($LASTEXITCODE) { throw 'NuGet restoration failed.' }
    dotnet test Remotely.Build.slnx -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Tests failed.' }
    $content = Join-Path $output 'Content'
    New-Item -ItemType Directory -Path $content | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repo 'Server/wwwroot/Content') -File |
        Where-Object { $_.Name -match '^Install-.*\.(ps1|sh)$' } |
        Copy-Item -Destination $content
    foreach ($rid in $RuntimeIdentifiers) {
        $agentOutput = Join-Path $output ('staging/' + $rid)
        $desktopProject = if ($rid.StartsWith('win-')) { 'Desktop.Win/Desktop.Win.csproj' } else { 'Desktop.Linux/Desktop.Linux.csproj' }
        dotnet publish Agent/Agent.csproj -c Release -r $rid --self-contained true -o $agentOutput
        if ($LASTEXITCODE) { throw ('Agent publish failed for ' + $rid) }
        dotnet publish $desktopProject -c Release -r $rid --self-contained true -o (Join-Path $agentOutput 'Desktop')
        if ($LASTEXITCODE) { throw ('Desktop publish failed for ' + $rid) }
        Sign-OwnedBinaries $agentOutput
        $zipName = switch ($rid) {
            'win-x64' { 'Remotely-Win-x64.zip' }
            'win-x86' { 'Remotely-Win-x86.zip' }
            'linux-x64' { 'Remotely-Linux.zip' }
        }
        [System.IO.Compression.ZipFile]::CreateFromDirectory($agentOutput, (Join-Path $content $zipName))
        $portableDirectory = Join-Path $content $(switch ($rid) { 'win-x64' { 'Win-x64' }; 'win-x86' { 'Win-x86' }; 'linux-x64' { 'Linux-x64' } })
        # Keep historical single-executable download URLs working; the default new UI uses a complete unpacked ZIP.
        $legacyOutput = Join-Path $output ('legacy/' + $rid)
        dotnet publish $desktopProject -c Release -r $rid --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $legacyOutput
        if ($LASTEXITCODE) { throw ('Legacy portable publish failed for ' + $rid) }
        Sign-OwnedBinaries $legacyOutput
        New-Item -ItemType Directory -Path $portableDirectory | Out-Null
        $desktopName = if ($rid.StartsWith('win-')) { 'Remotely_Desktop.exe' } else { 'Remotely_Desktop' }
        Copy-Item -LiteralPath (Join-Path $legacyOutput $desktopName) -Destination $portableDirectory
        [System.IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $agentOutput 'Desktop'), (Join-Path $content ('Remotely-Support-' + $rid + '.zip')))
    }
    & (Join-Path $PSScriptRoot 'Sign-UpdateArtifacts.ps1') -ArtifactDirectory $content -PrivateKeyPath $PrivateKeyPath -ReleaseSequence $ReleaseSequence
    $rsa = [System.Security.Cryptography.RSA]::Create()
    $publicKeyPath = Join-Path $output 'update-public-key.pem'
    try {
        $rsa.ImportFromPem([System.IO.File]::ReadAllText($PrivateKeyPath))
        [System.IO.File]::WriteAllText($publicKeyPath, $rsa.ExportSubjectPublicKeyInfoPem())
    } finally { $rsa.Dispose() }
    foreach ($rid in ($RuntimeIdentifiers | Where-Object { $_.StartsWith('win-') })) {
        $setup = Join-Path $output ('setup/' + $rid)
        dotnet publish Installer.Windows/Installer.Windows.csproj -c Release -r $rid --self-contained true -p:UpdatePublicKeyPath=$publicKeyPath -o $setup
        if ($LASTEXITCODE) { throw ('Installer publish failed for ' + $rid) }
        Sign-OwnedBinaries $setup
        $platform = $rid.Substring(4)
        foreach ($suffix in @('', '.manifest.json', '.sig')) {
            Copy-Item -LiteralPath (Join-Path $content ('Remotely-Win-' + $platform + '.zip' + $suffix)) -Destination $setup
        }
        [System.IO.Compression.ZipFile]::CreateFromDirectory($setup, (Join-Path $content ('Remotely-Setup-' + $rid + '.zip')))
    }
    & (Join-Path $PSScriptRoot 'Sign-UpdateArtifacts.ps1') -ArtifactDirectory $content -PrivateKeyPath $PrivateKeyPath -ReleaseSequence $ReleaseSequence
    dotnet publish Server/Server.csproj -c Release -r linux-x64 --self-contained false -p:UseAppHost=false -o (Join-Path $output 'Server')
    if ($LASTEXITCODE) { throw 'Server publication failed.' }
    Copy-Item -LiteralPath $content -Destination (Join-Path $output 'Server/wwwroot') -Recurse -Force
    $rsa = [System.Security.Cryptography.RSA]::Create()
    try {
        $rsa.ImportFromPem([System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $PrivateKeyPath)))
        [System.IO.File]::WriteAllText((Join-Path $output 'update-public-key.pem'), $rsa.ExportSubjectPublicKeyInfoPem())
    } finally { $rsa.Dispose() }
    Write-Output ('Signed release prepared in ' + $output)
} finally { Pop-Location }
