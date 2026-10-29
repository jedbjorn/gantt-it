# Releasing Gantt-It

A release is a git tag. Pushing a tag `v<major>.<minor>.<patch>` (optionally
with a pre-release suffix, `v1.0.0-rc.1`) builds that commit and publishes a
GitHub Release with the installer attached. Nothing is bumped by hand: no
version lives in a file that has to be edited before a release.

## Cut a release

1. Make sure `main` is green and holds what should ship.
2. Tag the `main` commit and push the tag:

   ```sh
   git fetch origin
   git tag -a v1.0.0 origin/main -m "Gantt-It 1.0.0"
   git push origin v1.0.0
   ```

3. Watch **Actions › release**. When it finishes, the release is at
   `https://github.com/jedbjorn/gantt-it/releases/tag/v1.0.0`.

A tag with a hyphen (`v1.0.0-rc.1`, `v1.1.0-beta.2`) is published as a
**pre-release**; it does not become "latest". Tags that are not
`v<a>.<b>.<c>[-<suffix>]` fail the workflow before anything is built.

## What the workflow does

`.github/workflows/release.yml`, on a pushed `v*` tag:

1. **Resolve version.** `v1.0.0-rc.1` → version `1.0.0-rc.1`. The MSI's
   ProductVersion is the numeric part, `1.0.0` (Windows Installer only accepts
   numbers; major and minor at most 255, patch at most 65535).
2. **Build.** Runs the same reusable pipeline as every PR (`_build.yml`)
   against the tagged commit, with `-p:GanttItVersion=<version>`:
   - Core tests (net8.0 and net10.0) and the four configuration builds.
   - On Windows: `Release R26` and `Release R27`, staged with
     `installer/stage.sh`, packed into one per-user MSI, and checked by
     `installer/inspect-msi.ps1` (UpgradeCode, per-user scope, ProductVersion,
     both Revit years' files).
3. **Release.** Creates the GitHub Release for the tag with auto-generated
   notes and these assets:
   - `GanttIt-<version>.msi` — the installer (Revit 2026 + 2027).
   - `GanttIt-<version>-Revit2026.zip`, `GanttIt-<version>-Revit2027.zip` —
     the contents of `Addins\<year>\` for a manual install.
   - `SHA256SUMS.txt`.

If any job fails, no release is created. Fix the problem on `main`, delete the
tag (`git push origin :refs/tags/v1.0.0` and `git tag -d v1.0.0`), and tag
again; or tag the next version instead.

## Where the version goes

`GanttItVersion` is the one MSBuild property that carries the tag
(`Directory.Build.props`). From it:

| Consumer | Release build `v1.0.0-rc.1` | Push/PR or local build |
|---|---|---|
| Assembly `InformationalVersion` | `1.0.0-rc.1` | `0.0.0-dev+<commit>` |
| `AppVersion` stored with every chart | `1.0.0-rc.1` | `0.0.0-dev+<commit>` |
| `AssemblyVersion` / `FileVersion` | `1.0.0.0` | `0.0.0.0` |
| MSI ProductVersion | `1.0.0` | `0.0.0` |
| MSI file name | `GanttIt-1.0.0-rc.1.msi` | `GanttIt-0.0.0-dev.msi` |

The MSI's UpgradeCode (`644FCAD5-E631-4441-9A11-75F175565136`, in
`installer/Product.wxs`) is fixed forever: it is how Windows recognises a new
MSI as an upgrade of the installed one. A newer MSI replaces the installed
version in place (one entry in Settings › Apps); an older one refuses to
install over a newer one. An MSI with the *same* ProductVersion also replaces
it, so `1.0.0` installs cleanly over `1.0.0-rc.1`.

## Rehearse without publishing

**Actions › release › Run workflow** asks for a version (for example
`1.0.0-rc.1`) and runs the resolve and build steps exactly as a tag would, then
stops: the last job prints the release it *would* create and publishes
nothing. Its artifacts (`GanttIt-msi`, `GanttIt-zips`) carry the chosen
version, which also makes it the way to get test MSIs with distinct versions:

```sh
gh workflow run release.yml -f version=1.0.0-rc.1
gh run download <run-id> -n GanttIt-msi
```

Every PR and push to `main` also uploads a `GanttIt-msi` artifact, with the
dev version `0.0.0-dev`.

## Verify a release

1. The release page lists the MSI, both zips and `SHA256SUMS.txt`, and is
   marked pre-release exactly when the tag has a suffix.
2. The **Inspect MSI** step of the release run's MSI job printed
   `ProductVersion` equal to the tag's numeric part and both
   `Addins\2026\` and `Addins\2027\` file lists.
3. On a Windows machine with Revit: install the MSI (no admin prompt), confirm
   Settings › Apps shows **Gantt-It** with the new version, start Revit 2026 or
   2027, choose **Always Load**, and open the Gantt-It panel. Generate or
   reopen a chart; its stored `AppVersion` is the release version.

## Build the MSI locally (Windows)

Needs the .NET 8 and .NET 10 SDKs; WiX comes from NuGet. From the repo root,
in Git Bash:

```sh
dotnet build src/GanttIt.Revit/GanttIt.Revit.csproj -c "Release R26" -p:GanttItVersion=1.0.0
dotnet build src/GanttIt.Revit/GanttIt.Revit.csproj -c "Release R27" -p:GanttItVersion=1.0.0
bash installer/stage.sh 2026
bash installer/stage.sh 2027
dotnet build installer/GanttIt.Installer.wixproj -c Release -p:GanttItVersion=1.0.0
powershell -File installer/inspect-msi.ps1 -Msi installer/bin/Release/GanttIt-1.0.0.msi
```

Omit `-p:GanttItVersion` for a dev build (`GanttIt-0.0.0-dev.msi`). The setup
artwork in `installer/branding/` is generated by
`tools/make_installer_branding.py`.

The add-in and the MSI are unsigned in v1 (decision #4): Revit shows its
unsigned add-in prompt and SmartScreen may warn on the MSI.
