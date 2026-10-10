using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using AutoCommand.Services;

namespace AutoCommand.Views
{
    /// <summary>
    /// Fleet view over every AutoCommand-created firewall block: grouped by
    /// destination owner so the risky rows (Microsoft infra / Windows
    /// components) jump out, with bulk unblock, legacy consolidation, CSV
    /// export and re-enable-all (undo for the status-bar panic button).
    /// </summary>
    public partial class BlockedRulesWindow : Window
    {
        private class Row
        {
            public string Target, Kind, Key, Ip, AppPath, Class, Description;
            public int Rules, EnabledRules, Hits;
            public DateTime? Created;
            public bool IsRisky => Class.Contains("Microsoft") || Class.Contains("Akamai") ||
                                   Class.Contains("Gcore") || Class.Contains("Windows component");
            public string RulesSummary => EnabledRules == Rules ? Rules.ToString() : $"{EnabledRules}/{Rules} on";
            public string CreatedDisplay => Created?.ToString("yyyy-MM-dd HH:mm") ?? "legacy";
            public string HitsDisplay => Hits > 0 ? Hits.ToString() : "·";
            public string OwnerDisplay { get; set; } = "";
            public string Note => Description.Length > 160 ? Description[..160] + "…" : Description;
        }

        private readonly ObservableCollection<Row> _rows = new();

        public BlockedRulesWindow()
        {
            InitializeComponent();
            var view = (ListCollectionView)CollectionViewSource.GetDefaultView(_rows);
            view.GroupDescriptions.Add(new PropertyGroupDescription("Class"));
            RulesGrid.ItemsSource = _rows;
            Loaded += (_, _) => _ = ReloadAsync();
        }

