using Godot;
using System;

namespace Logic.System.Platform
{
    public static class PlatformFactory
    {
        public static IPlatformBridge ResolveBridge()
        {
#pragma warning disable CA1416
            string osName = OS.GetName();
            
            if (osName == "Windows") 
                return new WindowsBridge();
            if (osName == "Linux" || osName == "FreeBSD" || osName == "X11") 
                return new LinuxBridge();
            if (osName == "Android") 
                return new AndroidBridge();
            if (osName == "macOS") 
                return new MacOSBridge();
                
#pragma warning restore CA1416
            throw new PlatformNotSupportedException($"Unsupported architecture: {osName}");
        }
    }
}
