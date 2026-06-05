using System.Diagnostics;
using Godot;

namespace Logic.System.Platform
{
    public class MacOSBridge : IPlatformBridge
    {
        public string OperatingSystemIdentifier => "macOS";
        public bool CanRunLocalEngines => false;

        public void InitializeEnvironment()
        {
            GD.Print("[PlatformBridge] MacOS Environment Initialized (UI-Only Mode).");
        }

        public HardwareManifest QueryHardwareNatively()
        {
            return new HardwareManifest { HasNvidiaGpu = false, PrimaryGpuName = "Apple Silicon/Radeon", GpuCount = 1 };
        }

        public void TerminateOrphanedResources() { }

        public ProcessStartInfo ConfigureEngineExecution(string engineName, string arguments, string localEngineDirectory)
        {
            throw new global::System.NotSupportedException("Local inference engines are currently structurally restricted on macOS. Remote API required.");
        }

        public ProcessStartInfo ConfigurePythonMicroservice(string scriptPath, string arguments, string projectRootDirectory, string environmentName = "python")
        {
            throw new global::System.NotSupportedException("Local Python microservices are currently structurally restricted on macOS. Remote API required.");
        }
    }
}
