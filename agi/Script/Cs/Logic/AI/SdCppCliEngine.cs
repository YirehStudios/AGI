using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Logic.Lite
{
    public class SdCppCliEngine
    {
        public async Task<string> GenerarImagenCliAsync(string promptUsuario, string modelSafeName, Logic.System.Config.ConfigManager config)
        {
            try
            {
                string osFolder = "";
#if GODOT_WINDOWS
                osFolder = "windows";
#elif GODOT_LINUX
                osFolder = "linux";
#endif
                string modelsDir = ProjectSettings.GlobalizePath($"user://bin/{osFolder}/comfyui/models");
                
                string unetPath = Path.Combine(modelsDir, "checkpoints", modelSafeName);
                if (!File.Exists(unetPath))
                    unetPath = Path.Combine(modelsDir, "unet", modelSafeName);

                if (!File.Exists(unetPath))
                    return $"Error: Checkpoint no encontrado. Esperado en {unetPath}";

                string vaePath = Path.Combine(modelsDir, "vae", "sdxl_vae.safetensors");
                string clipPath = Path.Combine(modelsDir, "clip", "clip_l.safetensors");
                string t5Path = Path.Combine(modelsDir, "clip", "t5xxl_fp16.safetensors");

                string sdCliPath = ProjectSettings.GlobalizePath($"user://bin/{osFolder}/sd_cpp/sd-cli");
                if (!File.Exists(sdCliPath))
                     sdCliPath = ProjectSettings.GlobalizePath($"user://bin/{osFolder}/sd_cpp/sd.exe");

                string outputName = "media_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + ".png";
                string outputPath = ProjectSettings.GlobalizePath($"user://workspace/{outputName}");

                int threads = config?.PerformanceProfile?.Image?.CpuThreads ?? 4;
                
                // Building standard sd.cpp cli arguments
                string args = $"-m \"{unetPath}\" -p \"{promptUsuario}\" -o \"{outputPath}\" --sampling-method euler_a --steps 20 -t {threads}";
                
                if (File.Exists(vaePath)) args += $" --vae \"{vaePath}\"";
                if (File.Exists(clipPath)) args += $" --clip_l \"{clipPath}\"";
                if (File.Exists(t5Path)) args += $" --t5xxl \"{t5Path}\"";
                
                args += " --vulkan"; // Obligatorio según el usuario

                var startInfo = new ProcessStartInfo
                {
                    FileName = sdCliPath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                GD.Print($"[SdCppCliEngine] Ejecutando: {startInfo.FileName} {startInfo.Arguments}");

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) GD.Print($"[SD.cpp_CLI] {e.Data}"); };
                    process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) GD.PrintErr($"[SD.cpp_CLI_ERR] {e.Data}"); };
                    
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    
                    await process.WaitForExitAsync();
                    
                    if (File.Exists(outputPath))
                    {
                        return $"Success! Result saved at {outputPath}. Show this image to the user using the [media] tag: [media]{outputPath}[/media]";
                    }
                    else
                    {
                        return $"Error generando imagen: El proceso terminó pero no se encontró la imagen en {outputPath}";
                    }
                }
            }
            catch (Exception ex)
            {
                return $"Error ejecutando SD.cpp CLI: {ex.Message}";
            }
        }
    }
}
