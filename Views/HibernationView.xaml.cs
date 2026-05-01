using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoCommand.Helpers;

namespace AutoCommand.Views
{
    public partial class HibernationView : UserControl
    {
        public HibernationView()
        {
            InitializeComponent();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await CheckHibernateStatus();
        }

        private async Task CheckHibernateStatus()
        {
            string output = await ProcessRunner.RunAsync("powercfg", "/a");

            bool hibernateAvailable = output.Contains("Hibernate") && !output.Contains("Hibernate is not available");
            bool hibernateEnabled = !output.Contains("Hibernation has not been enabled") && hibernateAvailable;

            Dispatcher.Invoke(() =>
            {
                if (hibernateEnabled)
                {
                    HibernateStatusText.Text = "⚠ Hibernate is ENABLED — hiberfile exists on disk";
                    HibernateStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xA6, 0x31));
                }
                else
                {
                    HibernateStatusText.Text = "✓ Hibernate is disabled";
                    HibernateStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F));
                }

                // Show all available power states
                PowerStatesText.Text = output.Trim();
            });
        }

        private async void DisableHibernateBtn_Click(object sender, RoutedEventArgs e)
        {
            await ProcessRunner.RunAsync("powercfg", "/hibernate off");
            await CheckHibernateStatus();
        }

        private async void EnableHibernateBtn_Click(object sender, RoutedEventArgs e)
        {
            await ProcessRunner.RunAsync("powercfg", "/hibernate on");
            await CheckHibernateStatus();
        }

        private void TrueShutdownBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Perform a true shutdown? All unsaved work will be lost.",
                "Confirm Shutdown", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            ProcessRunner.RunDetached("shutdown", "/s /t 0");
        }
    }
}

