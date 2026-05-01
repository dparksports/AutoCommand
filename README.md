# Auto-Command

Auto-Command is an enterprise-grade C# WPF security toolkit designed to provide granular control over Windows OS security features. It completely eliminates PowerShell dependencies, relying solely on highly efficient native C#, P/Invoke, and COM APIs for maximum performance and security.

## Features

*   **Zero-PowerShell Architecture**: All functionality uses native C# logic, WMI, COM (`INetFwPolicy2`, `Schedule.Service`), and P/Invoke (`iphlpapi.dll`, `setupapi.dll`), minimizing attack surfaces and maximizing speed.
*   **Security Enforcer**: A persistent background loop that automatically monitors the system for configuration drift, unauthorized VPN interfaces, hidden hotspots, high-privilege tasks, and malicious Hosts file redirects.
*   **Active Connections Monitor (Netstat)**: A native C# raw socket sniffer and IP helper API integration that displays live TCP/UDP connections mapped to PIDs, allowing instant process termination.
*   **Svchost Monitor**: Integrates natively with `Microsoft-Windows-Sysmon/Operational` via `EventLogWatcher` to track non-Microsoft `svchost` network activity in real-time, dumping tallies directly to memory and periodic CSV files.
*   **OS Hardening & Privacy**: 1-click toggles for LSA Protection (RunAsPPL), maximum UAC strictness, Windows Telemetry, Kernel Debugging (KDNET), and WiFi Direct hotpots.
*   **DBX Firmware Safety Check**: Authenticode and DBX revocation checks on the UEFI bootloader, ensuring firmware integrity against bootkits.
*   **Startup Persistence Scanner**: Enumerates all `HKCU`/`HKLM` Run keys and Startup directories to hunt and squish persistent malware binaries.
*   **Advanced Firewall Management**: Native interface to manage inbound/outbound rules and quickly apply trusted or strict enterprise profiles.

## Requirements
*   Windows 10/11
*   .NET 10 SDK
*   Administrative Privileges (required for raw sockets, Sysmon integration, and COM API management)

## Build Instructions
1. Clone the repository.
2. Ensure you have the .NET 10 SDK installed.
3. Build and run:
   ```powershell
   dotnet build
   dotnet run
   ```

## License
Licensed under the [Apache License, Version 2.0](LICENSE).
