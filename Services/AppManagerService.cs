using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Windows.Management.Deployment;
using AutoCommand.Models;

namespace AutoCommand.Services
{
    public class AppManagerService
    {
        public async Task<List<AppPackageItem>> GetInstalledAppsAsync()
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
                        if (p.IsFramework || p.IsResourcePackage) continue;

                        string installPath = "Unknown";
                        try { installPath = p.InstalledLocation.Path; } catch { }

                        string signatureStatus = p.SignatureKind.ToString();
                        string signerSubject = "None";

                        if (installPath != "Unknown")
                        {
                            // Try to read Authenticode details natively from the package's signature block
                            string p7xPath = Path.Combine(installPath, "AppxSignature.p7x");
                            if (File.Exists(p7xPath))
                            {
                                try
                                {
                                    var cert = new X509Certificate2(p7xPath);
                                    signerSubject = cert.Subject;
                                    // If we can read the cert, it's validly structured
                                    signatureStatus = "Valid"; 
                                }
                                catch
                                {
                                    signatureStatus = "Invalid/Error";
                                }
                            }
                        }

                        items.Add(new AppPackageItem
                        {
                            Name = p.Id.Name,
                            FullName = p.Id.FullName,
                            Version = $"{p.Id.Version.Major}.{p.Id.Version.Minor}.{p.Id.Version.Build}.{p.Id.Version.Revision}",
                            Publisher = p.Id.Publisher,
                            InstallLocation = installPath,
                            SignatureStatus = signatureStatus,
                            SignerCertificate = signerSubject,
                            IsSystem = (p.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System)
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AppManagerService Error: {ex.Message}");
                }
                
                return items.OrderBy(i => i.Name).ToList();
            });
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
