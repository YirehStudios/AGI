using Godot;
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace Logic.UI.Effects
{
    /// <summary>
    /// Advanced Effects Engine for managing high-performance native OS visual effects
    /// like Acrylic, Mica, and KDE Blur, alongside Godot's internal shaders.
    /// </summary>
    public partial class EffectsEngine : Node
    {
        public static EffectsEngine Instance { get; private set; }

        private Shader _glassShader;
        private Dictionary<string, ShaderMaterial> _materialCache = new Dictionary<string, ShaderMaterial>();

        // === VINCULACIÓN NATIVA WINDOWS (Win32 DWM API) ===
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38; // Atributo para Windows 11
        private const int DWMSBT_MAINWINDOW = 1;         // Auto (Defecto)
        private const int DWMSBT_TRANSIENTWINDOW = 2;    // Mica
        private const int DWMSBT_TABBEDWINDOW = 4;       // Acrylic completo

        public override void _Ready()
        {
            Instance = this;
            _glassShader = ResourceLoader.Load<Shader>("res://Resources/Shaders/frosted_glass.gdshader");
            GetTree().NodeAdded += OnNodeAdded;
        }

        public override void _ExitTree()
        {
            GetTree().NodeAdded -= OnNodeAdded;
        }

        private HashSet<Control> _frostedControls = new HashSet<Control>();
        private bool _mainBlurEnabled = false;

        /// <summary>
        /// Activa o desactiva el desenfoque del fondo de manera optimizada y nativa según el SO.
        /// </summary>
        public void SetNativeBackgroundBlur(bool enabled, int radius = 30, int windowId = 0)
        {
            if (radius > 30) radius = 30;
            if (radius < 0) radius = 0;

            bool effectiveEnabled = enabled && radius > 0;
            
            if (windowId == 0) _mainBlurEnabled = effectiveEnabled;

            if (OS.GetName() == "Windows")
            {
                ApplyWindowsBlur(effectiveEnabled, windowId);
            }
            else if (OS.GetName() == "Android")
            {
                ApplyAndroidBlur(effectiveEnabled, radius);
            }
            else if (OS.GetName() == "Linux" || OS.GetName() == "FreeBSD")
            {
                ApplyLinuxBlur(effectiveEnabled, windowId);
            }
            
            if (windowId == 0) UpdateKWinRegions();
        }

        private void ApplyWindowsBlur(bool enabled, int windowId)
        {
            long nativeHandle = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, windowId);
            if (nativeHandle == 0) return;

            if (windowId != 0 && nativeHandle == DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, 0))
            {
                return;
            }

            IntPtr hwnd = new IntPtr(nativeHandle);
            int backdropValue = enabled ? DWMSBT_TABBEDWINDOW : DWMSBT_MAINWINDOW;

            int result = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropValue, sizeof(int));
            if (result != 0)
            {
                GD.PushWarning($"[EffectsEngine] Windows DWM no pudo aplicar el atributo. Error: {result}");
            }
        }

        private void ApplyLinuxBlur(bool enabled, int windowId)
        {
            bool isWayland = OS.HasFeature("wayland");

            if (isWayland)
            {
                // Solo para ventana principal en Hyprland por ahora
                if (windowId == 0)
                {
                    string hyprInstance = OS.GetEnvironment("HYPRLAND_INSTANCE_SIGNATURE");
                    if (!string.IsNullOrEmpty(hyprInstance))
                    {
                        string appTitle = (string)ProjectSettings.GetSetting("application/config/name");
                        string rule = enabled ? $"windowrule blur, title:^({appTitle})$" : $"windowrule noblur, title:^({appTitle})$";
                        OS.Execute("hyprctl", new string[] { "dispatch", rule });
                    }
                }
            }
            else 
            {
                string desktop = OS.GetEnvironment("XDG_CURRENT_DESKTOP").ToLower();
                if (desktop.Contains("kde"))
                {
                    long x11WindowId = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, windowId);
                    if (x11WindowId == 0) return;

                    // Si la subventana está incrustada (comparte el handle con la ventana principal), no alterar el blur de la principal
                    if (windowId != 0 && x11WindowId == DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, 0))
                    {
                        return;
                    }
                    
                    if (windowId != 0)
                    {
                        string[] args = enabled 
                            ? new string[] { "-id", x11WindowId.ToString(), "-f", "_KDE_NET_WM_BLUR_BEHIND_REGION", "32c", "-set", "_KDE_NET_WM_BLUR_BEHIND_REGION", "0" }
                            : new string[] { "-id", x11WindowId.ToString(), "-remove", "_KDE_NET_WM_BLUR_BEHIND_REGION" };
                        OS.Execute("xprop", args);
                    }
                }
            }
        }

        private void UpdateKWinRegions()
        {
            if (OS.GetName() != "Linux" && OS.GetName() != "FreeBSD") return;
            string desktop = OS.GetEnvironment("XDG_CURRENT_DESKTOP").ToLower();
            if (!desktop.Contains("kde")) return;

            long x11WindowId = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, 0);
            if (x11WindowId == 0) return;

            if (_mainBlurEnabled)
            {
                OS.Execute("xprop", new string[] { "-id", x11WindowId.ToString(), "-f", "_KDE_NET_WM_BLUR_BEHIND_REGION", "32c", "-set", "_KDE_NET_WM_BLUR_BEHIND_REGION", "0" });
                return;
            }

            List<string> coords = new List<string>();
            foreach (var ctrl in _frostedControls)
            {
                if (ctrl != null && ctrl.IsVisibleInTree())
                {
                    // Solución 4K/QHD/HiDPI: Convertimos las coordenadas lógicas de la UI
                    // a los píxeles FÍSICOS reales de la ventana de X11 que necesita KWin.
                    
                    Window win = ctrl.GetWindow();
                    Vector2 posViewport = ctrl.GetGlobalTransformWithCanvas().Origin;
                    Vector2 sizeViewport = ctrl.GetGlobalTransformWithCanvas().Scale * ctrl.Size;
                    
                    Vector2 viewportSize = win.GetVisibleRect().Size;
                    Vector2 windowSize = new Vector2(win.Size.X, win.Size.Y);
                    
                    // Calculamos la proporción entre la resolución interna y la ventana física real
                    Vector2 scaleRatio = windowSize / viewportSize;

                    int finalX = (int)Math.Round(posViewport.X * scaleRatio.X);
                    int finalY = (int)Math.Round(posViewport.Y * scaleRatio.Y);
                    int finalW = (int)Math.Round(sizeViewport.X * scaleRatio.X);
                    int finalH = (int)Math.Round(sizeViewport.Y * scaleRatio.Y);

                    coords.Add($"{finalX}, {finalY}, {finalW}, {finalH}");
                }
            }

            if (coords.Count > 0)
            {
                string regionsStr = string.Join(", ", coords);
                OS.Execute("xprop", new string[] { "-id", x11WindowId.ToString(), "-f", "_KDE_NET_WM_BLUR_BEHIND_REGION", "32c", "-set", "_KDE_NET_WM_BLUR_BEHIND_REGION", regionsStr });
            }
            else
            {
                OS.Execute("xprop", new string[] { "-id", x11WindowId.ToString(), "-remove", "_KDE_NET_WM_BLUR_BEHIND_REGION" });
            }
        }

        private void ApplyAndroidBlur(bool enabled, int radius)
        {
            if (Engine.HasSingleton("GodotAndroidBlur"))
            {
                var androidPlugin = Engine.GetSingleton("GodotAndroidBlur");
                int finalRadius = enabled ? radius : 0;
                androidPlugin.Call("setBlurRadius", finalRadius);
            }
        }

        /// <summary>
        /// Aplica un shader interno de Godot para elementos UI (ej. Mensaje de Bot).
        /// </summary>
        public void ApplyFrostedGlass(Control target, float blurAmount, Color mixColor, bool enabled)
        {
            if (target == null) 
            {
                GD.PushWarning("[EffectsEngine] Intentó aplicar FrostedGlass a un Control nulo.");
                return;
            }

            if (!enabled || _glassShader == null)
            {
                GD.Print($"[EffectsEngine] Desactivando FrostedGlass para {target.Name}. Enabled: {enabled}, Shader: {(_glassShader != null ? "Cargado" : "Nulo")}");
                target.Material = null;
                if (_frostedControls.Contains(target)) {
                    _frostedControls.Remove(target);
                    target.ItemRectChanged -= UpdateKWinRegions;
                    target.VisibilityChanged -= UpdateKWinRegions;
                    UpdateKWinRegions();
                }
                return;
            }

            string matKey = $"{blurAmount:F1}_{mixColor.R:F2}_{mixColor.G:F2}_{mixColor.B:F2}_{mixColor.A:F2}";
            ShaderMaterial mat;
            
            if (_materialCache.ContainsKey(matKey))
            {
                mat = _materialCache[matKey];
                GD.Print($"[EffectsEngine] Reusando ShaderMaterial (Cache) para {target.Name}. Blur: {blurAmount}");
            }
            else
            {
                mat = new ShaderMaterial();
                mat.Shader = _glassShader;
                mat.SetShaderParameter("blur_amount", blurAmount);
                mat.SetShaderParameter("mix_color", new Color(mixColor.R, mixColor.G, mixColor.B, 0.6f));
                mat.SetShaderParameter("panel_alpha", mixColor.A);
                _materialCache[matKey] = mat;
                GD.Print($"[EffectsEngine] Creando nuevo ShaderMaterial para {target.Name}. Blur: {blurAmount}, MixColor: {mixColor}");
            }

            target.Material = mat;
            
            if (!_frostedControls.Contains(target)) {
                _frostedControls.Add(target);
                target.ItemRectChanged += UpdateKWinRegions;
                target.VisibilityChanged += UpdateKWinRegions;
                UpdateKWinRegions();
            }
            
            GD.Print($"[EffectsEngine] FrostedGlass aplicado con éxito a {target.Name}.");
        }

        private bool _subWindowsEnabled = false;

        private void OnNodeAdded(Node node)
        {
            if (!_subWindowsEnabled) return;
            
            if (node is Window win && win != GetTree().Root)
            {
                win.Transparent = true;
                win.TransparentBg = true;
            }
        }

        public void ApplySubWindowEffects(Node current, bool enableTransparency)
        {
            _subWindowsEnabled = enableTransparency;
            
            if (current is Window win && win != GetTree().Root)
            {
                win.Transparent = enableTransparency;
                win.TransparentBg = enableTransparency;
                
                // Si la transparencia está activada, le pedimos al SO (KWin/DWM) que difumine
                // el escritorio detrás de esta subventana específica.
                // Usamos el ID interno de la ventana (GetWindowId).
                if (enableTransparency && OS.GetName() == "Linux" || OS.GetName() == "FreeBSD")
                {
                    // Forzamos el blur de KWin para TODA la subventana
                    long x11WindowId = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, win.GetWindowId());
                    if (x11WindowId != 0 && x11WindowId != DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, 0))
                    {
                        OS.Execute("xprop", new string[] { "-id", x11WindowId.ToString(), "-f", "_KDE_NET_WM_BLUR_BEHIND_REGION", "32c", "-set", "_KDE_NET_WM_BLUR_BEHIND_REGION", "0" });
                    }
                }
            }

            foreach (Node child in current.GetChildren())
            {
                ApplySubWindowEffects(child, enableTransparency);
            }
        }
    }
}
