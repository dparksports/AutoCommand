# AutoCommand — see what your Windows PC is really doing, then take it back with one click

![AutoCommand](Assets/corp_banner.png)

**AutoCommand** is a free, open-source security and control dashboard for Windows 10 and 11. It runs as one administrator window where every question that normally costs you an afternoon — *what is this process, who launched it, where is it connecting, how do I make it stop, why does my PC phone home, what updated itself last night?* — becomes a row in a table, a colored status card, or a single button.

> **⬇ Download:** grab the latest self-contained build from the [**Releases page**](https://github.com/dparksports/autocommand-windows/releases) — no .NET installation required. Unzip, run `AutoCommand.exe` as Administrator, done.
>
> **🔧 Under the hood:** every technology mentioned below is documented in the [**Technical Notes**](docs/TECHNICAL_NOTES.md) — what runs, what it touches on disk, and why. Licensed Apache 2.0.

---

## Why it exists

Windows spreads the answers across a dozen tools that don't talk to each other. AutoCommand puts the whole picture in one place and turns the scary parts into single clicks:

![Why AutoCommand](Assets/corp_why.png)

Concretely, the situations it is built for:

* **A mystery process is sending packets to a server you don't recognize.** Find it by PID or destination, read which process owns it and what launched it — even if the process already exited — then block the IP, block the executable, kill the process, or disable the scheduled task that would resurrect it tomorrow at 3 a.m.
* **You want a record, not a guess.** Install Sysmon from the app in one click, and from then on every process launch (full command line and parent), every network connection and every DNS query is recorded locally — so "what was that?" becomes a lookup instead of forensics.
* **An app keeps replacing itself** and you want it pinned at the version that works. Freeze it: the app keeps running, its updater fails politely, and unfreezing restores the original permissions exactly.
* **Libraries and apps silently beacon usage statistics.** See which installed software has telemetry on, apply the vendor's own documented opt-out with the previous value snapshotted, and verify the beacons actually stopped.
* **A fresh Windows install needs an afternoon of cleanup.** Debloat 23 preinstalled apps in one confirmation, lock the firewall down, remove kernel-debug surfaces, guard the Secure Boot chain — and skip the steps for the things you actually use.

---

## What you get, tab by tab

### 🚀 Quick Scan — the first-hour ritual, one pass

![Quick Scan and Bloatware](Assets/corp_debloat.png)

The whole post-install cleanup — debloat sweep, firewall lockdown, privacy changes, hardening checks — as a single guided pass. Every step reuses the exact actions behind its dedicated tab, and the checklist doubles as a live status dashboard that re-verifies when the pass finishes. For the first hour after a clean Windows install, this is the only tab you need.

### 📡 Attack Surface — close the doors Windows leaves open

One inventory for the network surfaces Windows ships with and never mentions: SSTP tunneling and WAN miniports (remote-access doors), KDNET kernel-debug adapters, hosted-network and Wi-Fi Direct ghost hotspots. Inspect status at a glance, then disable, neuter, or remove each one — with a refresh that keeps the picture honest.

### 🛡 Advanced Firewall — lockdown to home-LAN in seconds

Curated profiles (full lockdown, home-LAN, per-app) applied in one click over the `INetFwPolicy2` COM engine, with a baseline of every rule's enabled state and drift reporting on re-scan. One-click block rules for any IP or executable, from here or from the Process Monitor.

### 📦 Bloatware — 23 apps gone in one confirmation

Twenty-three preinstalled apps (Copilot, Teams, OneDrive, Xbox, Solitaire, Feedback Hub, …) removed in one sweep — OneDrive through its own Win32 uninstaller, Store apps through the deployment engine, and classic OS components like `mstsc.exe` deliberately never offered. Every row carries a **＋ Bloatware / ✓ Bloatware** toggle and a Manage-List dialog, so if you actually use one of them, it never gets touched again; your list persists machine-wide as a delta over the defaults.

### 🧭 Sysmon Audit + 🔍 Process Monitor — every connection, attributed

![See everything — and know who launched it](Assets/corp_visibility.png)

**Sysmon Audit** builds the record Windows doesn't keep: one status card (service state, channel health, events per 24 h, active config hash) with Install / Repair / Apply-Config buttons, and a live feed of every process launch with its full command line and parent — backfilled from the log when you open the tab, so the answer survives hours later. The recommended configuration captures process creations, network connections and DNS queries.

**Process Monitor** turns that record into a live, self-updating grid of outbound connections (with a raw-socket sniffer for packet counters — no WinPcap/Npcap needed), sorted most-recent-traffic-first: a row jumps to the top the moment new packets arrive, and rows silent for 5+ minutes fade. Reverse-DNS runs in the background — you read hostnames, not IPs.

The part Windows cannot do: **attribution that survives process exit.** Short-lived helpers (spawned scripts, one-shot updaters, `taskhostw.exe` DLL hosts) are named, pathed, and tied to the scheduled task that launched them — including *which DLL* a COM-handler task actually executes, and a flag when that DLL no longer exists (classic leftover of uninstalled software). Right-click any row to block its IP or executable, kill the process, or inspect/disable the task behind it. Everything exports to CSV.

**Blocking that won't bite you.** Before a block lands, the destination is classified against known Microsoft/CDN ranges and service hosts are recognized — so taking out an update edge or the DNS-carrying svchost instance warns you *first*. Every block is 2 idempotent rules (not 4 near-duplicates) with full provenance in the rule description, readable from `wf.msc`. **Blocked → Manage all** opens the fleet view: targets grouped by destination owner with Microsoft/Windows rows in red, bulk unblock (including "unblock all Microsoft-owned"), one-click legacy-set consolidation (4 rules → 2), hit counts from the firewall's own drop log (one button to enable), and **RDAP-verified ownership** — registry answers cached machine-wide in `C:\ProgramData\AutoCommand\rdap-cache.json`, keyed by registered block so one lookup covers thousands of sibling IPs.

### 🔬 Svchost Trace — the timeline and the pattern detector

**Svchost Trace** watches the 70+ `svchost.exe` service hosts the way Process Monitor watches connections: spawn events with parent process and hosted service, TCP connect/close with lifetimes, cumulative byte samples — one JSONL timeline you can export and re-analyze offline. On top of it, a pattern detector flags what matters: svchost instances whose parent isn't `services.exe`, service respawn loops, destinations classified against known Microsoft/CDN ranges (unknowns flagged for RDAP verification), beacon-regular connection intervals (coefficient of variation below 0.35), and upload-dominant flows.

The tab also drives the headless capture service: **Start/Stop** runs the collector as a detached process that survives the app; **Auto-start on boot** persists it via your user Run key (no UAC); a global mutex keeps exactly one capture alive; size-capped rotation keeps the disk bounded. Traces exported here replay in the command-line analyzer (`svchost-watch/SvchostAnalyzer`).

### 📊 Telemetry — real opt-outs, verified quiet

![Pin the version that works — silence the beacons](Assets/corp_control.png)

Scans installed software against a knowledge base of known telemetry senders (Ultralytics YOLO, .NET CLI, PowerShell 7, VS Code, Edge, Chrome, Windows diagnostic data — updatable without a rebuild). Each row shows the on/off state with the exact evidence, applies the **vendor's own documented opt-out** (a settings key, environment variable or policy — never a firewall hack), keeps the previous value snapshotted so one click reverts it, and verifies via the DNS resolver cache that the beacons actually stopped. The first row of that table is ours: AutoCommand's own telemetry is opt-in.

### 👁 Privacy — your choices, visibly in force

Disables Microsoft's diagnostic-data task, keeps AutoCommand's own analytics behind an explicit opt-in, and surfaces tamper-protection status — so you can see at a glance whether your choices are still in force.

### ❄ Update Control — pin the version that works

Discovers self-updating apps on your machine (self-update `.old` leftovers, updater folders, autostart updaters) with their vendor signatures. Freezing one snapshots its permissions (`icacls /save`) and then denies delete/write on the executable: the app runs normally, its updater fails until you unfreeze, and unfreezing restores the original ACL exactly. Windows-signed system binaries are refused outright.

### 📋 Tasks · 🚀 Startup · 🌙 Hibernation · 🌐 Connections

Full Task Scheduler control (enable, disable, run, stop, delete), startup-entry management, hibernation posture in one toggle, and an alternate live view of current connections with process attribution and background reverse-DNS.

### 🔒 OS Hardening + ✅ DBX Safety — lock it down, keep it locked

![Lock it down — and keep it locked](Assets/corp_hardening.png)

Self-healing guards stop and disable SSTP tunneling and kernel-debug surfaces — and re-assert themselves if something drifts back, with toast alerts from the background Security Enforcer. The Secure Boot chain is hashed and watched: every EFI module against a stored baseline, boot-manager checks gated by SHA256 + Authenticode, and Microsoft DBX revocation updates parsed and applied only after explicit confirmation.

### ⚙ Settings

Startup behavior, analytics preference, and live application information — plus a status bar that always tells you whether the background Security Enforcer is active, and a **🚑 Restore Internet** panic button for the worst-case click: one press disables every AutoCommand-created block rule (nothing deleted, fully reversible) and flushes the DNS cache automatically, while **Restore except Microsoft/Windows** in the Blocked Rules manager brings back your real blocks and leaves only the breakage-causing ones off. The About card carries the © footnote.

---

## What AutoCommand will not do

* **Nothing runs without you.** Removals, applies, freezes and DBX updates all confirm first; AI-generated commands are displayed for approval.
* **Non-removable means non-offered.** System-signed OS components never appear in the bloatware list, and Windows binaries are never frozen by Update Control.
* **No hidden state.** Everything the app remembers is readable JSON/text on disk, listed at the end of the [Technical Notes](docs/TECHNICAL_NOTES.md). Its own telemetry is opt-in and off unless you say yes.

## Build from source

```powershell
# .NET 10 SDK required
git clone https://github.com/dparksports/autocommand-windows.git
cd autocommand-windows
dotnet build AutoCommand.csproj -c Release

# self-contained build (what the releases ship)
dotnet publish AutoCommand.csproj -c Release -r win-x64 --self-contained
```

Run the published `AutoCommand.exe` as Administrator — WPF, .NET 10, Windows 10 (19041+) / Windows 11.

## Contributing & license

Issues and PRs welcome — pick a tab, keep the design rules in the [Technical Notes](docs/TECHNICAL_NOTES.md) (zero-PowerShell core, explicit confirmation, verified downloads), and match the existing code style. Apache License 2.0.
