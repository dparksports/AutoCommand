using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using AutoCommand.Services;

namespace AutoCommand.Views
{
    public partial class AiSetupView : UserControl
    {
        private readonly string _keyPath = "gemini_api_key.txt";
        private readonly string _modelPrefsPath = "gemini_model_prefs.txt";

        public AiSetupView()
        {
            InitializeComponent();
            LoadExistingConfig();
        }

        private void LoadExistingConfig()
        {
            if (File.Exists(_keyPath))
            {
                ApiKeyBox.Text = File.ReadAllText(_keyPath).Trim();
            }

            if (File.Exists(_modelPrefsPath))
            {
                var prefs = File.ReadAllLines(_modelPrefsPath);
                if (prefs.Length >= 2)
                {
                    string target = prefs[1].Trim();
                    foreach (ComboBoxItem item in CloudModelCombo.Items)
                    {
                        if (item.Content.ToString() == target)
                        {
                            item.IsSelected = true;
                            break;
                        }
                    }
                }
            }

            // Initialize Local Setup
            ModelsDirBox.Text = $"{LocalLlmManager.SharedModelsDirectory}\n{LocalLlmManager.GoogleClawModelsDirectory}\n{LocalLlmManager.HuggingFaceCacheDirectory}";
            LocalBackendCombo.SelectedIndex = LocalLlmManager.UseCuda ? 0 : 1;
            
            // Set context combo based on current manager value
            uint ctx = LocalLlmManager.ContextSize;
            if (ctx >= 32768) LocalContextCombo.SelectedIndex = 3;
            else if (ctx >= 16384) LocalContextCombo.SelectedIndex = 2;
            else if (ctx >= 8192) LocalContextCombo.SelectedIndex = 1;
            else LocalContextCombo.SelectedIndex = 0;

            RefreshLocalModels();
        }

        private void CloudOption_Click(object sender, RoutedEventArgs e)
        {
            string key = ApiKeyBox.Text.Trim();
            if (string.IsNullOrEmpty(key))
            {
                MessageBox.Show("Please enter a valid Gemini API Key first.\n\nYou can get one for free at aistudio.google.com.", "API Key Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            File.WriteAllText(_keyPath, key);

            string selectedModelName = GetSelectedCloudModel();
            File.WriteAllLines(_modelPrefsPath, new[] { "Cloud", selectedModelName });

            MessageBox.Show("Cloud AI configuration saved!", "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private string GetSelectedCloudModel()
        {
            var selected = CloudModelCombo.SelectedItem;
            if (selected is ComboBoxItem cbi) return cbi.Content.ToString();
            if (selected is string s) return s;
            return "gemini-2.5-flash";
        }

        private void LocalOption_Click(object sender, RoutedEventArgs e)
        {
            if (LocalModelCombo.SelectedItem == null)
            {
                MessageBox.Show("Please select a .gguf model file or download one first.", "Model Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedModelName = LocalModelCombo.SelectedItem.ToString();
            File.WriteAllLines(_modelPrefsPath, new[] { "Local", selectedModelName });

            MessageBox.Show("Local AI configuration saved!", "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LocalBackendCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LocalBackendCombo != null)
            {
                LocalLlmManager.UseCuda = LocalBackendCombo.SelectedIndex == 0;
            }
        }

        private void LocalContextCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LocalContextCombo != null)
            {
                switch (LocalContextCombo.SelectedIndex)
                {
                    case 0: LocalLlmManager.ContextSize = 4096; break;
                    case 1: LocalLlmManager.ContextSize = 8192; break;
                    case 2: LocalLlmManager.ContextSize = 16384; break;
                    case 3: LocalLlmManager.ContextSize = 32768; break;
                }
            }
        }

        private void RefreshLocalModelsBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshLocalModels();
        }

        public void RefreshLocalModels()
        {
            var models = LocalLlmManager.GetAvailableModels();
            
            LocalModelCombo.ItemsSource = models;
            if (models.Count > 0)
            {
                // Try to preserve selection if it still exists
                string currentTarget = "";
                if (File.Exists(_modelPrefsPath))
                {
                    var lines = File.ReadAllLines(_modelPrefsPath);
                    if (lines.Length >= 2 && lines[0] == "Local")
                    {
                        currentTarget = lines[1].Trim();
                    }
                }

                int idx = models.IndexOf(currentTarget);
                LocalModelCombo.SelectedIndex = idx >= 0 ? idx : 0;
            }
            else
            {
                LocalModelCombo.ItemsSource = new[] { "No .gguf files found in Models Directory" };
                LocalModelCombo.SelectedIndex = 0;
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private async void RefreshCloudModelsBtn_Click(object sender, RoutedEventArgs e)
        {
            // Auto-save the API key if typed before refreshing
            string key = ApiKeyBox.Text.Trim();
            if (!string.IsNullOrEmpty(key)) File.WriteAllText(_keyPath, key);

            RefreshCloudModelsBtn.IsEnabled = false;
            RefreshCloudModelsBtn.Content = "⏳";
            
            var svc = new GeminiAssistantService();
            var models = await svc.GetCloudModelsAsync();
            
            if (models.Count > 0)
            {
                CloudModelCombo.Items.Clear();
                foreach(var m in models) CloudModelCombo.Items.Add(m);
                CloudModelCombo.SelectedIndex = 0;
            }
            else
            {
                MessageBox.Show("Could not fetch models. Please check your API key.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            
            RefreshCloudModelsBtn.IsEnabled = true;
            RefreshCloudModelsBtn.Content = "🔄 Refresh";
        }
    }
}
