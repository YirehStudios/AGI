using Godot;
using System;
using System.Diagnostics;
using System.IO;
using SysPath = System.IO.Path;

namespace Logic.System.Platform
{
    public class LinuxBridge : IPlatformBridge
    {
        public string OperatingSystemIdentifier => "Linux";
        public bool CanRunLocalEngines => true;

        public void InitializeEnvironment()
        {
            GD.Print("[PlatformBridge] Linux Environment Initialized.");
        }

        public HardwareManifest QueryHardwareNatively()
        {
            var manifest = new HardwareManifest { HasNvidiaGpu = false, GpuCount = 0, PrimaryGpuName = "Unknown" };

            try
            {
                // Traverse lspci natively filtering for VGA or 3D controllers
                ProcessStartInfo lspciInfo = new ProcessStartInfo
                {
                    FileName = "lspci",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process p = Process.Start(lspciInfo))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();

                    string[] lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        if (line.Contains("VGA") || line.Contains("3D controller"))
                        {
                            manifest.GpuCount++;
                            if (line.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                            {
                                manifest.HasNvidiaGpu = true;
                                manifest.PrimaryGpuName = "NVIDIA Device (Linux)";
                            }
                        }
                    }
                }

                // If NVIDIA is suspected, try to get the specific model via proc or nvidia-smi as a non-invasive diagnostic
                if (manifest.HasNvidiaGpu && File.Exists("/proc/driver/nvidia/version"))
                {
                    try
                    {
                        ProcessStartInfo smiInfo = new ProcessStartInfo
                        {
                            FileName = "nvidia-smi",
                            Arguments = "--query-gpu=name --format=csv,noheader",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        
                        using (Process smi = Process.Start(smiInfo))
                        {
                            string smiOutput = smi.StandardOutput.ReadToEnd().Trim();
                            smi.WaitForExit();
                            
                            if (!string.IsNullOrEmpty(smiOutput))
                            {
                                string[] gpus = smiOutput.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                                if (gpus.Length > 0)
                                {
                                    manifest.PrimaryGpuName = gpus[0].Trim();
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // nvidia-smi might not be globally linked, gracefully degrade to fallback name.
                        GD.Print("[LinuxBridge] nvidia-smi not accessible in PATH, utilizing generic hardware profile.");
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[LinuxBridge] Native hardware query failed: {ex.Message}");
            }

            return manifest;
        }

        public void TerminateOrphanedResources()
        {
            GD.Print("[LinuxBridge] Executing safe process teardown for local AGI resources.");
            
            string[] targetResources = { "llama-server", "whisper-server", "sherpa-onnx-tts-server", "search_server.py", "mcp_server.py", "tts_server.py", "image_server.py", "video_server.py" };
            string binPath = ProjectSettings.GlobalizePath("user://bin");

            foreach (string resourceName in targetResources)
            {
                try
                {
                    ProcessStartInfo pgrepInfo = new ProcessStartInfo
                    {
                        FileName = "pgrep",
                        Arguments = $"-f \"{resourceName}\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (Process pgrep = Process.Start(pgrepInfo))
                    {
                        string output = pgrep.StandardOutput.ReadToEnd();
                        pgrep.WaitForExit();

                        string[] pids = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string pidStr in pids)
                        {
                            if (int.TryParse(pidStr, out int pid))
                            {
                                // Validate process origin to prevent killing external processes (No-Interference)
                                string cmdlinePath = $"/proc/{pid}/cmdline";
                                if (File.Exists(cmdlinePath))
                                {
                                    string cmdline = File.ReadAllText(cmdlinePath);
                                    // cmdline args are null-separated in /proc
                                    if (cmdline.Contains(binPath) || cmdline.Contains("user://"))
                                    {
                                        try { Process.GetProcessById(pid).Kill(); } catch { }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[LinuxBridge] Non-interfering teardown encountered fault: {ex.Message}");
                }
            }
        }

        public ProcessStartInfo ConfigureEngineExecution(string engineName, string arguments, string localEngineDirectory)
        {
            string executablePath = SysPath.Combine(localEngineDirectory, engineName);
            
            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException($"Engine binary not found: {executablePath}");
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                WorkingDirectory = localEngineDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Dynamically inject the local library path to prioritize local .so libraries for Vulkan or CUDA
            startInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = localEngineDirectory;

            return startInfo;
        }

        public ProcessStartInfo ConfigurePythonMicroservice(string scriptPath, string arguments, string projectRootDirectory, string environmentName = "python")
        {
            string envPath = ProjectSettings.GlobalizePath("user://env");
            string uvPythonPath = SysPath.Combine(envPath, environmentName, "bin", "python3");

            if (!File.Exists(uvPythonPath))
            {
                // Fallback struct for backward compatibility if uv venv was constructed elsewhere
                uvPythonPath = SysPath.Combine(envPath, "python", "bin", "python3");
                if (!File.Exists(uvPythonPath))
                {
                    throw new FileNotFoundException($"Virtual environment python not found at {uvPythonPath}");
                }
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = uvPythonPath,
                Arguments = $"-u \"{scriptPath}\" {arguments}",
                WorkingDirectory = SysPath.GetDirectoryName(scriptPath) ?? projectRootDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Isolate dynamic libraries for python wheels (e.g., onnxruntime) from global state
            startInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = SysPath.Combine(envPath, "lib");

            return startInfo;
        }
    }
}
