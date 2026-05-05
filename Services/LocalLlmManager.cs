using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LLama;
using LLama.Common;

namespace AutoCommand.Services
{
    /// <summary>
    /// Manages the lifecycle of the local LLM server natively via LLamaSharp.
    /// </summary>
    public static class LocalLlmManager
    {
        private static LLamaWeights _model;
        private static LLamaContext _context;
        private static string _currentModelPath;
        private static bool _useCuda = true; // Default to CUDA per user request
        private static uint _contextSize = 4096; // Default KV cache size

        public static string SharedModelsDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
            "SharedModels");

        public static string GoogleClawModelsDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
            "googleclaw", "models");

        public static string HuggingFaceCacheDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
            ".cache", "huggingface", "hub");

        public static uint ContextSize
        {
            get => _contextSize;
            set
            {
                if (_contextSize != value)
                {
                    _contextSize = value;
                    UnloadModel(); // Force reload if KV cache size changes
                }
            }
        }

        public static bool UseCuda 
        { 
            get => _useCuda; 
            set 
            {
                if (_useCuda != value)
                {
                    _useCuda = value;
                    UnloadModel(); // Force reload if backend changes
                }
            }
        }

        // ── Discovery ────────────────────────────────────────────────────────

        /// <summary>Returns a list of full paths to all .gguf files in the known model directories.</summary>
        public static List<string> GetAvailableModels()
        {
            var models = new List<string>();

            void ScanDirectory(string dir, SearchOption searchOption = SearchOption.TopDirectoryOnly)
            {
                try
                {
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                        return;
                    }
                    models.AddRange(Directory.GetFiles(dir, "*.gguf", searchOption));
                }
                catch { }
            }

            ScanDirectory(SharedModelsDirectory);
            ScanDirectory(GoogleClawModelsDirectory);
            ScanDirectory(HuggingFaceCacheDirectory, SearchOption.AllDirectories);

            return models.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ── Memory Management ────────────────────────────────────────────────

        public static bool IsModelLoaded(string modelPath)
        {
            return _model != null && _context != null && string.Equals(_currentModelPath, modelPath, StringComparison.OrdinalIgnoreCase);
        }

        public static async Task LoadModelAsync(string modelPath, IProgress<string> progress = null)
        {
            if (IsModelLoaded(modelPath)) return;

            UnloadModel();

            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"Model file not found: {modelPath}");
            }

            await Task.Run(() =>
            {
                progress?.Report($"Loading model into memory ({(_useCuda ? "CUDA" : "CPU")} backend)… This may take a moment.");
                
                // Initialize parameters
                var parameters = new ModelParams(modelPath)
                {
                    ContextSize = _contextSize, 
                    GpuLayerCount = _useCuda ? 99 : 0 // 99 pushes all layers to GPU if using CUDA
                };

                // Load weights into memory
                _model = LLamaWeights.LoadFromFile(parameters);
                
                // Create context (reserves RAM/VRAM for KV cache)
                _context = _model.CreateContext(parameters);
                
                _currentModelPath = modelPath;
                progress?.Report("Model loaded successfully.");
            });
        }

        public static void UnloadModel()
        {
            if (_context != null)
            {
                _context.Dispose();
                _context = null;
            }
            
            if (_model != null)
            {
                _model.Dispose();
                _model = null;
            }

            _currentModelPath = null;
        }

        // ── Execution ────────────────────────────────────────────────────────

        /// <summary>Returns an executor instance for the currently loaded model.</summary>
        public static InteractiveExecutor GetExecutor()
        {
            if (_context == null || _model == null)
            {
                throw new InvalidOperationException("No local model is currently loaded in memory.");
            }
            
            return new InteractiveExecutor(_context);
        }
    }
}
