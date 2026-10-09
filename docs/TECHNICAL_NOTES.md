# AutoCommand — Technical Notes

How AutoCommand delivers what the [README](../README.md) promises: the technologies behind each capability, the design rules they follow, and every place on disk the app writes. No marketing — just what runs, what it touches, and why.

---

## Design rules

1. **Zero-PowerShell core.** Monitoring and mitigation talk to Windows directly — COM, WMI, and P/Invoke — never by spawning `powershell.exe`. Native command-line tools (`powercfg`, `bcdedit`, `schtasks`, `wevtutil`) are used only where Microsoft ships no COM or .NET surface, and always through one audited runner (`Helpers/ProcessRunner.cs`).
   *The one documented exception:* applying DBX updates uses the `Set-SecureBootUEFI` PowerShell cmdlet, because Microsoft provides no native C# API for UEFI Secure Boot variable writes.
2. **Nothing runs without you.** Bloatware removal, firewall profile applies, and DBX updates all confirm first; AI-generated commands are shown for explicit approval before execution.
3. **Non-removable means non-offered.** System-signed OS components are filtered out of the Default Apps list and are never offered by bloatware removal — by design, not by configuration.
4. **Verified downloads.** Auto-downloaded tools (Sysinternals `sigcheck64.exe`) must pass both a logged SHA256 check and a native `WinVerifyTrust` Authenticode verification; anything else is deleted, never executed.
5. **Plain, inspectable state.** User preferences live in readable JSON and text files (listed at the end), so you can back up, diff, or edit everything the app remembers.

---

## Subsystem by subsystem

### Process Monitor & network analytics
| Concern | Technology |
|---|---|
| Connection events | Sysmon — `EventLogWatcher` over the `Microsoft-Windows-Sysmon/Operational` channel; the installer configures the manifest via `wevtutil` and can repair inconsistent installs |
| Packet counters | Raw-socket sniffer — `IOControlCode.ReceiveAll` (SIO_RCVALL) on raw IP sockets; no WinPcap/Npcap dependency |
| Name resolution | Background reverse-DNS service with a persistent JSON cache |
| Task attribution | taskhostw instance GUID on the command line correlated with Task Scheduler history (the channel is enabled once if off) |
| Live ordering | WPF `ICollectionView` live sorting on the row's `LastSeen` timestamp; stale rows detected off a UI timer |

### Advanced Firewall
| Concern | Technology |
|---|---|
| Rule engine | `INetFwPolicy2` COM (`HNetCfg.FwPolicy2`) — enumerate, enable/disable, create block rules |
| Group names | Indirect strings (`@FirewallAPI.dll,-32752`) resolved via `SHLoadIndirectString` (`shlwapi.dll`) |
| Profiles & drift | Baseline of every rule's enabled state stored as JSON in `C:\ProgramData\AutoCommand`; re-scanned to report drift |
| 1-click blocks | Inbound + outbound rules per executable or remote address, created over the same COM interface |

### Security Enforcer
| Concern | Technology |
|---|---|
| Device events | WMI `__InstanceCreationEvent` / `__InstanceDeletionEvent` / `__InstanceModificationEvent` subscriptions on `Win32_NetworkAdapter` — zero polling idle cost |
| SSTP guard | Service state via the Service Controller; re-asserts stopped + disabled (`Start=4`) in a reconciliation sweep |
| Kernel-debug block | `bcdedit` state reads/writes plus KDNIC adapter removal |
| Adapter removal | `SetupAPI` P/Invoke (`setupapi.dll`) in `WanMiniportRemover.cs` — kernel-mode clean device removal, no `pnputil` |
| Logon task | `schtasks /create … /rl highest` via the shared runner |
| Alerts | Windows toast notifications (`Windows.UI.Notifications`) |

### Default Apps & Bloatware
| Concern | Technology |
|---|---|
| App inventory | `Windows.Management.Deployment.PackageManager` (`FindPackagesForUser`) — frameworks, resource packages, and System-signed components filtered to match Settings → Installed apps |
| Signature status | `Package.SignatureKind`, validated by the deployment engine at install time (Store / Enterprise / System / Developer / Unsigned) |
| Uninstall | `PackageManager.RemovePackageAsync` per package full name |
| OneDrive (Win32) | Not a Store package — detected via install directory + `UninstallString` registry entry (HKCU/HKLM), removed with `OneDriveSetup.exe /uninstall`, `winget` fallback |
| Your custom list | Delta config (opt-outs + additions) in `C:\ProgramData\AutoCommand\bloatware_config.json`, merged over the compiled-in defaults by `BloatwareConfig` |

### UEFI & Secure Boot
| Concern | Technology |
|---|---|
| EFI baseline | SHA256 hash of every `.efi` module on the EFI System Partition; drift reported against a stored baseline |
| Boot manager check | Sysinternals `sigcheck64.exe` — downloaded only after SHA256 + Authenticode (`wintrust.dll` `WinVerifyTrust`) gates; its UTF-16LE redirected output is decoded before parsing |
| DBX updates | `DBXUpdate.bin` downloaded from Microsoft's secureboot_objects repository, `EFI_SIGNATURE_LIST` parsed in C#, applied with `Set-SecureBootUEFI` after explicit confirmation |

### AI Assistant
| Concern | Technology |
|---|---|
| Cloud engine | Gemini REST API; the API key is stored locally, never bundled |
| Local engine | `LLamaSharp` running GGUF models on CPU or CUDA 12 — works air-gapped |
| Context | Every page implements `IAiAuditable`, serializing its live state (rules, packages, rows, boot files) into the prompt |
| Safety | Generated commands are displayed for approval; execution goes through the same native runner as everything else |

### Platform
* **.NET 10 (WPF)** front end; WebView2 hosts the opt-in Firebase Analytics page only.
* **Plugins** load through the bundled `AutoCommand.Sdk`.
* **README infographics** are generated by `Assets/generate_infographics.py` (Python + matplotlib).

---

## What the app writes on your disk

| Path | Purpose |
|---|---|
| `C:\ProgramData\AutoCommand\bloatware_config.json` | Your bloatware-list customizations (delta over defaults) |
| `C:\ProgramData\AutoCommand\firewall_config.json` | Per-rule firewall overrides |
| `C:\ProgramData\AutoCommand\firewall_baseline.json` | Firewall drift baseline |
| `<app>\blocked.txt` | Firewall blocks created from the monitors |
| `<app>\ignored_processes.txt` | Process Monitor ignore list |
| `<app>\ultimate_autopilot_stats.csv` | Hourly connection-stats export |
| `<app>\check_interval.txt` | Security Enforcer cadence |

Telemetry is **opt-in** and limited to the Firebase JS SDK inside a sandboxed WebView2 control; the local LLM and Gemini paths send nothing anywhere by themselves.
