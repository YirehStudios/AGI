using Godot;
using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace Logic.System.Config
{
    /// <summary>
    /// Manages the application's configuration state, handles persistence, and validates model integrity.
    /// Operates strictly as a data layer Singleton without UI dependencies.
    /// </summary>
    public partial class ConfigManager : Node
    {
        public enum AppMode 
        { 
            None, 
            RemoteUI, 
            LocalHost 
        }

        public AppMode CurrentMode { get; set; } = AppMode.None;
        public string RemoteHostUrl { get; set; } = string.Empty;
        public string ActiveModelPath { get; set; } = string.Empty;
        public string ActiveModelName { get; set; } = string.Empty;

        private string _settingsDirectory;
        private string _configFilePath;
        private string _presetsFilePath;

        /// <summary>
        /// Defines the structure for model presets loaded from the external JSON configuration.
        /// </summary>
        public class ModelPreset
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public List<string> DownloadLinks { get; set; }
            public long ExpectedSize { get; set; }
        }

        /// <summary>
        /// Internal structure used exclusively for JSON serialization of the configuration state.
        /// </summary>
        private class ConfigState
        {
            public AppMode Mode { get; set; }
            public string RemoteHostUrl { get; set; }
            public string ActiveModelPath { get; set; }
            public string ActiveModelName { get; set; }
        }

        public override void _Ready()
        {
            _settingsDirectory = ProjectSettings.GlobalizePath("user://settings");
            _configFilePath = Path.Combine(_settingsDirectory, "config.json");
            _presetsFilePath = ProjectSettings.GlobalizePath("user://presets.json");

            LoadConfiguration();
        }

        /// <summary>
        /// Serializes the current application state to the local configuration file.
        /// </summary>
        public void SaveConfiguration()
        {
            try
            {
                if (!Directory.Exists(_settingsDirectory))
                {
                    Directory.CreateDirectory(_settingsDirectory);
                }

                ConfigState state = new ConfigState
                {
                    Mode = CurrentMode,
                    RemoteHostUrl = RemoteHostUrl,
                    ActiveModelPath = ActiveModelPath,
                    ActiveModelName = ActiveModelName
                };

                JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(state, options);
                
                File.WriteAllText(_configFilePath, jsonString);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ConfigManager: Failed to save configuration. Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads and deserializes the configuration state from the local file system.
        /// </summary>
        public void LoadConfiguration()
        {
            if (!File.Exists(_configFilePath))
            {
                return;
            }

            try
            {
                string jsonString = File.ReadAllText(_configFilePath);
                ConfigState state = JsonSerializer.Deserialize<ConfigState>(jsonString);

                if (state != null)
                {
                    CurrentMode = state.Mode;
                    RemoteHostUrl = state.RemoteHostUrl;
                    ActiveModelPath = state.ActiveModelPath;
                    ActiveModelName = state.ActiveModelName;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ConfigManager: Failed to load configuration. Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Parses the external presets file to provide a list of available AI models.
        /// </summary>
        /// <returns>A list of ModelPreset objects. Returns an empty list if the file is missing or invalid.</returns>
        public List<ModelPreset> ListAvailablePresets()
        {
            if (!File.Exists(_presetsFilePath))
            {
                GD.PrintErr($"ConfigManager: Presets file not found at {_presetsFilePath}.");
                return new List<ModelPreset>();
            }

            try
            {
                string jsonString = File.ReadAllText(_presetsFilePath);
                JsonSerializerOptions options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<List<ModelPreset>>(jsonString, options) ?? new List<ModelPreset>();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ConfigManager: Failed to parse presets. Exception: {ex.Message}");
                return new List<ModelPreset>();
            }
        }

        /// <summary>
        /// Validates the existence and expected byte size of the currently assigned model file.
        /// </summary>
        /// <param name="expectedSize">The expected file size in bytes to verify integrity.</param>
        /// <returns>A tuple containing a boolean success flag and a descriptive error message if applicable.</returns>
        public (bool IsValid, string ErrorMessage) ValidateModelIntegrity(long expectedSize)
        {
            if (string.IsNullOrEmpty(ActiveModelPath))
            {
                return (false, "Model path is not configured.");
            }

            if (!File.Exists(ActiveModelPath))
            {
                return (false, $"The model file was not found at the specified path: {ActiveModelPath}");
            }

            try
            {
                FileInfo fileInfo = new FileInfo(ActiveModelPath);
                if (fileInfo.Length != expectedSize)
                {
                    return (false, $"Model size mismatch. Expected {expectedSize} bytes, but found {fileInfo.Length} bytes. The file may be corrupted or incomplete.");
                }

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, $"An error occurred while validating the model: {ex.Message}");
            }
        }
    }
}