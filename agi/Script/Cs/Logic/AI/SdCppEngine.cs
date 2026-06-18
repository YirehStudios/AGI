using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Logic.Lite
{
    /// <summary>
    /// Engine bridge for stable-diffusion.cpp (sd.cpp).
    /// Handles both standalone CLI inference (GenerarImagenCliAsync) and Server mode process generation (GetServerStartInfo).
    /// Utilizes EnvironmentManager for strict cross-platform OS path resolution.
    /// </summary>
    public class SdCppEngine
    {
        /// <summary>
        /// Centralized path resolution for sd.cpp components.
        /// Extracts absolute paths for the UNET, VAE, and CLIP components based on the EnvironmentManager configuration.
        /// </summary>
        private static void ResolvePaths(dynamic envManager, string safeFileName, out string unetPath, out string vaePath, out string clipPath, out string t5Path, out string sdCliPath, out string sdDir)
        {
            string osFolder = envManager.Bridge.OperatingSystemIdentifier.ToLower();
            string modelsDir = envManager.ModelsPath;
            
            unetPath = Path.Combine(modelsDir, "checkpoints", safeFileName);
            if (!File.Exists(unetPath)) unetPath = Path.Combine(modelsDir, "unet", safeFileName);
            
            vaePath = Path.Combine(modelsDir, "vae", "sdxl_vae.safetensors");
            if (!File.Exists(vaePath)) vaePath = Path.Combine(modelsDir, "checkpoints", "sdxl_vae.safetensors");
            
            clipPath = Path.Combine(modelsDir, "clip", "clip_l.safetensors");
            t5Path = Path.Combine(modelsDir, "clip", "t5xxl_fp16.safetensors");

            sdDir = Path.Combine(envManager.BinPath, osFolder, "sd_cpp");
            sdCliPath = Path.Combine(sdDir, "sd-cli");
            if (!File.Exists(sdCliPath)) sdCliPath = Path.Combine(sdDir, "sd");
            if (!File.Exists(sdCliPath)) sdCliPath = Path.Combine(sdDir, "sd.exe");
        }

        /// <summary>
        /// Generates an image asynchronously using the sd.cpp CLI natively.
        /// Uses ProcessStartInfo with hidden windows and redirects output to the system's LogMicroserviceStream.
        /// </summary>
        public async Task<string> GenerarImagenCliAsync(string promptUsuario, string modelSafeName, Logic.System.Config.ConfigManager config)
        {
            GD.Print($"[SdCppEngine] GenerarImagenCliAsync INICIADO. ModelSafeName: {modelSafeName}");
            try
            {
                var envManager = ((SceneTree)Engine.GetMainLoop()).Root.GetNodeOrNull("/root/EnvironmentManager");
                if (envManager == null) return "Error: EnvironmentManager no encontrado en la jerarquía global.";

                ResolvePaths(envManager, modelSafeName, out string unetPath, out string vaePath, out string clipPath, out string t5Path, out string sdCliPath, out string sdDir);

                if (!File.Exists(unetPath))
                {
                    GD.PrintErr($"[SdCppEngine] Checkpoint no encontrado. Rutas intentadas. Última: {unetPath}");
                    return $"Error: Checkpoint no encontrado. Esperado en {unetPath}";
                }

                GD.Print($"[SdCppEngine] Unet Path Confirmado: {unetPath}");

                string outputName = "media_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + ".png";
                string outputPath = ProjectSettings.GlobalizePath($"user://workspace/{outputName}");

                int threads = config?.PerformanceProfile?.Image?.CpuThreads ?? 4;
                GD.Print($"[SdCppEngine] Configuración: Threads={threads}, Output={outputPath}");
                
                string finalPrompt = promptUsuario;
                string lowerModel = modelSafeName.ToLower();
                if (lowerModel.Contains("pony") || lowerModel.Contains("sdxl"))
                {
                    if (!finalPrompt.Contains("score_9"))
                    {
                        finalPrompt = "score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, " + finalPrompt;
                    }
                }

                string negPrompt = "score_4, score_3, score_2, score_1, worst quality, low quality, bad anatomy, bad hands, missing fingers, extra digit, fewer digits, cropped, signature, watermark, username, blurry";

                // Building standard sd.cpp cli arguments
                string args = $"-m \"{unetPath}\" -p \"{finalPrompt}\" -n \"{negPrompt}\" -o \"{outputPath}\" --sampling-method euler_a --steps 20 -t {threads} --vulkan";
                
                if (File.Exists(vaePath)) args += $" --vae \"{vaePath}\"";
                if (File.Exists(clipPath)) args += $" --clip_l \"{clipPath}\"";
                if (File.Exists(t5Path)) args += $" --t5xxl \"{t5Path}\"";
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = sdCliPath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = sdDir
                };

                GD.Print($"[SdCppEngine] Ejecutando: {startInfo.FileName} {startInfo.Arguments}");

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Logic.Backend.BackendLauncher.LogMicroserviceStream("SD.cpp_CLI", e.Data, false); };
                    process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Logic.Backend.BackendLauncher.LogMicroserviceStream("SD.cpp_CLI", e.Data, true); };
                    
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
                return $"Error ejecutando SD.cpp: {ex.Message}";
            }
        }

        /// <summary>
        /// Instantiates ProcessStartInfo to boot sd.cpp in backend server mode using local EnvironmentManager constraints.
        /// </summary>
        public static ProcessStartInfo GetServerStartInfo(dynamic envManager, string safeFileName, string bindAddress)
        {
            ResolvePaths(envManager, safeFileName, out string unetPath, out string vaePath, out string clipPath, out string t5Path, out string sdCliPath, out string sdDir);

            string sdArgs = $"--mode server --host {bindAddress} --port 8188 --vulkan -m \"{unetPath}\"";
            if (File.Exists(vaePath)) sdArgs += $" --vae \"{vaePath}\"";
            if (File.Exists(clipPath)) sdArgs += $" --clip_l \"{clipPath}\"";
            if (File.Exists(t5Path)) sdArgs += $" --t5xxl \"{t5Path}\"";

            ProcessStartInfo info;
            try
            {
                info = envManager.Bridge.ConfigureEngineExecution("sd-cli", sdArgs, sdDir);
            }
            catch (FileNotFoundException)
            {
                try { info = envManager.Bridge.ConfigureEngineExecution("sd", sdArgs, sdDir); }
                catch { info = envManager.Bridge.ConfigureEngineExecution("sd.exe", sdArgs, sdDir); }
            }
            
            return info;
        }
    }
}
