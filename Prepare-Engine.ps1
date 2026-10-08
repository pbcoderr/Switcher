param([string]$ArchivePath)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$temporaryArchive = $null
try {
    if (-not $ArchivePath) {
        $temporaryArchive = Join-Path ([IO.Path]::GetTempPath()) ('Switcher-engine-' + [guid]::NewGuid().ToString('N') + '.zip')
        Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/SagerNet/sing-box/releases/download/v1.14.2/sing-box-1.14.2-windows-amd64.zip' -OutFile $temporaryArchive
        $ArchivePath = $temporaryArchive
    }
    if ((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash -ne 'C2D8BFFF918755808781DFDEEB8581B6C91EB3A243D9A7B55483CFC0C0684D32') { throw 'Incorrect sing-box archive checksum.' }
    $engineDirectory = Join-Path $PSScriptRoot 'Engine'
    if ((Test-Path -LiteralPath $engineDirectory) -and ((Get-Item -LiteralPath $engineDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Engine directory must not be a link.' }
    New-Item -ItemType Directory -Path $engineDirectory -Force | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ArchivePath).Path)
    try {
        foreach ($name in @('sing-box.exe', 'libcronet.dll', 'LICENSE')) {
            $entry = $archive.GetEntry('sing-box-1.14.2-windows-amd64/' + $name)
            if (-not $entry) { throw ('Archive entry missing: ' + $name) }
            $destination = Join-Path $engineDirectory $name
            if ((Test-Path -LiteralPath $destination) -and ((Get-Item -LiteralPath $destination).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unexpected linked engine file.' }
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)
        }
    } finally { $archive.Dispose() }
    Copy-Item -LiteralPath $ArchivePath -Destination (Join-Path $engineDirectory 'sing-box-1.14.2-windows-amd64.zip') -Force
    Write-Output 'sing-box 1.14.2 for Windows x64 is ready to embed. Build Switcher now.'
} finally {
    if ($temporaryArchive -and (Test-Path -LiteralPath $temporaryArchive)) { Remove-Item -LiteralPath $temporaryArchive -Force }
}
