using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Management.Deployment;
using AutoCommand.Models;

namespace AutoCommand.Services
{
    public class AppManagerService
    {
        /// <summary>
        /// Lists installed apps the way Windows Settings → Apps → Installed apps does:
        /// user-visible main packages (frameworks, resource bundles, partially staged
        /// packages and OS system components are hidden). Set includeSystemComponents
        /// to true to also show System-signed OS components.
        /// </summary>
        public async Task<List<AppPackageItem>> GetInstalledAppsAsync(bool includeSystemComponents = false)
        {
            return await Task.Run(() =>
            {
                var items = new List<AppPackageItem>();
                try
                {
                    var packageManager = new PackageManager();
                    // Empty string targets the current user's packages
                    var packages = packageManager.FindPackagesForUser(string.Empty);

                    foreach (var p in packages)
                    {
                        try
                        {
                            if (p.IsFramework || p.IsResourcePackage) continue;

                            bool isSystem = p.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System;
                            if (isSystem && !includeSystemComponents) continue;

                            items.Add(MapPackage(p));
                        }
                        catch
                        {
                            // Skip packages whose metadata cannot be read
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AppManagerService Error: {ex.Message}");
                }

                return items.OrderBy(i => i.Name).ToList();
            });
        }

        /// <summary>
        /// Finds installed packages whose package name or display name contains any of the
        /// given patterns (case-insensitive) — e.g. Outlook, Xbox, Family, Phone.
        /// </summary>
        public async Task<List<AppPackageItem>> FindBloatwareAsync(IEnumerable<string> patterns)
        {
            var patternList = patterns?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            if (patternList.Count == 0) return new List<AppPackageItem>();

            return await Task.Run(() =>
            {
                var matches = new List<AppPackageItem>();
                try
                {
                    var packageManager = new PackageManager();
                    foreach (var p in packageManager.FindPackagesForUser(string.Empty))
                    {
                        try
                        {
                            if (p.IsFramework || p.IsResourcePackage) continue;

                            // System-signed OS components (e.g. XboxGameCallableUI) are
                            // non-removable — never offer them for removal
                            if (p.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System) continue;

                            string pkgName = p.Id.Name ?? "";
                            string displayName = "";
                            try { displayName = p.DisplayName ?? ""; } catch { }

                            bool hit = patternList.Any(pattern =>
                                pkgName.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                                displayName.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                            if (!hit) continue;

                            matches.Add(MapPackage(p));
                        }
                        catch
                        {
                            // Skip packages whose metadata cannot be read
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"FindBloatwareAsync Error: {ex.Message}");
                }

                return matches.OrderBy(i => i.Name).ToList();
            });
        }

        private static AppPackageItem MapPackage(Windows.ApplicationModel.Package p)
        {
            string installPath = "Unknown";
            try { installPath = p.InstalledLocation.Path; } catch { }

            bool isSystem = p.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System;

            return new AppPackageItem
            {
                Name = string.IsNullOrEmpty(p.DisplayName) ? p.Id.Name : p.DisplayName,
                FullName = p.Id.FullName,
                Version = $"{p.Id.Version.Major}.{p.Id.Version.Minor}.{p.Id.Version.Build}.{p.Id.Version.Revision}",
                Publisher = p.Id.Publisher,
                InstallLocation = installPath,
                SignatureStatus = DescribeSignature(p.SignatureKind),
                SignerCertificate = ExtractCommonName(p.Id.Publisher),
                IsSystem = isSystem
            };
        }

        /// <summary>
        /// The deployment engine validates a package's signature at install time, so
        /// SignatureKind is authoritative. (Parsing AppxSignature.p7x directly is not
        /// viable — it's a PKCS#7 blob, not a DER certificate.)
        /// </summary>
        private static string DescribeSignature(Windows.ApplicationModel.PackageSignatureKind kind)
        {
            return kind switch
            {
                Windows.ApplicationModel.PackageSignatureKind.System => "System component",
                Windows.ApplicationModel.PackageSignatureKind.Store => "Valid (Store)",
                Windows.ApplicationModel.PackageSignatureKind.Enterprise => "Valid (Enterprise)",
                Windows.ApplicationModel.PackageSignatureKind.Developer => "Developer-signed",
                _ => "Unsigned"
            };
        }

        /// <summary>
        /// Extracts the CN= component from a package publisher string, e.g.
        /// "CN=Microsoft Corporation, O=Microsoft Corporation, ..." → "Microsoft Corporation".
        /// </summary>
        private static string ExtractCommonName(string publisher)
        {
            if (string.IsNullOrWhiteSpace(publisher)) return "Unknown";
            if (!publisher.StartsWith("CN=")) return publisher;

            string cn = publisher.Substring(3);
            int comma = cn.IndexOf(',');
            if (comma >= 0) cn = cn.Substring(0, comma);
            return cn.Trim().Trim('"');
        }

        public async Task<bool> UninstallAppAsync(string fullName)
        {
            try
            {
                var packageManager = new PackageManager();
                // Execute the native removal command
                var deploymentOperation = packageManager.RemovePackageAsync(fullName);

                // Await completion natively without spinning up external processes
                var tcs = new TaskCompletionSource<bool>();
                deploymentOperation.Completed = (info, status) =>
                {
                    if (status == Windows.Foundation.AsyncStatus.Completed)
                        tcs.SetResult(true);
                    else
                        tcs.SetResult(false);
                };

                return await tcs.Task;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UninstallAppAsync Error: {ex.Message}");
                return false;
            }
        }
    }
}
