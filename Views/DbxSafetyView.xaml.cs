using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoCommand.Helpers;

namespace AutoCommand.Views
{
    public partial class DbxSafetyView : UserControl
    {
        private string _efiDriveLetter = "Z:";
        private string _downloadedDbxPath;
        private readonly string _baselinePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AutoCommand", "efi_baseline.json");

        public DbxSafetyView()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            Log("DBX Safety Check ready. Click 'Run Full Check' to begin.");
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogOutput.AppendText(message + Environment.NewLine);
                LogOutput.ScrollToEnd();
            });
        }

        // ═══════════════════════════════════════
        //  FULL CHECK PIPELINE
        // ═══════════════════════════════════════
        private async void RunCheckBtn_Click(object sender, RoutedEventArgs e)
        {
            RunCheckBtn.IsEnabled = false;
            LogOutput.Clear();
            Log("Starting full DBX safety check...\n");

            try
            {
                // Step 1: Mount EFI
                bool mounted = await MountEfiPartition();
                if (!mounted)
                {
                    SetStatus(EfiStatusText, "✗ Failed to mount EFI partition", false);
                    return;
                }
                SetStatus(EfiStatusText, $"✓ EFI partition mounted at {_efiDriveLetter}", true);

                // Step 2: Check baseline
                await CheckEfiBaseline();

                // Step 3: Sigcheck (if available)
                await AnalyzeBootloader();

                // Step 4: DBX revocation check
                await CheckDbxSafety();
            }
            catch (Exception ex)
            {
                Log($"\n[ERROR] {ex.Message}");
            }
            finally
            {
                // Unmount EFI
                await UnmountEfiPartition();
                RunCheckBtn.IsEnabled = true;
            }
        }

        // ═══════════════════════════════════════
        //  EFI PARTITION MOUNT
        // ═══════════════════════════════════════
        private async Task<bool> MountEfiPartition()
        {
            Log("[1/4] Mounting EFI partition...");

            // Try to unmount first (in case it's stale)
            await ProcessRunner.RunAsync("mountvol", $"{_efiDriveLetter} /D");
            await Task.Delay(500);

            var result = await ProcessRunner.RunWithDetailsAsync("mountvol", $"{_efiDriveLetter} /S");

            if (result.ExitCode != 0)
            {
                Log($"  mountvol failed: {result.Error}");
                // Try alternate drive letter
                _efiDriveLetter = "Y:";
                await ProcessRunner.RunAsync("mountvol", $"{_efiDriveLetter} /D");
                await Task.Delay(500);
                result = await ProcessRunner.RunWithDetailsAsync("mountvol", $"{_efiDriveLetter} /S");
                if (result.ExitCode != 0)
                {
                    Log($"  Alternate mount also failed: {result.Error}");
                    return false;
                }
            }

            Log($"  Mounted EFI System Partition at {_efiDriveLetter}");
            return true;
        }

        private async Task UnmountEfiPartition()
        {
            await ProcessRunner.RunAsync("mountvol", $"{_efiDriveLetter} /D");
            Log("\nEFI partition unmounted.");
        }

        // ═══════════════════════════════════════
        //  INTEGRITY BASELINE
        // ═══════════════════════════════════════
        private async Task CheckEfiBaseline()
        {
            Log("\n[2/4] Checking EFI integrity baseline...");

            string efiBootDir = Path.Combine(_efiDriveLetter + "\\", "EFI", "Microsoft", "Boot");
            if (!Directory.Exists(efiBootDir))
            {
                efiBootDir = Path.Combine(_efiDriveLetter + "\\", "EFI", "Boot");
            }

            if (!Directory.Exists(efiBootDir))
            {
                SetStatus(BaselineStatusText, "✗ EFI boot directory not found", false);
                Log("  Boot directory not found.");
                return;
            }

            // Compute hashes of critical files
            var currentHashes = new Dictionary<string, string>();
            await Task.Run(() =>
            {
                try
                {
                    var files = Directory.GetFiles(efiBootDir, "*.*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".efi", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

                    using var sha = SHA256.Create();
                    foreach (var file in files)
                    {
                        try
                        {
                            var bytes = File.ReadAllBytes(file);
                            var hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
                            string relPath = file.Substring(_efiDriveLetter.Length);
                            currentHashes[relPath] = hash;
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    Log($"  Error scanning: {ex.Message}");
                }
            });

            Log($"  Scanned {currentHashes.Count} EFI files.");

            // Load or create baseline
            if (File.Exists(_baselinePath))
            {
                try
                {
                    string json = File.ReadAllText(_baselinePath);
                    var baseline = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                    int changed = 0, added = 0, removed = 0;
                    foreach (var kvp in currentHashes)
                    {
                        if (!baseline.ContainsKey(kvp.Key)) { added++; Log($"  [NEW] {kvp.Key}"); }
                        else if (baseline[kvp.Key] != kvp.Value) { changed++; Log($"  [CHANGED] {kvp.Key}"); }
                    }
                    foreach (var kvp in baseline)
                    {
                        if (!currentHashes.ContainsKey(kvp.Key)) { removed++; Log($"  [REMOVED] {kvp.Key}"); }
                    }

                    if (changed == 0 && added == 0 && removed == 0)
                    {
                        SetStatus(BaselineStatusText, $"✓ All {currentHashes.Count} files match baseline — no tampering detected", true);
                    }
                    else
                    {
                        SetStatus(BaselineStatusText, $"⚠ Drift detected: {changed} changed, {added} new, {removed} removed", false);
                    }
                }
                catch
                {
                    SetStatus(BaselineStatusText, "✗ Failed to read baseline", false);
                }
            }
            else
            {
                // Create baseline
                try
                {
                    string dir = Path.GetDirectoryName(_baselinePath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(_baselinePath, JsonSerializer.Serialize(currentHashes, new JsonSerializerOptions { WriteIndented = true }));
                    SetStatus(BaselineStatusText, $"✓ Baseline created with {currentHashes.Count} files. Re-run to verify.", true);
                    Log("  New baseline saved.");
                }
                catch (Exception ex)
                {
                    SetStatus(BaselineStatusText, $"✗ Failed to save baseline: {ex.Message}", false);
                }
            }
        }

        // ═══════════════════════════════════════
        //  SIGCHECK ANALYSIS
        // ═══════════════════════════════════════
        private async Task AnalyzeBootloader()
        {
            Log("\n[3/4] Analyzing bootloader signature...");

            // Look for sigcheck64.exe in common locations
            string sigcheckPath = FindSigcheck();
            if (sigcheckPath == null)
            {
                SetStatus(SigcheckStatusText, "⚠ sigcheck64.exe not found — skipping Authenticode verification", false);
                Log("  sigcheck64.exe not found. Place it in the AutoCommand directory or system PATH.");
                return;
            }

            string bootmgfwPath = Path.Combine(_efiDriveLetter + "\\", "EFI", "Microsoft", "Boot", "bootmgfw.efi");
            if (!File.Exists(bootmgfwPath))
            {
                bootmgfwPath = Path.Combine(_efiDriveLetter + "\\", "EFI", "Boot", "bootx64.efi");
            }

            if (!File.Exists(bootmgfwPath))
            {
                SetStatus(SigcheckStatusText, "✗ Boot manager EFI file not found", false);
                return;
            }

            // Run sigcheck
            string output = await ProcessRunner.RunAsync(sigcheckPath, $"-accepteula -nobanner \"{bootmgfwPath}\"");
            Log($"  Sigcheck output:\n{output}");

            bool verified = output.Contains("Verified") && output.Contains("Signed");
            if (verified && !output.Contains("not verified", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(SigcheckStatusText, "✓ Bootloader signature is valid (Authenticode verified)", true);
            }
            else
            {
                SetStatus(SigcheckStatusText, "⚠ Bootloader signature could not be verified", false);
            }

            // Extract PE hash for DBX comparison
            string peHash = ExtractHash(output, "PE256");
            string sha256Hash = ExtractHash(output, "SHA256");
            if (!string.IsNullOrEmpty(peHash))
                Log($"  PE256 Hash: {peHash}");
            if (!string.IsNullOrEmpty(sha256Hash))
                Log($"  SHA256 Hash: {sha256Hash}");
        }

        private string FindSigcheck()
        {
            var searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sigcheck64.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sysinternals", "sigcheck64.exe"),
                @"C:\Users\honey\SystemMonitor\DeviceMonitorCS\sigcheck64.exe"
            };

            foreach (var p in searchPaths)
            {
                if (File.Exists(p)) return p;
            }

            // Try PATH
            try
            {
                var result = ProcessRunner.RunWithDetails("where", "sigcheck64.exe");
                if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output))
                    return result.Output.Trim().Split('\n')[0].Trim();
            }
            catch { }

            return null;
        }

        private string ExtractHash(string output, string hashType)
        {
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith(hashType + ":", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring(hashType.Length + 1).Trim();
                }
            }
            return null;
        }

        // ═══════════════════════════════════════
        //  DBX REVOCATION CHECK
        // ═══════════════════════════════════════
        private async Task CheckDbxSafety()
        {
            Log("\n[4/4] Checking DBX revocation list...");

            string dbxPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DBXUpdate.bin");
            if (!File.Exists(dbxPath))
            {
                dbxPath = Path.Combine(Path.GetTempPath(), "DBXUpdate.bin");
            }

            if (!File.Exists(dbxPath))
            {
                SetStatus(DbxStatusText, "⚠ DBXUpdate.bin not found — download it first", false);
                Log("  No DBX update file found. Click 'Download' to get the latest.");
                return;
            }

            await Task.Run(() =>
            {
                try
                {
                    byte[] dbxData = File.ReadAllBytes(dbxPath);
                    Log($"  DBXUpdate.bin loaded: {dbxData.Length} bytes");

                    // Parse EFI_SIGNATURE_LIST headers
                    int offset = 0;
                    int sigCount = 0;
                    int listCount = 0;
                    var sha256Guid = new Guid("c1c41626-504c-4092-aca9-41f936934328");

                    while (offset + 28 <= dbxData.Length)
                    {
                        // Read SignatureType GUID (16 bytes)
                        byte[] guidBytes = new byte[16];
                        Array.Copy(dbxData, offset, guidBytes, 0, 16);
                        Guid sigType = new Guid(guidBytes);

                        // Read ListSize (4 bytes at offset+16)
                        uint listSize = BitConverter.ToUInt32(dbxData, offset + 16);
                        // Read HeaderSize (4 bytes at offset+20)
                        uint headerSize = BitConverter.ToUInt32(dbxData, offset + 20);
                        // Read SignatureSize (4 bytes at offset+24)
                        uint sigSize = BitConverter.ToUInt32(dbxData, offset + 24);

                        if (listSize == 0 || listSize > dbxData.Length) break;

                        listCount++;
                        if (sigSize > 0)
                        {
                            uint dataSize = listSize - 28 - headerSize;
                            int sigs = (int)(dataSize / sigSize);
                            sigCount += sigs;

                            string typeStr = sigType == sha256Guid ? "SHA256" : sigType.ToString();
                            Log($"  List #{listCount}: {typeStr} — {sigs} signatures (entry size: {sigSize} bytes)");
                        }

                        offset += (int)listSize;
                    }

                    Log($"\n  Total: {listCount} signature lists, {sigCount} revocation entries");

                    Dispatcher.Invoke(() =>
                    {
                        SetStatus(DbxStatusText,
                            $"✓ DBX parsed: {sigCount} revocation signatures across {listCount} lists",
                            true);
                    });
                }
                catch (Exception ex)
                {
                    Log($"  [ERROR] DBX parse failed: {ex.Message}");
                    Dispatcher.Invoke(() =>
                    {
                        SetStatus(DbxStatusText, $"✗ DBX parse error: {ex.Message}", false);
                    });
                }
            });
        }

        // ═══════════════════════════════════════
        //  DBX UPDATE DOWNLOAD / INSTALL
        // ═══════════════════════════════════════
        private async void DownloadDbxBtn_Click(object sender, RoutedEventArgs e)
        {
            DownloadDbxBtn.IsEnabled = false;
            DbxUpdateStatusText.Text = "Downloading...";
            Log("\nDownloading latest DBX update from Microsoft...");

            var result = await DbxRemediator.DownloadUpdateAsync();
            if (result.Error != null)
            {
                DbxUpdateStatusText.Text = $"✗ Download failed: {result.Error}";
                Log($"  Download failed: {result.Error}");
            }
            else
            {
                _downloadedDbxPath = result.Path;
                DbxUpdateStatusText.Text = $"✓ Downloaded. SHA256: {result.Checksum.Substring(0, 16)}...";
                Log($"  Downloaded to: {result.Path}");
                Log($"  SHA256: {result.Checksum}");
                InstallDbxBtn.IsEnabled = true;
            }
            DownloadDbxBtn.IsEnabled = true;
        }

        private async void InstallDbxBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_downloadedDbxPath))
            {
                MessageBox.Show("Download the update first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show(
                "Apply DBX update to UEFI firmware?\n\nThis modifies the Secure Boot revocation database. " +
                "Make sure your bootloader is not in the revocation list, or your system may not boot.",
                "CRITICAL WARNING", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            Log("\nApplying DBX update...");
            var result = await DbxRemediator.InstallUpdateAsync(_downloadedDbxPath);
            if (result.Success)
            {
                DbxUpdateStatusText.Text = "✓ DBX update applied successfully. Reboot required.";
                Log("  DBX update applied. Please reboot.");
            }
            else
            {
                DbxUpdateStatusText.Text = $"✗ Install failed: {result.Error}";
                Log($"  Install failed: {result.Error}");
            }
        }

        private async void RepairBootBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(
                "Repair bootloader? This will run:\n  bcdboot C:\\Windows /s Z: /f UEFI\n\nUse if bootloader is corrupted.",
                "Confirm Boot Repair", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            Log("\nRepairing bootloader...");

            // Need EFI mounted
            bool mounted = await MountEfiPartition();
            if (!mounted)
            {
                Log("  Failed to mount EFI for boot repair.");
                return;
            }

            var result = await ProcessRunner.RunWithDetailsAsync("bcdboot",
                $@"C:\Windows /s {_efiDriveLetter} /f UEFI");

            if (result.ExitCode == 0)
            {
                Log("  Boot repair completed successfully.");
                MessageBox.Show("Boot repair completed.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                Log($"  Boot repair failed: {result.Error}");
                MessageBox.Show($"Boot repair failed:\n{result.Error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            await UnmountEfiPartition();
        }

        // ═══════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════
        private void SetStatus(TextBlock target, string text, bool isGood)
        {
            Dispatcher.Invoke(() =>
            {
                target.Text = text;
                target.Foreground = isGood
                    ? new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F))
                    : new SolidColorBrush(Color.FromRgb(0xE5, 0xA6, 0x31));
            });
        }
    }
}

