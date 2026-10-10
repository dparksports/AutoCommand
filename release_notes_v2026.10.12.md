# AutoCommand v2026.10.12

Self-contained Windows build — **no .NET installation required**. Unzip and run `AutoCommand.exe` as Administrator.

## Highlights

- **✂ AI Security Audit removed** — the status-bar audit button, the per-tab insight bar, the engine-setup dialog and the Gemini/LLamaSharp backends are gone while the feature is rethought. Every tab's `IAiAuditable.GetAuditContext()` state serialization is intentionally kept in the codebase as the foundation for whatever replaces it. No core capability depended on the AI layer; monitoring, hardening, freezing and telemetry opt-outs are unchanged.
- **📦 Much smaller download** — dropping the local-LLM stack (LLamaSharp CPU + CUDA 12 backends) shrinks the self-contained build by hundreds of megabytes. Faster to download, faster to start.
- **⚙ Settings shows the real version** — the About block previously carried a stale hardcoded number; it now reads the live assembly version, same as the status bar.
