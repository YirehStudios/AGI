using Godot;

namespace Logic.UI
{
    public partial class ThemeManager : Node
    {
        public static ThemeManager Instance { get; private set; }
        public bool EsModoOscuro { get; private set; } // Memoria global

        private Theme _temaClaro;
        private Theme _temaOscuro;

        // Dentro de ThemeManager.cs, actualiza el _Ready:
        public override void _Ready()
        {
            Instance = this;

            // Conectamos el archivo que acabas de hackear (Modo Claro)
            _temaClaro = ResourceLoader.Load<Theme>("res://Resources/UI_Themes/tema_claro.tres");

            // Conectamos el archivo original oscuro de Passivestar (Modo Oscuro)
            _temaOscuro = ResourceLoader.Load<Theme>("res://Resources/UI_Themes/minimal_theme.tres");
            
            var effects = new Logic.UI.Effects.EffectsEngine();
            effects.Name = "EffectsEngine";
            AddChild(effects);

            // Aplicar transparencia global al iniciar
            CallDeferred(nameof(ApplyTransMode));

            // Hook into NodeAdded to apply settings to dynamically spawned Popups/Windows and Text Nodes
            GetTree().NodeAdded += OnNodeAdded;
            
            // Aplicar a los nodos que ya están en la escena
            CallDeferred(nameof(AplicarBordesMasivosDeffered));
        }

        private void AplicarBordesMasivosDeffered()
        {
            AplicarBordesMasivos(GetTree().Root);
        }

        private void AplicarBordesMasivos(Node nodoRaiz)
        {
            if (nodoRaiz == null) return;

            AplicarBorde(nodoRaiz);

            foreach (Node hijo in nodoRaiz.GetChildren())
            {
                AplicarBordesMasivos(hijo);
            }
        }

        private void OnNodeAdded(Node node)
        {
            if (node is Window win)
            {
                var config = Logic.System.Config.ConfigManager.Instance;
                if (config != null && config.TransModeApplyToSubWindows)
                {
                    win.Transparent = true;
                    win.TransparentBg = true;
                }
            }

            AplicarBorde(node);
        }

        private void AplicarBorde(Node nodo)
        {
            if (nodo is Control control)
            {
                bool aplicar = false;

                if (nodo is Label || nodo is RichTextLabel || nodo is Button || nodo is LineEdit || nodo is TextEdit)
                {
                    aplicar = true;
                }

                if (aplicar)
                {
                    // El error anterior ocurría porque Godot por defecto devuelve Blanco (1,1,1) al pedir el color
                    // de la fuente si no hay override, causando que pusiera bordes negros a textos negros.
                    // Ahora nos basamos en la variable global del ThemeManager:
                    // EsModoOscuro = true -> Texto Blanco -> Borde Negro
                    // EsModoOscuro = false -> Texto Negro -> Borde Blanco
                    
                    Color colorBorde = EsModoOscuro 
                        ? new Color(0f, 0f, 0f, 0.5f)  // Borde negro suave y semi-transparente
                        : new Color(1f, 1f, 1f, 0.8f); // Borde blanco suave

                    // Aplicamos un grosor mucho más fino para que no se deformen las letras
                    control.AddThemeColorOverride("font_outline_color", colorBorde);
                    control.AddThemeConstantOverride("outline_size", 2); 

                    // Solo a los Labels y RichTextLabels les ponemos la sombra extra de tu imagen de referencia
                    if (nodo is Label || nodo is RichTextLabel)
                    {
                        control.AddThemeColorOverride("font_shadow_color", new Color(colorBorde.R, colorBorde.G, colorBorde.B, 0.3f));
                        control.AddThemeConstantOverride("shadow_offset_x", 1);
                        control.AddThemeConstantOverride("shadow_offset_y", 1);
                    }
                }
            }
        }
        public Theme ObtenerTemaGlobal(bool esOscuro)
        {
            EsModoOscuro = esOscuro; // Guardamos el modo actual
            return esOscuro ? _temaOscuro : _temaClaro;
        }

        // EL EXTRACTOR A PRUEBA DE BALAS: Saca el diseño directo del archivo .theme
        public StyleBox ExtraerEstiloDirecto(string nombreVariacion)
        {
            Theme temaActivo = EsModoOscuro ? _temaOscuro : _temaClaro;
            if (temaActivo != null && temaActivo.HasStylebox("panel", nombreVariacion))
            {
                return temaActivo.GetStylebox("panel", nombreVariacion);
            }
            return new StyleBoxFlat(); // Fallback por si escribes mal el nombre
        }

