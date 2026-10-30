[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory)][string]$PrivateKeyPath,
    [Parameter(Mandatory)][ValidateRange(1, [long]::MaxValue)][long]$ReleaseSequence,
    [DateTimeOffset]$ExpiresUtc = [DateTimeOffset]::UtcNow.AddDays(14)
)
$ErrorActionPreference = 'Stop'
$rsa = [System.Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem([System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $PrivateKeyPath)))
    if ($rsa.KeySize -lt 3072) { throw 'A minimum 3072-bit RSA signing key is required.' }
    $directory = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
    $artifacts = Get-ChildItem -LiteralPath $directory -File | Where-Object {
        $_.Name -match '^Remotely-(Win-x64|Win-x86|Linux|MacOS-x64|MacOS-arm64|Setup-win-x64|Setup-win-x86)\.zip$' -or
        $_.Name -match '^Install-(Remotely\.ps1|(Ubuntu|Manjaro|MacOS)-(x64|arm64)\.sh)$'
    }
    if (!$artifacts) { throw 'No release artifacts were found.' }
    foreach ($artifact in $artifacts) {
        $manifest = [ordered]@{
            ReleaseSequence = $ReleaseSequence
            FileName = $artifact.Name
            Sha256 = (Get-FileHash -LiteralPath $artifact.FullName -Algorithm SHA256).Hash
            Length = $artifact.Length
            ExpiresUtc = $ExpiresUtc.ToUniversalTime().ToString('O')
        }
        $payload = [System.Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Compress))
        $signature = $rsa.SignData($payload, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pss)
        [System.IO.File]::WriteAllBytes($artifact.FullName + '.manifest.json', $payload)
        [System.IO.File]::WriteAllBytes($artifact.FullName + '.sig', $signature)
        Write-Output ('Signed: ' + $artifact.Name)
    }
} finally { $rsa.Dispose() }
