[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Run this script on the Windows computer that hosts the local Paws N Claws app.'
}

$secret = Read-Host 'TextBee API key' -AsSecureString
try {
    if ($secret.Length -eq 0) { throw 'Enter an API key.' }
    $directory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PawsNClaws\Secrets'
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $target = Join-Path $directory 'textbee-key.dpapi'
    $temporary = Join-Path $directory ("textbee-" + [Guid]::NewGuid().ToString('N') + ".tmp")
    try {
        ConvertFrom-SecureString -SecureString $secret | Set-Content -LiteralPath $temporary -Encoding ascii
        if (Test-Path -LiteralPath $target) {
            [System.IO.File]::Replace($temporary, $target, [NullString]::Value)
        } else {
            [System.IO.File]::Move($temporary, $target)
        }
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
    }
    Write-Output 'Encrypted TextBee credential saved for the current Windows account.'
} finally {
    $secret.Dispose()
}
