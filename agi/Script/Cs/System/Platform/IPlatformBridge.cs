using System.Diagnostics;

namespace Logic.System.Platform
{
    public interface IPlatformBridge
    {
        string OperatingSystemIdentifier { get; }
        bool CanRunLocalEngines { get; }

        void InitializeEnvironment();
        HardwareManifest QueryHardwareNatively();
        void TerminateOrphanedResources();
        
        /// <summary>
        /// Configures process execution parameters to ensure dependencies (e.g. DLLs, shared objects)
        /// are scoped strictly to the local directory, enforcing the anti-collision architecture.
        /// </summary>
        ProcessStartInfo ConfigureEngineExecution(string engineName, string arguments, string localEngineDirectory);
        
        /// <summary>
        /// Configures the Python virtual environment execution context natively for the platform.
        /// </summary>
        ProcessStartInfo ConfigurePythonMicroservice(string scriptPath, string arguments, string projectRootDirectory, string environmentName = "python");
    }
}
