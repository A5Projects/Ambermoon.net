<#
.SYNOPSIS
Build a clean, self-contained Windows x64 ZIP for either fork branch.
.EXAMPLE
./scripts/Package-WindowsRelease.ps1 -Edition immersive -Tag immersive-v1.0.2 -OutputDirectory ../release-immersive
.NOTES
Run from the branch being released. OutputDirectory must not already exist.
The script does not change installed games, upload assets, or create Git tags.
Run any game tests in a separate extracted copy, not in the release payload.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][ValidateSet('immersive', 'expanded')][string]$Edition,
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')][string]$Tag,
    [string]$DotnetPath = 'dotnet',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'This packaging script must run on Windows.'
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = [IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory))
if (Test-Path -LiteralPath $outputRoot) {
    throw "OutputDirectory already exists. Choose a fresh directory: $outputRoot"
}

$sourceCommit = & git -c "safe.directory=$repoRoot" -C $repoRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'Cannot determine the source Git commit.'
}
$sourceCommit = $sourceCommit.Trim()
$editionName = if ($Edition -eq 'immersive') { 'Immersive UI' } else { 'Expanded UI' }
if ($Tag -notmatch "^$Edition-v([0-9]+\.[0-9]+\.[0-9]+(?:[-.][A-Za-z0-9.-]+)?)$") {
    throw "Tag must use the form $Edition-v1.0.0 (an optional prerelease suffix is allowed)."
}
$releaseVersion = $Matches[1]
$artifactEdition = if ($Edition -eq 'immersive') { 'ImmersiveUI' } else { 'Expanded' }
$folderName = "Ambermoon.net-$artifactEdition-$releaseVersion-Windows-x64"
$payload = Join-Path $outputRoot $folderName
$payloadPrefix = $payload.TrimEnd('\') + '\'
$project = Join-Path $repoRoot 'Ambermoon.net\Ambermoon.net.csproj'

$branchDocs = @('Controls.md', 'Configuration.md', 'CustomMusic.md', 'CARRYWEIGHT.txt',
    'docs\action_buttons_0.png', 'docs\action_buttons_1.png', 'docs\amount_popup.png')
if ($Edition -eq 'immersive') {
    $branchDocs += @('ImmersiveUI.md', 'IMMERSIVE-BALANCE.txt', 'docs\3d-movement-timing.md', 'docs\inventory-input-lock.md',
        'tools\CameraTimingProbe\README.md', 'tools\CameraTimingProbe\CameraTimingProbe.csproj',
        'tools\CameraTimingProbe\Program.cs')
} else {
    $branchDocs += @('MOUSELOOK.txt', 'EXPLORATION-CHEATS.txt')
}
foreach ($relative in (@('LICENSE', 'Ambermoon.net\versions.dat', 'Ambermoon.net\diffs.dat') + $branchDocs)) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $relative) -PathType Leaf)) {
        throw "Missing release input '$relative'. Confirm that Edition matches the checked-out branch."
    }
}

New-Item -ItemType Directory -Path $payload -Force | Out-Null
$publishArguments = @(
    'publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-o', $payload, '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false',
    '-p:PublishSingleFile=false', "-p:SourceRevisionId=$sourceCommit"
)
if ($NoRestore) { $publishArguments += '--no-restore' }
& $DotnetPath @publishArguments
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

# The upstream updater installs Pyrdacor's builds, not this fork. Its DLL stays
# because it is a project dependency, but GameWindow requires the EXE to offer it.
$removeFiles = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Where-Object {
    $_.Extension -eq '.pdb' -or $_.Name -eq 'AmbermoonPatcher.exe'
})
foreach ($file in $removeFiles) {
    $resolved = (Resolve-Path -LiteralPath $file.FullName).ProviderPath
    if (-not $resolved.StartsWith($payloadPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a file outside the release payload: $resolved"
    }
    Remove-Item -LiteralPath $resolved
}

$documentation = Join-Path $payload 'Documentation'
$licenses = Join-Path $documentation 'Licenses'
New-Item -ItemType Directory -Path $documentation, $licenses | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $payload 'LICENSE')
foreach ($relative in $branchDocs) {
    $documentPath = Join-Path $documentation $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $documentPath) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot $relative) -Destination $documentPath
}
if ($Edition -eq 'immersive') {
    # Code links in the investigation refer to the matching tagged source,
    # while its portable probe and other documentation stay available offline.
    $timingDocument = Join-Path $documentation 'docs\3d-movement-timing.md'
    $timingText = [IO.File]::ReadAllText($timingDocument)
    $timingText = $timingText.Replace('](../Ambermoon.', "](https://github.com/A5Projects/Ambermoon.net/blob/$sourceCommit/Ambermoon.")
    [IO.File]::WriteAllText($timingDocument, $timingText, [Text.UTF8Encoding]::new($false))
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'Package') -Destination (Join-Path $documentation 'Upstream') -Recurse

