using System;
using System.Threading.Tasks;

namespace AutoCommand.Sdk
{
    public interface IPlugin
    {
        string Name { get; }
        string Description { get; }
        string Author { get; }
        string Version { get; }

        Task InitializeAsync(IPluginContext context);
        Task ExecuteAsync();
    }

    public interface IPluginContext
    {
        void Log(string message);
        void LogError(string message, Exception ex = null);
        Task<string> RunCommandAsync(string fileName, string arguments);
        // Add more useful methods for plugins here
    }
}
