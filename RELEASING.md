# Packaging fork releases

The fork releases are portable, self-contained Windows x64 ZIPs. The fork version is separate from the upstream engine version: `immersive-v1.0.1` and `expanded-v1.0.0` both use Ambermoon.net 1.15.1.

Use the `main` branch for Immersive UI and `expanded` for Expanded. Commit the intended source and documentation before packaging so the binary and `START-HERE.txt` identify the correct source commit.

## Build

Install a .NET 9 SDK and Git, then run PowerShell from the chosen branch. The output directory must be new and should be outside the checkout.

```powershell
# On main:
./scripts/Package-WindowsRelease.ps1 -Edition immersive -Tag immersive-v1.0.1 -OutputDirectory ../release-immersive

# Or, on expanded:
./scripts/Package-WindowsRelease.ps1 -Edition expanded -Tag expanded-v1.0.0 -OutputDirectory ../release-expanded
```

Use `-DotnetPath` for a private SDK and `-NoRestore` only when this checkout has already restored its Windows x64 dependencies. Normal packaging restores dependencies. The bundled `Ambermoon.net/versions.dat` and `diffs.dat` must be present; they supply the startup game-data choices and save migration patches.

The script publishes the game and runtime, preserves upstream documentation and licenses, adds branch-specific controls and a quick-start, and writes a ZIP plus a SHA-256 checksum. It excludes personal settings, saves, test files, debug symbols, and the upstream updater launcher. The updater DLL remains a required assembly dependency. Omitting the launcher prevents upstream updates from replacing this fork.

## Verify and publish

Extract the ZIP into a separate test folder. Never test in the pristine payload directory: the game and verification tools can create settings, saves, and screenshots.

Check that a normal launch opens windowed and offers Original and Advanced; start a new game in each. Check the branch's exploration layout and mouse-mode controls. For interface or gameplay changes, also run the relevant regression checks before release. Packaging-only changes need package/startup checks.

Create a Git tag on the exact source commit used for packaging, and publish a release with that tag, the matching ZIP, and its `.sha256` file. Keep edition tags separate so Expanded retains its own standard-balance source history. Mark the Immersive UI release as the repository's latest release; the README links directly to both editions. Include the engine/data versions, controls, balance distinction, and save-transfer guidance in the release notes.

Downloads must not include a player's configuration or saves. Install future releases in a fresh folder and copy backed-up saves using the same Original/Advanced edition and language. No fork automatic updater is provided.
