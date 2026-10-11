# AutoCommand v2026.10.16

Self-contained Windows build — **no .NET installation required**. Unzip and run `AutoCommand.exe` as Administrator.

## Highlights

- **🔢 Correct version everywhere** — the release version now flows from a single source (`<Version>` in the project file) into the binary at build time. The v2026.10.14 and v2026.10.15 builds displayed "2026.10.13" in the status bar and Settings tab despite being newer; that class of drift is now structurally impossible — the release pipeline verifies the built exe reports the project version before anything ships.
- **🔧 Svchost Trace engine auto-install fixed** — the auto-install button built its download URL from a version source that lagged behind the actual release tag (a 404 on every release since 10.13); it now derives the tag from the same version the binary reports.
- **📦 One-command releases** — a single `release.ps1` script now cuts releases end-to-end (bump, build, verify, package, checksums, commit, tag, push, GitHub release), so the tag, zip names and checksums can never disagree with the binary again.

## Fixes

- Status bar / Settings version display showed 2026.10.13 on the 2026.10.14 and 2026.10.15 builds — hand-maintained assembly attributes had lagged the project version; the attributes are now generated at build time.
- Svchost Trace "install capture engine" failed with a download error on any build whose internal version disagreed with its release tag.
- `SHA256SUMS.txt` now covers the app zip as well as the capture engine (groundwork for in-app auto-update).

## Assets in this release

- **`AutoCommand-v2026.10.16-win-x64.zip`** — the app with the Svchost Trace capture engine bundled at `tools\SvchostAnalyzer.exe`. Unzip, run `AutoCommand.exe` as Administrator.
- **`SvchostAnalyzer-win-x64.zip` + `SHA256SUMS.txt`** — the capture engine standalone (SHA-256 verified against `SHA256SUMS.txt` before anything runs), plus checksums for both zips.
