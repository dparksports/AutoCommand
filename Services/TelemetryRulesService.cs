using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AutoCommand.Helpers;

namespace AutoCommand.Services
{
    // -----------------------------------------------------------------------
    // Rule knowledge base (Assets/telemetry_rules.json, seed fallback below)
    // -----------------------------------------------------------------------

    public class TelemetryRule
    {
        public string Id { get; set; }
        public string AppName { get; set; }
        public string Category { get; set; }
        public string Mechanism { get; set; }
        public string Scope { get; set; }
        public string RiskNote { get; set; }
        public List<string> WatchDomains { get; set; } = new();
        public DetectionSpec Detection { get; set; }
        public ProbeSpec StateProbe { get; set; }
        public OptOutSpec OptOut { get; set; }
    }

    public class DetectionSpec
    {
        public string Type { get; set; }        // file | env | os
        public string Path { get; set; }
    }

    public class ProbeSpec
    {
        public string Type { get; set; }        // json | env | registry
        public string Path { get; set; }
        public string Key { get; set; }
        public string Var { get; set; }
        public string RegKey { get; set; }
        public string RegName { get; set; }
        public List<string> OffValues { get; set; } = new();
    }

    public class OptOutSpec
    {
        public string Type { get; set; }        // json | env | registry
        public string Path { get; set; }
        public string Key { get; set; }
        public string Value { get; set; }
        public string Var { get; set; }
        public string RegKey { get; set; }
        public string RegName { get; set; }
        public string Kind { get; set; } = "dword";
    }

    public enum TelemetryState { On, Off, Unknown }

    public class TelemetryRuleStatus
    {
        public TelemetryRule Rule { get; set; }
        public bool Detected { get; set; }
        public TelemetryState State { get; set; }
        public string StateText => State switch
        {
            TelemetryState.On => "Telemetry on",
            TelemetryState.Off => "Opted out",
            _ => "Unknown"
        };
        public string Evidence { get; set; }
        public bool HasRevertSnapshot { get; set; }
    }

    // -----------------------------------------------------------------------
    // Service
    // -----------------------------------------------------------------------

    /// <summary>
    /// Opt-out manager for telemetry across installed software. Rules live in
    /// Assets/telemetry_rules.json (updatable without a rebuild); each rule
    /// knows how to detect the app, whether its telemetry is on, and how to
    /// opt out and revert. Every applied change is snapshotted to
    /// telemetry_state.json so opt-outs are one-click reversible.
    /// </summary>
    public class TelemetryRulesService
    {
        private const string RulesFile = "Assets\\telemetry_rules.json";
        private const string StateFile = "telemetry_state.json";

        private sealed class ChangeRecord
        {
            public string RuleId { get; set; }
            public string Kind { get; set; }        // json | env | registry
            public string Target { get; set; }      // file path / var name / reg key
            public string Name { get; set; }        // json key / reg value name
            public string PreviousValue { get; set; } // null-marker = was absent
            public DateTime ChangedAt { get; set; }
        }

        // telemetry_rules.json uses camelCase keys; matching is case-insensitive
        // so hand-edited rules with PascalCase keys also load
        private static readonly JsonSerializerOptions RuleJsonOptions =
            new() { PropertyNameCaseInsensitive = true };

        private readonly string _rulesPath = Path.Combine(AppContext.BaseDirectory, RulesFile);
        private List<ChangeRecord> _changes = new();

        public TelemetryRulesService()
        {
            LoadChanges();
        }

        public List<TelemetryRule> Rules => LoadRules();

        private List<TelemetryRule> LoadRules()
        {
            try
            {
                if (File.Exists(_rulesPath))
                {
                    var doc = JsonNode.Parse(File.ReadAllText(_rulesPath)).AsObject();
                    var rules = doc["rules"].Deserialize<List<TelemetryRule>>(RuleJsonOptions);
                    if (rules is { Count: > 0 }) return rules;
                }
            }
            catch { /* fall through to built-in seed */ }
            return SeedRules();
        }

