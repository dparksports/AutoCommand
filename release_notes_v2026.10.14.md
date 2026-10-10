# AutoCommand v2026.10.14

Self-contained Windows build — **no .NET installation required**. Unzip and run `AutoCommand.exe` as Administrator.

## Highlights

- **🔬 New tab: Svchost Trace** — a Procmon-style timeline over the 70+ `svchost.exe` service hosts: spawn events with parent process and hosted service, TCP connect/close lifetimes, byte-counter samples, and a pattern detector that flags non-`services.exe` parents, respawn loops, beacon-regular connect intervals (CV < 0.35), upload-dominant flows, and destinations classified against known Microsoft/CDN ranges. Export to JSONL and re-analyze offline with the bundled `svchost-watch/SvchostAnalyzer` (.NET console tool, capture + analyze, exit codes for scheduling).
- **Headless capture service** — Start/Stop from the Svchost Trace tab runs the collector detached (survives app close); **Auto-start on boot** persists via your user Run key (no UAC); a global mutex guarantees a single capture; `--max-size-mb` rotation bounds the disk.
- **🛠 Blocked Rules manager** — every AutoCommand block rule in one fleet view, grouped by destination owner with Microsoft/Windows rows in red: bulk unblock (including "unblock all Microsoft-owned"), legacy consolidation (4 rules per IP → 2, provenance preserved), CSV export, re-enable all.
- **🚑 Restore Internet panic button** (status bar, every tab) — one click disables every AutoCommand-created block rule and runs `ipconfig /flushdns` automatically. Nothing is deleted; fully reversible.
- **↩ Restore except Microsoft/Windows** — the smart middle path after a panic: brings back your real blocks and keeps only the breakage-causing ones (Microsoft/CDN targets, Windows components) disabled, with a preview of exactly what stays off.
- **Block guardrails** — before a block lands, the target is classified: Microsoft/CDN destinations and DNS-carrying service hosts warn about what will break. Blocks are now 2 idempotent rules with full provenance (process, PID, host, byte counts, UTC time) in the description — no more GUID piles in `wf.msc`.
- **🌐 RDAP-verify + machine-wide cache** — one button asks the registries who owns each unclassified target; answers are cached by registered block in `C:\ProgramData\AutoCommand\rdap-cache.json` (so one lookup covers thousands of sibling IPs), honoring server TTLs, with negative-caching so flaky networks don't hammer registries.
- **📋 Drop-logging button** — enables firewall dropped-packet logging on all profiles from the manager; the new "Hits 10m" column then shows how often each block actually fires (0-hit rows = cleanup candidates).
- **🔍 Process Monitor** — `Last Update` moves up beside Process so recency is visible at a glance.
- **⚙ Settings** — "Developed by" replaced by a footnote: © 2026 Dan Park, magicpoint.ai · Made with ❤️ in California.

## Fixes

- Legacy firewall rule names (`AutoCommand IP Block - <ip>`) were mis-parsed during inventory, which would have broken unblock and classification for pre-upgrade rules.
- Drop-logging detection/enable avoids INetFwPolicy2's parameterized `FirewallProfile` property, which the .NET COM binder cannot dispatch — netsh + registry are used instead (verified live).
- README documents the new tab order and the Svchost Trace workflow.
