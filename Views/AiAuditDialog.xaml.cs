using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AutoCommand.Services;

namespace AutoCommand.Views
{
    public partial class AiAuditDialog : Window
    {
        private readonly string _contextData;
        private readonly bool _useLocalLlm;
        private readonly string _targetModel;
        private readonly GeminiAssistantService _aiService;
        private readonly AiAuditResult _preComputedResult;

        // Standard constructor — runs a fresh analysis
        public AiAuditDialog(string contextData, bool useLocalLlm, string targetModel)
        {
            InitializeComponent();
            _contextData = contextData;
            _useLocalLlm = useLocalLlm;
            _targetModel = targetModel;
            _aiService = new GeminiAssistantService();
        }

        // Pre-computed constructor — shows cached result instantly, no API call
        public AiAuditDialog(AiAuditResult preComputedResult)
        {
            InitializeComponent();
            _preComputedResult = preComputedResult;
            _aiService = null;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // If opened from proactive cache, show instantly without an API call
            if (_preComputedResult != null)
            {
                DisplayResult(_preComputedResult);
                return;
            }
            try
            {
                var result = await _aiService.AnalyzeContextAsync(_contextData, _useLocalLlm, _targetModel);
                DisplayResult(result);
            }
            catch (Exception ex)
            {
                DisplayError(ex.Message);
            }
        }

        private void DisplayResult(AiAuditResult result)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ResultsPanel.Visibility = Visibility.Visible;

            SummaryText.Text = result.Summary;
            DetailsText.Text = result.Details;

            string level = result.ThreatLevel?.ToLower() ?? "unknown";
            
            if (level.Contains("safe"))
            {
                ThreatIcon.Text = "🟢";
                ThreatLevelText.Text = "Safe";
                ThreatLevelText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4ADE80"));
                ThreatBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#14532D"));
                ThreatBorder.BorderThickness = new Thickness(1);
            }
            else if (level.Contains("caution") || level.Contains("warning"))
            {
                ThreatIcon.Text = "🟡";
                ThreatLevelText.Text = "Caution";
                ThreatLevelText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FACC15"));
                ThreatBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#713F12"));
                ThreatBorder.BorderThickness = new Thickness(1);
            }
            else if (level.Contains("danger") || level.Contains("critical"))
            {
                ThreatIcon.Text = "🔴";
                ThreatLevelText.Text = "Danger";
                ThreatLevelText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171"));
                ThreatBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F1D1D"));
                ThreatBorder.BorderThickness = new Thickness(1);
            }
            else
            {
                ThreatIcon.Text = "⚪";
                ThreatLevelText.Text = "Unknown";
                ThreatLevelText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
            }
            StartLocalEngineBtn.Visibility = Visibility.Collapsed;
        }

        private void DisplayError(string errorMessage)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ResultsPanel.Visibility = Visibility.Visible;

            ThreatIcon.Text = "❌";
            ThreatLevelText.Text = "Analysis Error";
            ThreatLevelText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171"));
            
            if (_useLocalLlm && (errorMessage.Contains("No connection could be made") || errorMessage.Contains("refused")))
            {
                SummaryText.Text = "The Local AI Engine is not currently running.";
                DetailsText.Text = "You have selected 'Local AI' for maximum privacy, but the local engine (local_llm_manager.py) is offline. Please start it using the button below, wait a few moments for the model to load, and then try your audit again.";
                StartLocalEngineBtn.Visibility = Visibility.Visible;
            }
            else if (!_useLocalLlm && (errorMessage.Contains("400") || errorMessage.Contains("403") || errorMessage.Contains("API key") || errorMessage.Contains("Unauthorized")))
            {
                SummaryText.Text = "Invalid or Missing API Key.";
                DetailsText.Text = "The Google Gemini API rejected the request. Please make sure your API key is correct. You can update it by clicking the ⚙️ Settings icon next to the AI Audit button in the Command Panel.\n\nError: " + errorMessage;
                StartLocalEngineBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                SummaryText.Text = "Failed to communicate with the AI engine.";
                DetailsText.Text = errorMessage;
                StartLocalEngineBtn.Visibility = Visibility.Collapsed;
            }
        }

        private void StartLocalEngineBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c start \"Local AI Engine\" python \"C:\\Users\\honey\\googleclaw\\local_llm_manager.py\" start",
                    UseShellExecute = true
                });
                DetailsText.Text += "\n\n🚀 Starting Local AI Engine... Please wait a moment for the console window to appear and load the model, then close this dialog and click '✨ AI Security Audit' again.";
                StartLocalEngineBtn.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch engine: {ex.Message}");
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