        public void ApplyTransMode()
        {
            var config = Logic.System.Config.ConfigManager.Instance;
            if (config == null) return;

            var root = GetTree().Root;
            MainApp mainApp = null;
            
            var mainApps = GetTree().GetNodesInGroup("MainAppGroup");
            if (mainApps.Count > 0) mainApp = mainApps[0] as MainApp;
            if (mainApp == null) mainApp = root.FindChild("MainApp", true, false) as MainApp;

            root.TransparentBg = config.TransModeEnabled;

            var shader = ResourceLoader.Load<Shader>("res://Resources/Shaders/frosted_glass.gdshader");

            Color mixColorMain = EsModoOscuro ? new Color(0.05f, 0.05f, 0.08f, config.TransModeOpacity) 
                                              : new Color(0.96f, 0.96f, 0.97f, config.TransModeOpacity);

            Color mixColorPopups = EsModoOscuro ? new Color(0.05f, 0.05f, 0.08f, config.TransModePopupsOpacity) 
                                                : new Color(0.96f, 0.96f, 0.97f, config.TransModePopupsOpacity);

            Color mixColorSubWindows = EsModoOscuro ? new Color(0.05f, 0.05f, 0.08f, config.TransModeSubWindowsOpacity) 
                                                : new Color(0.96f, 0.96f, 0.97f, config.TransModeSubWindowsOpacity);



            Theme temaActivo = ObtenerTemaGlobal(EsModoOscuro);
            if (temaActivo != null && temaActivo.HasStylebox("panel", "Panel"))
            {
                var baseStyle = (StyleBoxFlat)temaActivo.GetStylebox("panel", "Panel");
                
                var modStyleMain = (StyleBoxFlat)baseStyle.Duplicate();
                Color cMain = modStyleMain.BgColor;
                cMain.A = config.TransModeEnabled ? config.TransModeOpacity : 1.0f;
                modStyleMain.BgColor = cMain;

                var modStyleSubWindows = (StyleBoxFlat)baseStyle.Duplicate();
                Color cSubWindows = modStyleSubWindows.BgColor;
                cSubWindows.A = config.TransModeApplyToSubWindows ? config.TransModeSubWindowsOpacity : 1.0f;
                modStyleSubWindows.BgColor = cSubWindows;

                temaActivo.SetStylebox("panel", "Panel", modStyleMain);
                temaActivo.SetStylebox("panel", "Siderbard", modStyleMain);

                temaActivo.SetStylebox("panel", "PopupMenu", modStyleSubWindows);
                temaActivo.SetStylebox("panel", "PopupDialog", modStyleSubWindows);
                temaActivo.SetStylebox("panel", "Window", modStyleSubWindows);
            }

            // Para los paneles principales (ventana al OS), no usamos ShaderMaterial ya que SCREEN_TEXTURE
            // choca con TransparentBg. Simplemente bajamos el SelfModulate para dejar ver el escritorio (que KDE blurrea).
            if (mainApp != null && mainApp.ContentContainer != null)
            {
                mainApp.ContentContainer.Material = null;
                mainApp.ContentContainer.SelfModulate = config.TransModeEnabled 
                    ? new Color(1f, 1f, 1f, 0f) 
                    : new Color(1f, 1f, 1f, 1f);
            }

            if (mainApp != null && mainApp.SidebarContainer != null)
            {
                mainApp.SidebarContainer.Material = null;
                mainApp.SidebarContainer.SelfModulate = config.TransModeEnabled 
                    ? new Color(1f, 1f, 1f, 0f) 
                    : new Color(1f, 1f, 1f, 1f);
            }

            if (mainApp != null)
            {
                mainApp.SelfModulate = config.TransModeEnabled 
                    ? new Color(1f, 1f, 1f, config.TransModeOpacity) 
                    : new Color(1f, 1f, 1f, 1f);

                var backgroundPanel = mainApp.GetNodeOrNull<Panel>("Panel");
                if (backgroundPanel != null)
                {
                    backgroundPanel.Visible = !config.TransModeEnabled;
                }
            }


            if (temaActivo != null)
            {
                // =========================================================
                // 1. ARREGLAR HSLIDER Y CHECKBUTTON EN MODO OSCURO/CLARO
                // =========================================================
                var sliderGrabber = new StyleBoxFlat();
                sliderGrabber.BgColor = new Color(0.274f, 0.623f, 0.924f); // Azul
                sliderGrabber.CornerRadiusTopLeft = 8;
                sliderGrabber.CornerRadiusTopRight = 8;
                sliderGrabber.CornerRadiusBottomLeft = 8;
                sliderGrabber.CornerRadiusBottomRight = 8;
                sliderGrabber.ExpandMarginTop = 4;
                sliderGrabber.ExpandMarginBottom = 4;
                
                var sliderGrabberHigh = (StyleBoxFlat)sliderGrabber.Duplicate();
                sliderGrabberHigh.BgColor = new Color(0.4f, 0.7f, 1.0f); // Azul claro

                var sliderTrack = new StyleBoxFlat();
                sliderTrack.BgColor = EsModoOscuro ? new Color(0.2f, 0.2f, 0.25f) : new Color(0.8f, 0.8f, 0.85f);
                sliderTrack.CornerRadiusTopLeft = 8;
                sliderTrack.CornerRadiusTopRight = 8;
                sliderTrack.CornerRadiusBottomLeft = 8;
                sliderTrack.CornerRadiusBottomRight = 8;
                sliderTrack.ExpandMarginTop = 4;
                sliderTrack.ExpandMarginBottom = 4;

                temaActivo.SetStylebox("slider", "HSlider", sliderTrack);
                temaActivo.SetStylebox("grabber_area", "HSlider", sliderGrabber);
                temaActivo.SetStylebox("grabber_area_highlight", "HSlider", sliderGrabberHigh);
            }

            // =========================================================
            // 3. SHADER DE BLUR PARA POPUPS (Settings, Files, etc)
            // =========================================================
            var effects = GetNodeOrNull<Logic.UI.Effects.EffectsEngine>("EffectsEngine");
            if (effects != null)
            {
                // Native background blur applies to the OS compositor
                effects.SetNativeBackgroundBlur(config.TransModeEnabled, (int)config.TransModeBlur);

                if (mainApp != null)
                {
                    // Set properly tinted StyleBoxes instead of relying on the Godot shader
                    var popupStyle = new StyleBoxFlat();
                    popupStyle.CornerRadiusTopLeft = 12;
                    popupStyle.CornerRadiusTopRight = 12;
                    popupStyle.CornerRadiusBottomLeft = 12;
                    popupStyle.CornerRadiusBottomRight = 12;
                    popupStyle.ContentMarginLeft = 24;
                    popupStyle.ContentMarginTop = 24;
                    popupStyle.ContentMarginRight = 24;
                    popupStyle.ContentMarginBottom = 24;

                    if (config.TransModeApplyToPopups)
                    {
                        // Pasamos el color nativo real al BgColor para que el Shader pueda leerlo
                        // a través de la variable COLOR.rgb y no pierda la estética nativa al llegar al 100% de opacidad.
                        popupStyle.BgColor = EsModoOscuro ? new Color(0.12f, 0.12f, 0.15f, 1.0f) : new Color(0.9f, 0.9f, 0.92f, 1.0f);
                        mainApp.SettingsPanel.AddThemeStyleboxOverride("panel", popupStyle);
                        mainApp.FilesPanel.AddThemeStyleboxOverride("panel", popupStyle);

                        mainApp.SettingsPanel.SelfModulate = new Color(1, 1, 1, 1);
                        mainApp.FilesPanel.SelfModulate = new Color(1, 1, 1, 1);

                        effects.ApplyFrostedGlass(mainApp.SettingsPanel, config.TransModePopupsBlur, mixColorPopups, true);
                        effects.ApplyFrostedGlass(mainApp.FilesPanel, config.TransModePopupsBlur, mixColorPopups, true);
                    }
                    else
                    {
                        // Modo sólido: Aplicamos un color opaco y apagamos el shader
                        popupStyle.BgColor = EsModoOscuro ? new Color(0.12f, 0.12f, 0.15f, 1.0f) : new Color(0.9f, 0.9f, 0.92f, 1.0f);
                        mainApp.SettingsPanel.AddThemeStyleboxOverride("panel", popupStyle);
                        mainApp.FilesPanel.AddThemeStyleboxOverride("panel", popupStyle);

                        mainApp.SettingsPanel.SelfModulate = new Color(1, 1, 1, 1);
                        mainApp.FilesPanel.SelfModulate = new Color(1, 1, 1, 1);

                        effects.ApplyFrostedGlass(mainApp.SettingsPanel, 0, mixColorPopups, false);
                        effects.ApplyFrostedGlass(mainApp.FilesPanel, 0, mixColorPopups, false);
                    }
                }

                effects.ApplySubWindowEffects(root, config.TransModeApplyToSubWindows);
            }
        }

    }
}