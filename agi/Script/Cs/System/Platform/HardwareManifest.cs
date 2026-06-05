namespace Logic.System.Platform
{
    public class HardwareManifest
    {
        public bool HasNvidiaGpu { get; set; }
        public string PrimaryGpuName { get; set; }
        public int GpuCount { get; set; }
    }
}
