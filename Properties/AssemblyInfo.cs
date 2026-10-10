using System.Reflection;
using System.Runtime.Versioning;

// Release version — the status bar reads ProductVersion (InformationalVersion)
// from the running exe. Keep this in sync with <Version> in AutoCommand.csproj
// on every release (GenerateAssemblyInfo is disabled for the WPF designer, so
// the csproj value alone does not reach the binary).
[assembly: AssemblyVersion("2026.10.10.0")]
[assembly: AssemblyFileVersion("2026.10.10.0")]
[assembly: AssemblyInformationalVersion("2026.10.10")]

// AutoCommand is a Windows-only WPF application.
// Declaring the assembly target here satisfies the CA1416 platform-compatibility
// analyzer for all Windows API calls throughout the codebase, removing the need
// for per-call [SupportedOSPlatform] guards or #pragma suppressions.
[assembly: SupportedOSPlatform("windows10.0.17763.0")]
