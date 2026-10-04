using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AutoCommand.Helpers;
using AutoCommand.Models;
using AutoCommand.Services;

namespace AutoCommand.Views
{
    /// <summary>
    /// Dedicated tab for the Gemini / local-model command assistant.
    /// </summary>
    public partial class GeminiAssistantView : UserControl, IAiAuditable
    {
        public GeminiAssistantView()
        {
            InitializeComponent();
        }

        public string GetAuditContext()
        {
            string log = AiChatLog?.Text;
            if (string.IsNullOrWhiteSpace(log)) return "No assistant conversation yet.";
            return log.Length > 4000 ? log.Substring(log.Length - 4000) : log;
        }

        private async void AiPromptInput_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                await ProcessAiRequest();
            }
        }

        private async void AiSendBtn_Click(object sender, RoutedEventArgs e)
        {
            await ProcessAiRequest();
        }

        private async Task ProcessAiRequest()
        {
            string prompt = AiPromptInput.Text.Trim();
            if (string.IsNullOrEmpty(prompt)) return;

            AiPromptInput.Text = "";
            AiPromptInput.IsEnabled = false;
            AiSendBtn.IsEnabled = false;
            AiReviewPanel.Visibility = Visibility.Collapsed;

            AppendChat($"You: {prompt}");
            AppendChat("Gemini: Thinking...");

            try
            {
                bool useLocal = false;
                string targetModel = "gemini-2.5-flash"; // Default

                try
                {
                    string prefsPath = "gemini_model_prefs.txt";
                    if (System.IO.File.Exists(prefsPath))
                    {
                        var parts = System.IO.File.ReadAllText(prefsPath).Split('|');
                        if (parts.Length >= 2)
                        {
                            useLocal = bool.Parse(parts[0]);
                            targetModel = parts[1];
                        }
                    }
                }
                catch { } // Fallback to defaults

                var assistant = new GeminiAssistantService();
                string generatedCommand = await assistant.GenerateCommandAsync(prompt, useLocal, targetModel);

                // Remove the "Thinking..." line
                string currentLog = AiChatLog.Text;
                int lastGeminiIndex = currentLog.LastIndexOf("Gemini: Thinking...");
                if (lastGeminiIndex >= 0)
                {
                    AiChatLog.Text = currentLog.Substring(0, lastGeminiIndex).TrimEnd() + "\n";
                }

                AppendChat("Gemini: Here is the command you requested. Please review it carefully before running.");
                AiSuggestedCommand.Text = generatedCommand;
                AiReviewPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                AppendChat($"Error: {ex.Message}");
            }
            finally
            {
                AiPromptInput.IsEnabled = true;
                AiSendBtn.IsEnabled = true;
                AiPromptInput.Focus();
            }
        }

        private void AppendChat(string text)
        {
            AiChatLog.AppendText(text + "\n\n");
            AiChatLog.ScrollToEnd();
        }

        private void AiClearChatBtn_Click(object sender, RoutedEventArgs e)
        {
            AiChatLog.Clear();
            AiReviewPanel.Visibility = Visibility.Collapsed;
            AiSuggestedCommand.Text = "";
        }

        private void AiCancelCommandBtn_Click(object sender, RoutedEventArgs e)
        {
            AiReviewPanel.Visibility = Visibility.Collapsed;
            AiSuggestedCommand.Text = "";
            AppendChat("System: Command execution cancelled by user.");
        }

        private async void AiRunCommandBtn_Click(object sender, RoutedEventArgs e)
        {
            string cmd = AiSuggestedCommand.Text.Trim();
            if (string.IsNullOrEmpty(cmd)) return;

            AiReviewPanel.Visibility = Visibility.Collapsed;
            AiSuggestedCommand.Text = "";
            AppendChat($"System: Executing command...\n> {cmd}");

            try
            {
                // Run via PowerShell
                string result = await ProcessRunner.RunAsync("powershell", $"-NoProfile -Command \"{cmd.Replace("\"", "\\\"")}\"");
                AppendChat(string.IsNullOrWhiteSpace(result) ? "System: Command completed successfully (no output)." : $"Output:\n{result}");
            }
            catch (Exception ex)
            {
                AppendChat($"System Error: {ex.Message}");
            }
        }
    }
}
