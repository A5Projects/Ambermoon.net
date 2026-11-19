<#
.SYNOPSIS
Build the Immersive installer and its SHA-256 sidecar from a release ZIP.
.EXAMPLE
./scripts/Package-WindowsInstaller.ps1 -Archive ./release/Ambermoon-ImmersiveEdition-1.2.0-Windows-x64.zip -Compiler 'C:\Tools\Inno Setup 7\ISCC.exe'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Archive,
    [string]$Compiler = 'ISCC.exe'
)
$ErrorActionPreference = 'Stop'
$zip = Get-Item -LiteralPath $Archive
if ($zip.Name -notmatch '^Ambermoon-ImmersiveEdition-(\d+\.\d+\.\d+)-Windows-x64\.zip$') {
    throw "Expected an Immersive Edition Windows x64 release ZIP."
}
$version = $Matches[1]
$zipHash = (Get-FileHash -LiteralPath $zip.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$checksum = (Get-Content -LiteralPath ($zip.FullName + '.sha256') -Raw).Trim()
if ($checksum -ne "$zipHash  $($zip.Name)") { throw 'Release ZIP checksum mismatch.' }
$compilerPath = (Get-Command $Compiler -CommandType Application -ErrorAction Stop).Source
$stagingName = 'inno-build-' + [guid]::NewGuid().ToString('N')
$staging = New-Item -Path (Join-Path $zip.DirectoryName $stagingName) -ItemType Directory
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zip.FullName, $staging.FullName)
    $payload = Join-Path $staging.FullName $zip.BaseName
    $hostDll = Get-Item -LiteralPath (Join-Path $payload 'Ambermoon.net.dll')
    if ($hostDll.VersionInfo.ProductVersion.Split('+')[0] -ne $version) {
        throw 'The packaged game version differs from the ZIP filename.'
    }
    & $compilerPath "/DPayloadDir=$payload" "/DReleaseVersion=$version" "/O$($zip.DirectoryName)" (Join-Path $PSScriptRoot 'ImmersiveEdition.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed ($LASTEXITCODE)." }
    $setup = Get-Item -LiteralPath (Join-Path $zip.DirectoryName ($zip.BaseName + '-Setup.exe'))
    $setupHash = (Get-FileHash -LiteralPath $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($setup.FullName + '.sha256', "$setupHash  $($setup.Name)`n", [Text.UTF8Encoding]::new($false))
    Write-Output "Created $($setup.FullName) (SHA-256 $setupHash)"
}
finally {
    $path = (Resolve-Path -LiteralPath $staging.FullName).ProviderPath
    if ([IO.Path]::GetDirectoryName($path) -eq $zip.DirectoryName -and [IO.Path]::GetFileName($path) -eq $stagingName) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}
