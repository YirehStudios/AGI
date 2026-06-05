using System.Diagnostics;
using Godot;

namespace Logic.System.Platform
{
    public class AndroidBridge : IPlatformBridge
    {
        public string OperatingSystemIdentifier => "Android";
        public bool CanRunLocalEngines => false;

        public void InitializeEnvironment()
        {
            GD.Print("[PlatformBridge] Android Environment Initialized (UI-Only Mode).");
        }

        public HardwareManifest QueryHardwareNatively()
        {
            return new HardwareManifest { HasNvidiaGpu = false, PrimaryGpuName = "Mobile SoC/APU", GpuCount = 1 };
        }

        public void TerminateOrphanedResources() { }

        public ProcessStartInfo ConfigureEngineExecution(string engineName, string arguments, string localEngineDirectory)
        {
            throw new global::System.NotSupportedException("Local inference engines are structurally restricted on Android (UI-Only Mode).");
        }

        public ProcessStartInfo ConfigurePythonMicroservice(string scriptPath, string arguments, string projectRootDirectory, string environmentName = "python")
        {
            throw new global::System.NotSupportedException("Local Python microservices are structurally restricted on Android (UI-Only Mode).");
        }
    }
}
