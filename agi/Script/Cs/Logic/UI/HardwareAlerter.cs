using Godot;
using Logic.System.Config;
using System;

namespace Logic.UI
{
    /// <summary>
    /// Serves as a dynamic UI bridge that natively queries the underlying hardware via the platform bridge
    /// and constructs conditional routing interfaces for optimized inference execution pipelines.
    /// </summary>
    public partial class HardwareAlerter : Control
    {
        private EnvironmentManager _environmentManager;
        private ConfirmationDialog _alertWindow;

        public override void _Ready()
        {
            _environmentManager = GetNodeOrNull<EnvironmentManager>("/root/EnvironmentManager");

            if (_environmentManager == null)
            {
                GD.PrintErr("[HardwareAlerter] EnvironmentManager is uninitialized. Aborting hardware pipeline routing.");
                return;
            }

            _alertWindow = new ConfirmationDialog();
            _alertWindow.Title = "Hardware Pipeline Selection";
            _alertWindow.GetOkButton().Text = "Enable CUDA Turbo";
            _alertWindow.GetCancelButton().Text = "Use Vulkan Baseline";
            _alertWindow.DialogText = "NVIDIA Hardware Detected.\nWould you like to enable the high-performance CUDA Turbo mode, or fallback to the universal Vulkan baseline?";
            
            _alertWindow.Confirmed += OnCudaTurboConfirmed;
            _alertWindow.Canceled += OnVulkanBaselineConfirmed;
            
            AddChild(_alertWindow);

            // Execute hardware resolution only if local inference is structurally permitted
            if (_environmentManager.CanRunLocalModels)
            {
                EvaluateHardware();
            }
        }

        private void EvaluateHardware()
        {
            try
            {
                var manifest = _environmentManager.Bridge.QueryHardwareNatively();

                if (manifest.HasNvidiaGpu)
                {
                    GD.Print("[HardwareAlerter] NVIDIA Hardware detected. Prompting user for acceleration track.");
                    _alertWindow.PopupCentered();
                }
                else
                {
                    GD.Print("[HardwareAlerter] Non-NVIDIA hardware detected. Enforcing Vulkan Baseline track.");
                    ConfigManager.Instance.UseCudaTurbo = false;
                    ConfigManager.Instance.SaveConfiguration();
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[HardwareAlerter] Critical failure querying native hardware manifest: {ex.Message}");
            }
        }

        private void OnCudaTurboConfirmed()
        {
            GD.Print("[HardwareAlerter] User opted into CUDA Turbo mode.");
            ConfigManager.Instance.UseCudaTurbo = true;
            ConfigManager.Instance.SaveConfiguration();
        }

        private void OnVulkanBaselineConfirmed()
        {
            GD.Print("[HardwareAlerter] User opted into Vulkan Baseline mode.");
            ConfigManager.Instance.UseCudaTurbo = false;
            ConfigManager.Instance.SaveConfiguration();
        }
    }
}
