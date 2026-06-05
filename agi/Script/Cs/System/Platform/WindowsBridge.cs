using Godot;
using System;
using System.Diagnostics;
using System.Management;
using System.IO;
using SysPath = System.IO.Path;

namespace Logic.System.Platform
{
    [global::System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public class WindowsBridge : IPlatformBridge
    {
        public string OperatingSystemIdentifier => "Windows";
        public bool CanRunLocalEngines => true;

        public void InitializeEnvironment()
        {
            GD.Print("[PlatformBridge] Windows Environment Initialized.");
        }

        public HardwareManifest QueryHardwareNatively()
        {
            var manifest = new HardwareManifest { HasNvidiaGpu = false, GpuCount = 0, PrimaryGpuName = "Unknown" };

            try
            {
                // WMI Query for VideoControllers (Requires System.Management assembly)
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        manifest.GpuCount++;
                        string gpuName = obj["Name"]?.ToString() ?? "";
                        if (string.IsNullOrEmpty(manifest.PrimaryGpuName) || manifest.PrimaryGpuName == "Unknown")
                        {
                            manifest.PrimaryGpuName = gpuName;
                        }

                        if (gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                        {
                            manifest.HasNvidiaGpu = true;
                            manifest.PrimaryGpuName = gpuName; // Prioritize NVIDIA for CUDA turbo validation
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[WindowsBridge] Native WMI GPU discovery failed: {ex.Message}");
            }

            return manifest;
        }

        public void TerminateOrphanedResources()
        {
            GD.Print("[WindowsBridge] Executing non-interfering process teardown for orphaned AGI resources.");
            string[] targetResources = { "llama-server", "whisper-server", "sherpa-onnx-tts-server" };
            string binPath = ProjectSettings.GlobalizePath("user://bin").Replace('/', '\\');
            string envPath = ProjectSettings.GlobalizePath("user://env").Replace('/', '\\');

            // 1. Terminate native C++ components cleanly
            foreach (string resourceName in targetResources)
            {
                Process[] orphanedProcesses = Process.GetProcessesByName(resourceName);
                foreach (Process process in orphanedProcesses)
                {
                    try
                    {
                        string processPath = process.MainModule?.FileName;
                        if (processPath != null && processPath.StartsWith(binPath, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!process.HasExited)
                            {
                                process.Kill();
                                process.WaitForExit(1000);
                            }
                        }
                    }
                    catch (Exception) { /* Ignored due to access restrictions or exited state */ }
                    finally { process.Dispose(); }
                }
            }
            
            // 2. Clean up python microservices strictly bound to the embedded environment
            try
            {
                Process[] pythonProcesses = Process.GetProcessesByName("python");
                foreach (Process p in pythonProcesses)
                {
                    try
                    {
                        string pPath = p.MainModule?.FileName;
                        if (pPath != null && pPath.StartsWith(envPath, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!p.HasExited) p.Kill();
                        }
                    }
                    catch (Exception) { /* Ignored */ }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[WindowsBridge] Native python teardown failed: {ex.Message}");
            }
        }

        public ProcessStartInfo ConfigureEngineExecution(string engineName, string arguments, string localEngineDirectory)
        {
            string executablePath = SysPath.Combine(localEngineDirectory, engineName + ".exe");
            
            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException($"Engine binary not found: {executablePath}");
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                WorkingDirectory = localEngineDirectory, // Enforces LoadLibrary priority for .dlls locally
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            return startInfo;
        }

        public ProcessStartInfo ConfigurePythonMicroservice(string scriptPath, string arguments, string projectRootDirectory, string environmentName = "python")
        {
            string envPath = ProjectSettings.GlobalizePath("user://env");
            string embeddedPythonPath = SysPath.Combine(envPath, environmentName, "python.exe");

            if (!File.Exists(embeddedPythonPath))
            {
                throw new FileNotFoundException($"Embedded Python environment not found: {embeddedPythonPath}");
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = embeddedPythonPath,
                Arguments = $"-u \"{scriptPath}\" {arguments}",
                WorkingDirectory = SysPath.GetDirectoryName(embeddedPythonPath), // Enforces local DLL loading for ONNX
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            return startInfo;
        }
    }
}
