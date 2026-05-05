using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AutoCommand.Services
{
    public class AiAuditResult
    {
        [JsonPropertyName("threatLevel")]
        public string ThreatLevel { get; set; } // "Safe", "Caution", "Danger"

        [JsonPropertyName("summary")]
        public string Summary { get; set; }

        [JsonPropertyName("details")]
        public string Details { get; set; }
    }

    public class GeminiAssistantService
    {
        // Bug fix #3: Set a 30-second timeout so a hung API never freezes the UI
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private readonly string _cloudApiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
        private readonly string _systemInstruction;

        public GeminiAssistantService()
        {
            _systemInstruction = @"You are an expert Windows security assistant for non-technical users. 
Your job is to analyze the data provided from the AutoCommand toolkit. 
Highlight any suspicious items (like unknown processes, risky ports, or unusual configurations) in simple, easy-to-understand terms. 
Keep it brief, actionable, and friendly.

You MUST respond strictly in the following JSON format:
{
  ""threatLevel"": ""Safe"" | ""Caution"" | ""Danger"",
  ""summary"": ""A brief 1-2 sentence non-technical summary"",
  ""details"": ""A more detailed explanation of what you found, formatted in Markdown if necessary.""
}
Do NOT include Markdown code block formatting (like ```json) in your response, just the raw JSON object.
";
        }

        private string GetApiKey()
        {
            try
            {
                string path = "gemini_api_key.txt";
                if (File.Exists(path))
                    return File.ReadAllText(path).Trim();
            }
            catch { }
            return null;
        }

        // Bug fix #4: Filter to only models that support generateContent
        public async Task<List<string>> GetCloudModelsAsync()
        {
            var models = new List<string>();
            try
            {
                string apiKey = GetApiKey();
                if (string.IsNullOrEmpty(apiKey)) return models;

                var response = await _httpClient.GetAsync($"{_cloudApiBaseUrl}?key={apiKey}");
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(responseString);

                if (document.RootElement.TryGetProperty("models", out JsonElement modelsArray))
                {
                    foreach (var item in modelsArray.EnumerateArray())
                    {
                        // Only include models that can run generateContent
                        bool supportsGenerate = false;
                        if (item.TryGetProperty("supportedGenerationMethods", out JsonElement methods))
                        {
                            foreach (var method in methods.EnumerateArray())
                            {
                                if (method.GetString() == "generateContent")
                                {
                                    supportsGenerate = true;
                                    break;
                                }
                            }
                        }
                        if (!supportsGenerate) continue;

                        if (item.TryGetProperty("name", out JsonElement nameProp))
                        {
                            string name = nameProp.GetString();
                            if (name.StartsWith("models/"))
                                name = name.Substring(7);

                            // Filter to Gemini 2.x and 1.5 families only (skip experimental/internal)
                            if (name.StartsWith("gemini-"))
                                models.Add(name);
                        }
                    }
                }

                // Sort: 2.5 first, then 2.0, then 1.5
                models.Sort((a, b) =>
                {
                    int VersionScore(string m)
                    {
                        if (m.StartsWith("gemini-2.5")) return 3;
                        if (m.StartsWith("gemini-2.0")) return 2;
                        if (m.StartsWith("gemini-1.5")) return 1;
                        return 0;
                    }
                    return VersionScore(b).CompareTo(VersionScore(a));
                });
            }
            catch { }
            return models;
        }

        public Task<List<string>> GetLocalModelsAsync()
        {
            // Now simply returns the list of GGUF files in the SharedModels directory
            return Task.FromResult(LocalLlmManager.GetAvailableModels());
        }

        public async Task<AiAuditResult> AnalyzeContextAsync(string contextData, bool useLocalLlm, string targetModel,
            CancellationToken ct = default)
        {
            if (useLocalLlm)
                return await AnalyzeWithLocalLlmAsync(contextData, targetModel, ct);
            else
                return await AnalyzeWithCloudApiAsync(contextData, targetModel, ct);
        }

        public async Task<string> GenerateCommandAsync(string userPrompt, bool useLocalLlm, string targetModel, CancellationToken ct = default)
        {
            string systemInstruction = @"You are a strict Windows PowerShell script generator. 
The user will ask you to perform a task on their Windows machine. 
You must output ONLY the raw PowerShell code required to perform the task. 
Do NOT include markdown formatting (like ```powershell), explanations, or confirmation. Just the code.";

            if (useLocalLlm)
            {
                // Local fallback (similar to AnalyzeWithLocalLlmAsync)
                await LocalLlmManager.LoadModelAsync(targetModel);
                var executor = LocalLlmManager.GetExecutor();
                string prompt = $"<|system|>\n{systemInstruction}\n<|user|>\n{userPrompt}\n<|model|>\n";
                
                var inferenceParams = new LLama.Common.InferenceParams() 
                { 
                    MaxTokens = 500, 
                    SamplingPipeline = new LLama.Sampling.DefaultSamplingPipeline { Temperature = 0.1f },
                    AntiPrompts = new List<string> { "<|user|>" } 
                };
                var resultBuilder = new StringBuilder();
                await foreach (var text in executor.InferAsync(prompt, inferenceParams, ct)) resultBuilder.Append(text);
                
                string cleaned = resultBuilder.ToString().Trim();
                if (cleaned.StartsWith("```"))
                {
                    int start = cleaned.IndexOf('\n') + 1;
                    int end = cleaned.LastIndexOf("```");
                    if (end > start) cleaned = cleaned.Substring(start, end - start).Trim();
                }
                return cleaned;
            }
            else
            {
                string apiKey = GetApiKey();
                if (string.IsNullOrEmpty(apiKey)) throw new InvalidOperationException("Gemini API key not found. Please set it in AI Setup.");

                var requestBody = new
                {
                    system_instruction = new { parts = new[] { new { text = systemInstruction } } },
                    contents = new[] { new { parts = new[] { new { text = userPrompt } } } },
                    generationConfig = new { temperature = 0.1 }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                string modelUrl = $"{_cloudApiBaseUrl}/{targetModel}:generateContent?key={apiKey}";
                var response = await _httpClient.PostAsync(modelUrl, content, ct);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                
                try
                {
                    using var document = JsonDocument.Parse(responseString);
                    var textResult = document.RootElement
                        .GetProperty("candidates")[0]
                        .GetProperty("content")
                        .GetProperty("parts")[0]
                        .GetProperty("text").GetString();

                    string cleaned = textResult.Trim();
                    if (cleaned.StartsWith("```"))
                    {
                        int start = cleaned.IndexOf('\n') + 1;
                        int end = cleaned.LastIndexOf("```");
                        if (end > start) cleaned = cleaned.Substring(start, end - start).Trim();
                    }
                    return cleaned;
                }
                catch { return "Error parsing AI response."; }
            }
        }

        private async Task<AiAuditResult> AnalyzeWithCloudApiAsync(string contextData, string targetModel,
            CancellationToken ct = default)
        {
            string apiKey = GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                // Bug fix #2: corrected error message to point to the actual file
                throw new InvalidOperationException(
                    "Gemini API key not found. Please run the AI setup and save your key.\n(Expected: gemini_api_key.txt)");
            }

            var requestBody = new
            {
                system_instruction = new { parts = new[] { new { text = _systemInstruction } } },
                contents = new[]
                {
                    new { parts = new[] { new { text = $"Analyze the following data:\n\n{contextData}" } } }
                },
                generationConfig = new { response_mime_type = "application/json" }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            if (string.IsNullOrWhiteSpace(targetModel))
            {
                throw new InvalidOperationException(
                    "No cloud model selected. Please open the AI Setup tab and select a model.");
            }

            string modelUrl = $"{_cloudApiBaseUrl}/{targetModel}:generateContent?key={apiKey}";
            var response = await _httpClient.PostAsync(modelUrl, content, ct);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            return ParseGeminiResponse(responseString);
        }

        private async Task<AiAuditResult> AnalyzeWithLocalLlmAsync(string contextData, string targetModel,
            CancellationToken ct = default)
        {
            // Ensure model is loaded in memory via LLamaSharp
            await LocalLlmManager.LoadModelAsync(targetModel);
            var executor = LocalLlmManager.GetExecutor();

            // Construct standard ChatML/Instruct style prompt enforcing JSON
            string prompt = $"<|system|>\n{_systemInstruction}\n<|user|>\nAnalyze the following data and return EXACTLY valid JSON matching the requested schema:\n\n{contextData}\n<|model|>\n{{";

            var inferenceParams = new LLama.Common.InferenceParams() 
            { 
                MaxTokens = 1500, 
                SamplingPipeline = new LLama.Sampling.DefaultSamplingPipeline { Temperature = 0.1f },
                AntiPrompts = new List<string> { "<|user|>" } 
            };

            var resultBuilder = new StringBuilder();
            resultBuilder.Append("{"); // Since we seeded the prompt with { to force JSON start

            await foreach (var text in executor.InferAsync(prompt, inferenceParams, ct))
            {
                resultBuilder.Append(text);
            }

            return ParseJsonResult(resultBuilder.ToString());
        }

        // Bug fix #5: Centralised, safe JSON parsing with fallback for malformed LLM output
        private AiAuditResult ParseGeminiResponse(string responseString)
        {
            try
            {
                using var document = JsonDocument.Parse(responseString);
                var textResult = document.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text").GetString();

                return ParseJsonResult(textResult);
            }
            catch (Exception ex)
            {
                return new AiAuditResult
                {
                    ThreatLevel = "Unknown",
                    Summary = "Could not parse the AI response.",
                    Details = $"The AI returned an unexpected format. Raw error: {ex.Message}"
                };
            }
        }

        private AiAuditResult ParseJsonResult(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return FallbackResult("The AI returned an empty response.");

            // Strip markdown code fences if a local model wrapped the JSON anyway
            string cleaned = text.Trim();
            if (cleaned.StartsWith("```"))
            {
                int start = cleaned.IndexOf('\n') + 1;
                int end = cleaned.LastIndexOf("```");
                if (end > start) cleaned = cleaned.Substring(start, end - start).Trim();
            }

            try
            {
                return JsonSerializer.Deserialize<AiAuditResult>(cleaned);
            }
            catch
            {
                return FallbackResult(text.Length > 300 ? text.Substring(0, 300) + "…" : text);
            }
        }

        private static AiAuditResult FallbackResult(string rawText) => new AiAuditResult
        {
            ThreatLevel = "Unknown",
            Summary = "The AI response could not be parsed.",
            Details = rawText
        };
    }
}
