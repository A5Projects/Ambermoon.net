# Packaging fork releases

The fork releases are portable, self-contained Windows x64 ZIPs. **Ambermoon Immersive Edition v1.2.0** is the current release, previously named Immersive UI. Its upstream base is Ambermoon.net 1.15.1 with selected fixes from 1.15.2 and later commits; this is not a full upstream update. **Expanded v1.0.1** retains its separate version and standard balance. See the [published releases](https://github.com/A5Projects/Ambermoon.net/releases) for downloads and checksums.

Use the `main` branch for Immersive Edition and `expanded` for Expanded. Commit the intended source and documentation before packaging so the binary and `START-HERE.txt` identify the correct source commit. Keep the desktop project Version and `EditionInfo.Version` aligned; the immersive packaging command rejects a mismatching tag version.

## Build

Install a .NET 9 SDK and Git, then run PowerShell from the chosen branch. The output directory must be new and should be outside the checkout.

```powershell
# On main:
./scripts/Package-WindowsRelease.ps1 -Edition immersive -Tag immersive-v1.2.0 -OutputDirectory ../release-immersive

# Or, on expanded:
./scripts/Package-WindowsRelease.ps1 -Edition expanded -Tag expanded-v1.0.1 -OutputDirectory ../release-expanded
```

Use `-DotnetPath` for a private SDK and `-NoRestore` only when this checkout has already restored its Windows x64 dependencies. Normal packaging restores dependencies. The bundled `Ambermoon.net/versions.dat` and `diffs.dat` must be present; they supply the startup game-data choices and save migration patches.

The script publishes the game and runtime, preserves upstream documentation and licenses, adds branch-specific controls and a quick-start, and writes a ZIP plus a SHA-256 checksum. It excludes personal settings, saves, test files, debug symbols, and the upstream updater launcher. The updater DLL remains a required assembly dependency. Immersive Edition also disables upstream patching in code so local builds with a leftover launcher cannot compare its independent version number to upstream or replace the fork.

## Verify and publish

Extract the ZIP into a separate test folder. Never test in the pristine payload directory: the game and verification tools can create settings, saves, and screenshots.

Check that a normal launch opens windowed and offers Original and Advanced; start a new game in each. Check the branch's exploration layout and mouse-mode controls. For interface or gameplay changes, also run the relevant regression checks before release. Packaging-only changes need package/startup checks.

When publication is authorized, create a Git tag on the exact source commit used for packaging, and publish a release with that tag, the matching ZIP, and its `.sha256` file. Keep edition tags separate so Expanded retains its own standard-balance source history. Use the release title **Ambermoon Immersive Edition v1.2.0** and mark it as the repository's latest release. Update the README's published links at that time. Include the upstream base separately, controls, balance distinction, save-transfer guidance and [upstream backport credits](docs/immersive-edition-1.1.0.md). The new immersive artifact name is `Ambermoon-ImmersiveEdition-1.2.0-Windows-x64.zip`; existing releases keep their names and URLs.

Downloads must not include a player's configuration or saves. Install future releases in a fresh folder and copy backed-up saves using the same Original/Advanced edition and language. No fork automatic updater is provided.