# project.assets.json records the actual package folders used by restore, even
# when a developer has redirected NuGet away from its usual per-user cache.
$assets = Get-Content -LiteralPath (Join-Path $repoRoot 'Ambermoon.net\obj\project.assets.json') -Raw | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
if ($env:NUGET_PACKAGES) { $packageRoots += $env:NUGET_PACKAGES }
$packageRoots += Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages'
$packageRoots = @($packageRoots | Select-Object -Unique)

function Find-PackageFile([string]$Package, [string]$Version, [string]$RelativePath) {
    foreach ($root in $packageRoots) {
        $candidate = Join-Path (Join-Path (Join-Path $root $Package.ToLowerInvariant()) $Version) $RelativePath
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw "Missing notice file for $Package/$Version ($RelativePath). Restore its NuGet package before packaging."
}

$runtimeConfig = Get-Content -LiteralPath (Join-Path $payload 'Ambermoon.net.runtimeconfig.json') -Raw | ConvertFrom-Json
$framework = @($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' })
if ($framework.Count -ne 1) { throw 'Cannot identify the bundled .NET runtime version.' }
$runtimeVersion = $framework[0].version
Copy-Item -LiteralPath (Find-PackageFile 'Microsoft.NETCore.App.Runtime.win-x64' $runtimeVersion 'LICENSE.TXT') -Destination (Join-Path $licenses 'dotnet-LICENSE.TXT')
Copy-Item -LiteralPath (Find-PackageFile 'Microsoft.NETCore.App.Runtime.win-x64' $runtimeVersion 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $licenses 'dotnet-THIRD-PARTY-NOTICES.TXT')

$dependencies = Get-Content -LiteralPath (Join-Path $payload 'Ambermoon.net.deps.json') -Raw | ConvertFrom-Json
$glfw = @($dependencies.libraries.PSObject.Properties.Name | Where-Object { $_ -match '^glfw/' })
if ($glfw.Count -ne 1) { throw 'Cannot identify the bundled GLFW package version.' }
$glfwVersion = $glfw[0].Split('/')[1]
Copy-Item -LiteralPath (Find-PackageFile 'glfw' $glfwVersion 'docs\LICENSE.md') -Destination (Join-Path $licenses 'GLFW-LICENSE.md')

$sourceUrl = "https://github.com/A5Projects/Ambermoon.net/tree/$sourceCommit"
$releaseUrl = 'https://github.com/A5Projects/Ambermoon.net/releases/tag/' + [Uri]::EscapeDataString($Tag)
$editionHelp = if ($Edition -eq 'immersive') {
@'
Immersive UI fills the area below the portraits during 2D and 3D exploration.
Esc/Enter/Tab or MENU opens the compact action overlay; Esc closes other windows
first. See Documentation/ImmersiveUI.md and Controls.md for the complete controls.
The Original selection keeps original balance, including misses and weapon breaks.
Advanced uses relaxed balance, including a normal clock about 30% slower while
travelling or standing still. Original's clock and explicit waits are unchanged.
See Documentation/IMMERSIVE-BALANCE.txt for the full rules.
F7 quicksaves during exploration to QuickSave F7 without replacing other saves.
Reload through the normal load menu; no quick-load key is added. In combat F7
keeps its battle-speed function. See Documentation/Controls.md.
'@
} else {
@'
Expanded UI enlarges the 3D view and keeps the classic sidebar and standard
balance. See Documentation/MOUSELOOK.txt and Controls.md for its controls.
Documentation/EXPLORATION-CHEATS.txt describes the optional session cheats.
'@
}
$movementHelp = if ($Edition -eq 'immersive') {
    'Keyboard movement defaults to 60% walking speed. Caps Lock toggles 100% sprint; holding Shift temporarily sprints.'
} else {
    'Caps Lock switches keyboard movement to/from 60% speed.'
}
$quickStart = @"
Ambermoon.net - $editionName
Release: $Tag
Source commit: $sourceCommit
Source: $sourceUrl
Release and downloads: $releaseUrl

This is the A5Projects fork of Ambermoon.net 1.15.1 by Pyrdacor and contributors.
The included LICENSE is the GNU GPL v3. Original documentation and credits are
preserved under Documentation/Upstream. Runtime notices are under
Documentation/Licenses.

STARTING
Extract the entire ZIP into a new writable folder, then run Ambermoon.net.exe.
Do not run the executable inside the ZIP or copy it without its companion files.
This is the Windows x64 build; the .NET runtime files are included, so no separate
.NET installation is needed. The game normally starts windowed. F11 changes the window mode.
The startup selector offers Original 1.22 and Ambermoon Advanced 1.33.

$editionHelp

In either edition, C or a mouse-wheel click switches between arrows and horizontal
mouse-look during 3D exploration. Settings page 4 also has a clickable mouse-mode
option. QWEASD movement gives W/S forward/back, A/D strafe, and Q/E turning.
Space interacts ahead. $movementHelp
Optional cheats start disabled. See Documentation/CARRYWEIGHT.txt for the temporary
carryweight command and save transfer details.

UPDATES AND SAVES
This package omits the upstream updater executable so it cannot replace the fork
with an upstream build. There is no fork auto-updater; download updates manually
from https://github.com/A5Projects/Ambermoon.net/releases.
Extract updates into a fresh folder rather than mixing different builds.
To import saves, back up and copy your Saves folder and select the same game-data
edition and language. Do not transfer Original saves into Advanced or vice versa.
Our two UI editions share a save format; balance and carried-weight limits differ.
User settings and saves are not included in this download.
"@
[IO.File]::WriteAllText((Join-Path $payload 'START-HERE.txt'), $quickStart, [Text.UTF8Encoding]::new($false))

$requiredFiles = @('Ambermoon.net.exe', 'Ambermoon.net.dll', 'Ambermoon.net.deps.json',
    'Ambermoon.net.runtimeconfig.json', 'versions.dat', 'diffs.dat', 'coreclr.dll',
    'hostfxr.dll', 'hostpolicy.dll', 'glfw3.dll', 'soft_oal.dll', 'x64\api-ms-win-core-winrt-l1-1-0.dll')
foreach ($relative in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $relative) -PathType Leaf)) {
        throw "Release payload is incomplete: $relative"
    }
}
foreach ($dataFile in @('versions.dat', 'diffs.dat')) {
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $repoRoot "Ambermoon.net\$dataFile") -Algorithm SHA256).Hash
    $payloadHash = (Get-FileHash -LiteralPath (Join-Path $payload $dataFile) -Algorithm SHA256).Hash
    if ($sourceHash -ne $payloadHash) { throw "Published $dataFile differs from the source game data." }
}
foreach ($entry in (Get-ChildItem -LiteralPath $payload -Recurse -Force)) {
    $relative = $entry.FullName.Substring($payloadPrefix.Length).Replace('\', '/')
    if ($relative -match '(^|/)(Saves[^/]*|Verification|Screenshots|\.git)(/|$)' -or
        $entry.Name -match '(\.cfg$|\.pdb$|^error\.txt$|^AmbermoonPatcher\.exe$|^Ambermoon\..*Verify|^Ambermoon\.OptionsPreview)') {
        throw "Personal, test, or updater artifact found in release payload: $relative"
    }
}

$zipPath = Join-Path $outputRoot ($folderName + '.zip')
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($payload, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true)
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = $zipPath + '.sha256'
[IO.File]::WriteAllText($checksumPath, "$zipHash  $([IO.Path]::GetFileName($zipPath))`n", [Text.UTF8Encoding]::new($false))
Write-Host "Created $zipPath"
Write-Host "SHA256: $zipHash"
Write-Host "Test a separate extracted copy before uploading; keep this payload pristine."
