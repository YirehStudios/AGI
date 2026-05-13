using Godot;
using System.Text.Json;
using System.Collections.Generic;

namespace Logic.Config
{
    public partial class ConfigManager : Node
    {
        public static ConfigManager Instance { get; private set; }
        public bool DarkMode { get; set; } = true; // Por defecto oscuro para la estética cyber
        private string _path = "user://settings.json";

        public override void _Ready()
        {
            Instance = this;
            LoadSettings();
        }

        public void SaveSettings()
        {
            var data = new Dictionary<string, bool> { { "dark_mode", DarkMode } };
            string jsonString = JsonSerializer.Serialize(data);
            using var file = FileAccess.Open(_path, FileAccess.ModeFlags.Write);
            if (file != null) file.StoreString(jsonString);
        }

        private void LoadSettings()
        {
            if (!FileAccess.FileExists(_path)) return;
            using var file = FileAccess.Open(_path, FileAccess.ModeFlags.Read);
            if (file == null) return;
            
            var jsonText = file.GetAsText();
            var data = JsonSerializer.Deserialize<Dictionary<string, bool>>(jsonText);
            if (data != null && data.ContainsKey("dark_mode")) DarkMode = data["dark_mode"];
        }
    }
}