        private async Task ReloadAsync()
        {
            StatusText.Text = "Scanning firewall…";
            var inv = await FirewallService.Instance.GetBlockedInventoryAsync();
            var hits = await Task.Run(() => FirewallService.GetRecentBlockHits(10));

            _rows.Clear();
            foreach (var t in inv)
            {
                int h = 0;
                if (t.Kind == "ip" && hits.TryGetValue(t.Ip, out int v)) h = v;
                // app hits: log hits are per-IP, not per-app — leave as · for now
                string owner = "";
                if (t.Kind == "ip")
                {
                    var cached = Services.RdapService.Instance.Cached(t.Ip);
                    if (cached != null)
                        owner = cached.IsNegative ? "lookup failed" :
                                $"{cached.Netname} · {(cached.Org ?? "?")} [{cached.Country}]";
                }
                _rows.Add(new Row
                {
                    Target = t.Target, Kind = t.Kind, Key = t.Key, Ip = t.Ip, AppPath = t.AppPath,
                    Class = t.Class, Rules = t.Rules, EnabledRules = t.EnabledRules, Hits = h,
                    Description = t.Description, Created = t.Created, OwnerDisplay = owner,
                });
            }

            int risky = _rows.Count(r => r.IsRisky);
            int disabled = _rows.Count(r => r.EnabledRules == 0);
            StatusText.Text =
                $"{_rows.Count} blocked targets · {risky} risky (Microsoft/Windows — likely breakage, not threats) · {disabled} fully disabled" +
                (FirewallService.FirewallDropLoggingOn()
                    ? ""
                    : " · drop-logging is off — click 'Enable drop-logging' to see hit counts");
            ConsolidateBtn.IsEnabled = _rows.Any(r => r.Rules > 2 && r.Kind == "ip");
            UnblockMsBtn.IsEnabled = risky > 0;
            EnableLogBtn.Visibility = FirewallService.FirewallDropLoggingOn()
                ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void EnableLogBtn_Click(object sender, RoutedEventArgs e)
        {
            EnableLogBtn.IsEnabled = false;
            try
            {
                string err = await Task.Run(() => FirewallService.SetDropLogging(true));
                StatusText.Text = err == null
                    ? "Drop-logging enabled on all profiles — hit counts will appear as blocks fire."
                    : $"Drop-logging partially failed: {err}";
            }
            finally { EnableLogBtn.IsEnabled = true; }
            await ReloadAsync();
        }

        private async void RdapBtn_Click(object sender, RoutedEventArgs e)
        {
            var targets = _rows.Where(r => r.Kind == "ip" && r.Class == "Unclassified").ToList();
            if (targets.Count == 0)
            {
                StatusText.Text = "No unclassified IP targets — everything is already classified or verified.";
                return;
            }
            RdapBtn.IsEnabled = false;
            try
            {
                StatusText.Text = $"RDAP-verifying {targets.Count} target(s)… (cached results are instant)";
                int ok = 0, failed = 0, ms = 0, cached = 0;
                foreach (var r in targets)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var entry = await Services.RdapService.Instance.LookupAsync(r.Ip);
                    sw.Stop();
                    if (sw.ElapsedMilliseconds < 50) cached++;
                    if (entry.IsNegative) failed++;
                    else
                    {
                        ok++;
                        r.OwnerDisplay = $"{entry.Netname} · {(entry.Org ?? "?")} [{entry.Country}]";
                    }
                    ms += (int)sw.ElapsedMilliseconds;
                }
                StatusText.Text = $"RDAP: {ok} verified, {failed} failed · {cached} served from cache · " +
                                  $"total {ms} ms · cache: {Services.RdapService.Instance.EntryCount} block(s) in {Services.RdapService.Instance.CachePath}";
            }
            finally { RdapBtn.IsEnabled = true; }
            await ReloadAsync();
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

        private async void UnblockSelectedBtn_Click(object sender, RoutedEventArgs e)
        {
            var sel = RulesGrid.SelectedItems.Cast<Row>().ToList();
            if (sel.Count == 0) { MessageBox.Show("Select one or more rows first.", "Unblock"); return; }
            if (MessageBox.Show($"Permanently remove the block rules for {sel.Count} target(s)?\n" +
                string.Join("\n", sel.Select(r => $"  · {r.Target} ({r.Class}, {r.Rules} rules)")),
                "Confirm unblock", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            int removed = 0;
            foreach (var r in sel)
                removed += r.Kind == "ip"
                    ? await FirewallService.Instance.RemoveBlockRulesForIpAsync(r.Ip)
                    : await FirewallService.Instance.RemoveBlockRulesForAppAsync(r.AppPath);
            StatusText.Text = $"Removed {removed} firewall rule(s).";
            await ReloadAsync();
        }

        private async void UnblockMsBtn_Click(object sender, RoutedEventArgs e)
        {
            var risky = _rows.Where(r => r.IsRisky).ToList();
            if (risky.Count == 0) return;
            if (MessageBox.Show(
                    $"These {risky.Count} targets are Microsoft / Windows infrastructure — blocking them breaks\n" +
                    "updates, notifications, sign-in or DNS rather than blocking threats.\n\n" +
                    string.Join("\n", risky.Select(r => $"  · {r.Target} ({r.Class}, {r.Rules} rules)")) +
                    "\n\nRemove ALL of them?",
                    "Unblock Microsoft-owned ⚠", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            int removed = 0;
            foreach (var r in risky)
                removed += r.Kind == "ip"
                    ? await FirewallService.Instance.RemoveBlockRulesForIpAsync(r.Ip)
                    : await FirewallService.Instance.RemoveBlockRulesForAppAsync(r.AppPath);
            StatusText.Text = $"Removed {removed} rule(s) blocking Microsoft/Windows infrastructure.";
            await ReloadAsync();
        }

        private async void ConsolidateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(
                    "Legacy IP blocks created 4 rules each (TCP/UDP × in/out). This consolidates each set to the\n" +
                    "current 2-rule scheme (outbound TCP+UDP): inbound pair deleted, outbound pair renamed with\n" +
                    "provenance preserved. No protection is lost — inbound was already covered by stateful filtering.",
                    "Consolidate legacy", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            var (targets, removedIn, migratedOut) = await FirewallService.Instance.ConsolidateLegacyIpBlocksAsync();
            StatusText.Text = $"Consolidated {targets} target(s): {removedIn} redundant inbound rule(s) deleted, {migratedOut} migrated to v2 naming.";
            await ReloadAsync();
        }

        private async void ReenableBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Re-enable ALL AutoCommand block rules? (Undoes 'Restore Internet'.)",
                    "Re-enable", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var res = await FirewallService.Instance.SetAllAutoCommandBlocksEnabledAsync(true);
            StatusText.Text = $"Re-enabled AutoCommand blocks ({res.Changed} rule(s) changed).";
            await ReloadAsync();
        }

        private async void RestoreExceptRiskyBtn_Click(object sender, RoutedEventArgs e)
        {
            var risky = _rows.Where(r => r.IsRisky).ToList();
            var toRestore = _rows.Where(r => !r.IsRisky).ToList();
            if (toRestore.Count == 0) { MessageBox.Show("Nothing to restore — no non-risky blocks.", "Restore"); return; }

            var preview = new System.Text.StringBuilder();
            preview.AppendLine($"Restore {toRestore.Count} target(s) — everything EXCEPT these {risky.Count}, which stay");
            preview.AppendLine("disabled because blocking them breaks Windows rather than threats:");
            foreach (var r in risky.Take(12)) preview.AppendLine($"  · {r.Target}  ({r.Class})");
            if (risky.Count > 12) preview.AppendLine($"  … and {risky.Count - 12} more");
            preview.AppendLine().AppendLine("Kept-disabled targets remain listed (red rows) — re-enable");
            preview.AppendLine("individually anytime if you truly want them blocked.");
            if (MessageBox.Show(preview.ToString(), "Restore except Microsoft/Windows ⚠",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            var res = await FirewallService.Instance.SetAllAutoCommandBlocksEnabledAsync(true, skipRisky: true);
            StatusText.Text = $"Restored {res.Changed} rule(s); kept {res.SkippedRisky} risky rule(s) disabled.";
            await ReloadAsync();
        }

        private void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"blocked-rules-{DateTime.Now:yyyyMMdd-HHmm}.csv"
            };
            if (dlg.ShowDialog() != true) return;
            var lines = new List<string> { "Target,Kind,Class,Rules,Enabled,Created,Hits10m,Description" };
            foreach (var r in _rows)
                lines.Add($"{r.Target},{r.Kind},\"{r.Class}\",{r.Rules},{r.EnabledRules}," +
                          $"\"{r.CreatedDisplay}\",{r.Hits},\"{(r.Description ?? "").Replace("\"", "'")}\"");
            File.WriteAllLines(dlg.FileName, lines);
            StatusText.Text = $"Exported {_rows.Count} rows → {dlg.FileName}";
        }
    }
}