        /// <summary>Same content as the shipped JSON — used when the file is missing or corrupt.</summary>
        private static List<TelemetryRule> SeedRules() =>
            JsonSerializer.Deserialize<List<TelemetryRule>>(@"
[
  {
    ""id"": ""ultralytics-sync"",
    ""appName"": ""Ultralytics YOLO (Python)"",
    ""category"": ""Python library"",
    ""mechanism"": ""Sends anonymized usage statistics to Google Analytics whenever the library loads, tied to a persistent install uuid."",
    ""scope"": ""User"",
    ""riskNote"": ""Low — model training and inference are unaffected."",
    ""watchDomains"": [ ""google-analytics.com"" ],
    ""detection"": { ""type"": ""file"", ""path"": ""%APPDATA%\\Ultralytics\\settings.json"" },
    ""stateProbe"": { ""type"": ""json"", ""path"": ""%APPDATA%\\Ultralytics\\settings.json"", ""key"": ""sync"", ""offValues"": [ ""false"" ] },
    ""optOut"": { ""type"": ""json"", ""path"": ""%APPDATA%\\Ultralytics\\settings.json"", ""key"": ""sync"", ""value"": ""false"" }
  }
]", RuleJsonOptions) ?? new List<TelemetryRule>();

        // -----------------------------------------------------------------------
        // Evaluation
        // -----------------------------------------------------------------------

        public Task<List<TelemetryRuleStatus>> EvaluateAllAsync() => Task.Run(() =>
            Rules.Select(Evaluate).ToList());

        private TelemetryRuleStatus Evaluate(TelemetryRule rule)
        {
            var status = new TelemetryRuleStatus { Rule = rule, State = TelemetryState.Unknown };

            try
            {
                status.Detected = rule.Detection?.Type switch
                {
                    "file" => File.Exists(Expand(rule.Detection.Path)),
                    "os" => true,
                    _ => false
                };
            }
            catch { status.Detected = false; }

            if (!status.Detected)
            {
                status.Evidence = "Not detected on this machine.";
                return status;
            }

            var (state, evidence) = Probe(rule.StateProbe);
            status.State = state;
            status.Evidence = evidence;
            status.HasRevertSnapshot = _changes.Any(c => c.RuleId == rule.Id);
            return status;
        }

        private static (TelemetryState, string) Probe(ProbeSpec probe)
        {
            if (probe == null) return (TelemetryState.Unknown, "No state probe defined.");

            try
            {
                switch (probe.Type)
                {
                    case "json":
                    {
                        string path = Expand(probe.Path);
                        if (!File.Exists(path)) return (TelemetryState.Unknown, $"{probe.Path} not found.");
                        string current = ReadJsonKey(path, probe.Key)?.ToString();
                        return Match(current, probe.OffValues,
                            $"{probe.Key} = {current ?? "(absent)"} in {probe.Path}");
                    }
                    case "env":
                    {
                        string value = Environment.GetEnvironmentVariable(probe.Var, EnvironmentVariableTarget.User)
                                       ?? Environment.GetEnvironmentVariable(probe.Var, EnvironmentVariableTarget.Machine);
                        return Match(value, probe.OffValues,
                            $"{probe.Var} = {value ?? "(not set)"}");
                    }
                    case "registry":
                    {
                        string value = ReadRegistry(probe.RegKey, probe.RegName);
                        return Match(value, probe.OffValues,
                            $"{probe.RegKey}\\{probe.RegName} = {value ?? "(not set)"}");
                    }
                }
            }
            catch (Exception ex)
            {
                return (TelemetryState.Unknown, $"Probe failed: {ex.Message}");
            }
            return (TelemetryState.Unknown, "Unknown probe type.");
        }

        private static (TelemetryState, string) Match(string current, List<string> offValues, string evidence)
        {
            bool off = current != null && offValues.Any(v =>
                string.Equals(v, current, StringComparison.OrdinalIgnoreCase));
            return (off ? TelemetryState.Off : TelemetryState.On, evidence);
        }

        // -----------------------------------------------------------------------
        // Apply / revert
        // -----------------------------------------------------------------------

        public async Task<(bool Success, string Message)> ApplyOptOutAsync(TelemetryRule rule)
        {
            if (rule?.OptOut == null) return (false, "Rule has no opt-out action.");
            try
            {
                switch (rule.OptOut.Type)
                {
                    case "json":
                    {
                        string path = Expand(rule.OptOut.Path);
                        if (!File.Exists(path))
                            await File.WriteAllTextAsync(path, "{}");
                        string previous = ReadJsonKey(path, rule.OptOut.Key)?.ToString();
                        WriteJsonKey(path, rule.OptOut.Key, rule.OptOut.Value);
                        RecordChange(rule.Id, "json", path, rule.OptOut.Key, previous);
                        return (true, $"{rule.AppName}: set {rule.OptOut.Key} = {rule.OptOut.Value}.");
                    }
                    case "env":
                    {
                        string previous = Environment.GetEnvironmentVariable(rule.OptOut.Var, EnvironmentVariableTarget.User);
                        Environment.SetEnvironmentVariable(rule.OptOut.Var, rule.OptOut.Value, EnvironmentVariableTarget.User);
                        RecordChange(rule.Id, "env", rule.OptOut.Var, null, previous);
                        return (true, $"{rule.AppName}: set {rule.OptOut.Var} = {rule.OptOut.Value} (user).");
                    }
                    case "registry":
                    {
                        string previous = ReadRegistry(rule.OptOut.RegKey, rule.OptOut.RegName);
                        WriteRegistry(rule.OptOut.RegKey, rule.OptOut.RegName, rule.OptOut.Value,
                            string.Equals(rule.OptOut.Kind, "dword", StringComparison.OrdinalIgnoreCase));
                        RecordChange(rule.Id, "registry", rule.OptOut.RegKey, rule.OptOut.RegName, previous);
                        return (true, $"{rule.AppName}: set {rule.OptOut.RegName} = {rule.OptOut.Value}.");
                    }
                    default:
                        return (false, $"Unknown opt-out type '{rule.OptOut.Type}'.");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Opt-out failed: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> RevertAsync(TelemetryRule rule)
        {
            var record = _changes.LastOrDefault(c => c.RuleId == rule.Id);
            if (record == null) return (false, "No snapshot for this rule — nothing to revert.");

            try
            {
                switch (record.Kind)
                {
                    case "json":
                        if (record.PreviousValue == null) RemoveJsonKey(record.Target, record.Name);
                        else WriteJsonKey(record.Target, record.Name, record.PreviousValue);
                        break;
                    case "env":
                        Environment.SetEnvironmentVariable(record.Target, record.PreviousValue,
                            EnvironmentVariableTarget.User);
                        break;
                    case "registry":
                        if (record.PreviousValue == null) DeleteRegistryValue(record.Target, record.Name);
                        else WriteRegistry(record.Target, record.Name, record.PreviousValue, true);
                        break;
                }
                _changes.Remove(record);
                SaveChanges();
                await Task.CompletedTask;
                return (true, $"{rule.AppName}: previous setting restored.");
            }
            catch (Exception ex)
            {
                return (false, $"Revert failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks the DNS resolver cache for a telemetry domain — the cheap
        /// "is it still beaconing?" test, mirroring a manual
        /// Get-DnsClientCache inspection.
        /// </summary>
        public async Task<bool> CheckRecentDnsLookupAsync(string domain)
        {
            var ps = $"if (Get-DnsClientCache -ErrorAction SilentlyContinue | Where-Object {{ $_.Entry -like '*{domain}*' }}) {{ 'SEEN' }} else {{ 'CLEAN' }}";
            var (output, _, _) = await ProcessRunner.RunWithDetailsAsync("powershell.exe",
                $"-NoProfile -NonInteractive -Command \"{ps}\"");
            return (output ?? string.Empty).Trim().EndsWith("SEEN", StringComparison.Ordinal);
        }

        // -----------------------------------------------------------------------
        // Primitive readers/writers
        // -----------------------------------------------------------------------

        private static string Expand(string path) =>
            Environment.ExpandEnvironmentVariables(path ?? string.Empty);

        private static JsonNode ReadJsonKey(string path, string key)
        {
            var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
            return root != null && root.ContainsKey(key) ? root[key] : null;
        }

        private static void WriteJsonKey(string path, string key, string value)
        {
            var root = (JsonNode.Parse(File.ReadAllText(path)) as JsonObject) ?? new JsonObject();
            root[key] = value;
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void RemoveJsonKey(string path, string key)
        {
            if (!File.Exists(path)) return;
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root) return;
            root.Remove(key);
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private static string ReadRegistry(string regKey, string name)
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(StripHklm(regKey));
            return key?.GetValue(name)?.ToString();
        }

        private static void WriteRegistry(string regKey, string name, string value, bool dword)
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(StripHklm(regKey));
            if (dword && int.TryParse(value, out int number))
                key.SetValue(name, number, Microsoft.Win32.RegistryValueKind.DWord);
            else
                key.SetValue(name, value, Microsoft.Win32.RegistryValueKind.String);
        }

        private static void DeleteRegistryValue(string regKey, string name)
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(StripHklm(regKey), writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }

        private static string StripHklm(string regKey) =>
            regKey.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase)
                ? regKey[5..]
                : regKey;

        // -----------------------------------------------------------------------
        // Snapshot persistence (telemetry_state.json)
        // -----------------------------------------------------------------------

        private void RecordChange(string ruleId, string kind, string target, string name, string previousValue)
        {
            // Latest snapshot per rule wins on revert
            _changes.RemoveAll(c => c.RuleId == ruleId);
            _changes.Add(new ChangeRecord
            {
                RuleId = ruleId,
                Kind = kind,
                Target = target,
                Name = name,
                PreviousValue = previousValue,
                ChangedAt = DateTime.Now
            });
            SaveChanges();
        }

        private void LoadChanges()
        {
            try
            {
                if (File.Exists(StateFile))
                    _changes = JsonSerializer.Deserialize<List<ChangeRecord>>(File.ReadAllText(StateFile)) ?? new();
            }
            catch { _changes = new(); }
        }

        private void SaveChanges()
        {
            try
            {
                File.WriteAllText(StateFile, JsonSerializer.Serialize(_changes,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* best-effort — the changed setting itself persists */ }
        }
    }
}
