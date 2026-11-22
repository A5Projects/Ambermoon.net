# Publishing Ambermoon Experimental

Experimental has its own Windows x64 prereleases. The first is **Ambermoon Experimental v1.2.0-preview.1**, tagged `experimental-v1.2.0-preview.1` on `Experimental`. Immersive Edition and Expanded have their own release instructions. [Immersive Edition](https://github.com/A5Projects/Ambermoon.net/blob/main/RELEASING.md) · [Expanded](https://github.com/A5Projects/Ambermoon.net/blob/expanded/RELEASING.md).

## Build the ZIP and installer

On Windows, use Git, a .NET 9 SDK and Inno Setup 7. Commit the intended source and documentation on `Experimental` before packaging. The ZIP records the source commit in `START-HERE.txt`. The desktop project and `EditionInfo` should identify the same preview. From the checkout, choose a new output directory outside the source tree and run these commands.

```powershell
./scripts/Package-WindowsRelease.ps1 -Edition experimental -Tag experimental-v1.2.0-preview.1 -OutputDirectory ../release-experimental
./scripts/Package-WindowsInstaller.ps1 -Archive ../release-experimental/Ambermoon-Experimental-1.2.0-preview.1-Windows-x64.zip
```

Use `-DotnetPath` on the ZIP command for a private SDK and `-Compiler` on the installer command for a specific `ISCC.exe`. Keep the `.zip.sha256` beside the ZIP. The installer script verifies that checksum before packaging the same files into `-Setup.exe` and writing its `.exe.sha256`.

## Verify the preview

Extract the ZIP into a separate test folder. Check the build identity in `START-HERE.txt`, the Original and Advanced selector, and the renderer guide in `Documentation/docs/experimental-renderer.md`. In a 3D area, switch between Classic and Experimental on Options page 5, cycle the three texture modes, and try the frame limit on page 4. Record which configurations were exercised when describing results.

Test the installer in a folder dedicated to Experimental. It has its own installation identity and optional Start menu shortcut so it can sit alongside stable Immersive Edition. An Experimental upgrade should offer its previous path and shortcut choice. Verify that upgrading and uninstalling preserve copies of saves, settings, and screenshots. Distribute clean packages containing the game, runtime, and documentation.

## Publish a GitHub prerelease

Push `Experimental` and the `experimental-v1.2.0-preview.1` tag at the exact source commit used for packaging. Create a GitHub release titled **Ambermoon Experimental v1.2.0-preview.1** using that tag. Select **Set as a pre-release** and turn off **Set as the latest release**. With the GitHub CLI these settings are `--prerelease --latest=false`. This keeps stable Immersive Edition available from the repository’s Latest link.

Attach the Windows x64 ZIP, the installer, and their two SHA-256 files. Include how to compare the renderers, the extent of playtesting, and the stable Immersive Edition link in the release notes. Check the published asset digests against the local files and verify the tag and release flags. [Experimental preview](https://github.com/A5Projects/Ambermoon.net/releases/tag/experimental-v1.2.0-preview.1) · [Stable Immersive Edition](https://github.com/A5Projects/Ambermoon.net/releases/tag/immersive-v1.2.0).
