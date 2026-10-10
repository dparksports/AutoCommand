# AutoCommand v2026.10.11

Self-contained Windows build — **no .NET installation required**. Unzip and run `AutoCommand.exe` as Administrator.

## Patch fix

- **🐛 Sysmon Audit: fixed a crash when opening the tab with Sysmon installed** — the process-creation feed's row-trim removed at an out-of-range index (`Count` instead of `Count-1`), and a duplicate history backfill guaranteed the trim ran on first open (v2026.10.10 only; triggered after installing Sysmon from the tab). The feed now trims from the bottom correctly and backfills exactly once. Thanks to the crash report that made this a five-minute fix.
