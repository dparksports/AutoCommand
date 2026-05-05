using System.Runtime.Versioning;

// AutoCommand is a Windows-only WPF application.
// Declaring the assembly target here satisfies the CA1416 platform-compatibility
// analyzer for all Windows API calls throughout the codebase, removing the need
// for per-call [SupportedOSPlatform] guards or #pragma suppressions.
[assembly: SupportedOSPlatform("windows10.0.17763.0")]
