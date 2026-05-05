# AutoCommand v3.4

AutoCommand is an enterprise-grade C# WPF security toolkit designed to provide non-technical users with granular control over Windows OS security features. It emphasizes native performance, zero-dependency deployment, and conversational AI assistance.

## What's New in v3.4

*   **✨ Gemini Command Assistant**: A new natural-language chat interface in the Command Panel. Users can ask the AI to perform system tasks, and Gemini will generate and safely execute the required PowerShell scripts.
*   **📦 Default Apps Uninstaller**: Easily view and remove pre-installed Windows bloatware. Includes live Authenticode signature validation for Appx packages using native WinRT APIs.
*   **🧩 Plugin & Extensibility System**: Developers can now add features without modifying the core app. Drop `.dll` or raw `.cs` files into the `Plugins/` folder, and the new Roslyn-powered loader will compile and integrate them at runtime.
*   **🌐 DNS Resolution & Connections UI**: The active network connections tab now resolves and displays remote domains and geographic data dynamically, utilizing a fast, rate-limited caching service.
*   **🛡️ Active Process Blocking**: Right-click any active network connection to instantly generate native COM (`INetFwPolicy2`) Windows Firewall block rules (Inbound and Outbound) for the underlying executable.
*   **Advanced Firewall Profiles**: The Firewall tab now includes a 1-click Quick Profile selector (Shield Up, Gaming, Office, Home, Public Strict).

## Core Features

*   **Zero-PowerShell Architecture**: Core monitoring and enforcement functionality uses native C# logic, WMI, COM (`INetFwPolicy2`, `Schedule.Service`), WinRT, and P/Invoke (`iphlpapi.dll`, `setupapi.dll`), maximizing speed and stability.
*   **Security Enforcer**: A persistent background loop that automatically monitors the system for configuration drift, unauthorized VPN interfaces, hidden hotspots, high-privilege tasks, and malicious Hosts file redirects.
*   **Process & Network Monitor**: Integrates natively with `Microsoft-Windows-Sysmon/Operational` via `EventLogWatcher` and raw sockets to track application network activity (packets and bytes) in real-time.
*   **OS Hardening & Privacy**: Toggles for LSA Protection, UAC strictness, Windows Telemetry, Kernel Debugging (KDNET), and WiFi Direct hotspots, including IPv6 deactivation.
*   **DBX Firmware Safety Check**: Authenticode and DBX revocation checks on the UEFI bootloader, ensuring firmware integrity against bootkits.
*   **Startup Persistence Scanner**: Enumerates all `HKCU`/`HKLM` Run keys and Startup directories to hunt and squish persistent malware binaries.

## Requirements
*   Windows 10/11
*   .NET 10 SDK (or a self-contained build)
*   Administrative Privileges (required for raw sockets, Sysmon integration, and COM API management)

## Build Instructions
1. Clone the repository.
2. Ensure you have the .NET 10 SDK installed.
3. Build and run as Administrator:
   ```powershell
   dotnet build
   Start-Process -FilePath "dotnet" -ArgumentList "run --project AutoCommand.csproj" -Verb RunAs
   ```

## License
Licensed under the [Apache License, Version 2.0](LICENSE).
