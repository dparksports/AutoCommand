# AutoCommand v2026.10.15

Self-contained Windows build — **no .NET installation required**. Unzip and run `AutoCommand.exe` as Administrator.

## Highlights

- **✏️ Rename legacy blocks (Advanced Firewall Settings tab)** — rules still carrying the old `AutoCommand IP Block - <ip>` naming now have a one-click fix in place: the new button appears beside Re-baseline/Pause auto-fix whenever legacy-named rules are detected (with a live count in its tooltip), recreates each block under the current `AC-BLOCK-IP <ip>` scheme, and re-captures the baseline afterwards so the Security Enforcer doesn't flag the renamed rules as drift. Migration is lossless — descriptions (provenance) carry over, redundant inbound duplicates are removed, protection unchanged. Covers legacy app blocks (`AutoCommand Process Block - …`) too.
- **Connections tab blocks under the current naming** — "Block remote IP" and "Block all remote IPs" on the Process Monitor now create the v2 2-rule blocks (outbound TCP + UDP, `AC-BLOCK-IP` naming) with full provenance (process, PID, UTC time) in the description, instead of the legacy 4-rule scheme — no more legacy names being minted. Confirm dialogs now match the actual behavior and include the same Microsoft-infrastructure warning as the Svchost monitor.

## Fixes

- New legacy-name block rules are no longer created by the Connections tab (they were re-appearing in Advanced Firewall Settings after every block from there). Unblock flows are unaffected — they already match both naming schemes.
- `AddBlockRuleForIpAsync` is documented as the legacy path so new code lands on `AddIpBlockAsync`.

## Assets in this release

- **`AutoCommand-v2026.10.15-win-x64.zip`** — the app with the Svchost Trace capture engine bundled at `tools\SvchostAnalyzer.exe`. Unzip, run `AutoCommand.exe` as Administrator.
- **`SvchostAnalyzer-win-x64.zip` + `SHA256SUMS.txt`** — the capture engine standalone, for machines where the app was installed before this release (SHA-256 verified against `SHA256SUMS.txt` before anything runs, installed to `%LOCALAPPDATA%\AutoCommand\tools`).
