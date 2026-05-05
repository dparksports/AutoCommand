using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using AutoCommand.Helpers;

namespace AutoCommand.Services
{
    public class SysmonInstallerService
    {
        private const string SysmonDownloadUrl = "https://live.sysinternals.com/Sysmon64.exe";
        private const string SysmonExeName = "Sysmon64.exe";
        private const string ConfigFileName = "sysmon_config.xml";

        public bool IsSysmonInstalled()
        {
            // Check if the Sysmon service exists and the binary is on disk
            string system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return File.Exists(Path.Combine(system32, "Sysmon64.exe"));
        }

        public async Task<(bool Success, string ErrorMessage)> InstallAndConfigureAsync()
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "AutoCommand_Sysmon");
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

                string exePath = Path.Combine(tempDir, SysmonExeName);
                string configPath = Path.Combine(tempDir, ConfigFileName);

                // 1. Download Sysmon64
                using (var client = new HttpClient())
                {
                    var data = await client.GetByteArrayAsync(SysmonDownloadUrl);
                    await File.WriteAllBytesAsync(exePath, data);
                }

                // 2. Generate optimized config (Enable Network Connect - Event ID 3)
                // We exclude common noise to keep the log clean
                string configXml = @"
<Sysmon schemaversion=""4.82"">
  <EventFiltering>
    <RuleGroup name="""" groupRelation=""or"">
      <NetworkConnect onmatch=""include"">
        <Rule groupRelation=""and"">
          <Image condition=""not end with"">browser.exe</Image> <!-- Example exclusion -->
        </Rule>
      </NetworkConnect>
    </RuleGroup>
  </EventFiltering>
</Sysmon>";
                await File.WriteAllTextAsync(configPath, configXml);

                // 3. Install/Update Sysmon
                // -i: Install, -accepteula: self-explanatory, -c: use config
                var (output, error, exitCode) = await ProcessRunner.RunWithDetailsAsync(exePath, $"-i \"{configPath}\" -accepteula");
                
                if (exitCode == 0)
                    return (true, string.Empty);
                else
                    return (false, $"Exit code: {exitCode}. Output: {output}. Error: {error}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SysmonInstallerService Error: {ex.Message}");
                return (false, ex.Message);
            }
        }
    }
}
