using System.Windows;

namespace AutoCommand
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Handle UI thread exceptions
            this.DispatcherUnhandledException += (s, args) =>
            {
                LogCrash(args.Exception, "Dispatcher");
                args.Handled = true; // Prevent app from closing
                MessageBox.Show($"A UI error occurred: {args.Exception.Message}\n\nDetails saved to crash.log.", 
                    "AutoCommand Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            // Handle background thread exceptions
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is System.Exception ex)
                {
                    LogCrash(ex, "AppDomain");
                }
            };

            // Handle async Task exceptions
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                LogCrash(args.Exception, "TaskScheduler");
                args.SetObserved();
            };
        }

        private void LogCrash(System.Exception ex, string source)
        {
            try
            {
                string logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                string logEntry = $"\n[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]\n{ex.ToString()}\n";
                System.IO.File.AppendAllText(logPath, logEntry);
            }
            catch { /* Failsafe */ }
        }
    }
}

