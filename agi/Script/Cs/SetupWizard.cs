using Godot;
using System;
using System.Collections.Generic;
using Logic.System.Config;
using Logic.System.Drivers;
using Logic.Network;

namespace Logic.Utils
{
	/// <summary>
	/// Orchestrates the initial application setup through a State Machine approach,
	/// managing UI transitions, dependency installations, and configuration binding.
	/// </summary>
	public partial class SetupWizard : Control
	{
		public enum WizardState
		{
			Welcome,
			Dependencies,
			ModeSelection,
			ModelSelection,
			Downloading
		}

		[Export] public Control PanelWelcome;
		[Export] public Control PanelDependencies;
		[Export] public Control PanelModeSelection;
		[Export] public Control PanelModelSelection;
		[Export] public Control PanelDownloading;

		[Export] public RichTextLabel TerminalLog;
		[Export] public ProgressBar InstallProgress;
		[Export] public Button BtnComenzar;
		[Export] public Button BtnServidorRemoto;
		[Export] public Button BtnLocalHost;
        [Export] public TextEdit TxtCommandDisplay;
		[Export] public Button BtnCopyCommand;
		[Export] public Label LblRestartWarning;
        [Export] public VBoxContainer ModelListContainer;
		
		[Export] public string MainChatScenePath = "res://Scenes/IAScene/MainApp.tscn";
        [Export] public ProgressBar ModelDownloadProgress;
		[Export] public Label ModelDownloadStatus;
		
		private DownloadManager _downloadManager;

		private WizardState _currentState;
		private DependencyInstaller _dependencyInstaller;
		private ConfigManager _configManager;

		/// <summary>
		/// Inicializa los administradores secundarios y enlaza los eventos de la interfaz de usuario.
		/// Suscribe el manejador local a los eventos de progreso en tiempo real emitidos por el DownloadManager.
		/// </summary>
		public override void _Ready()
		{
			_configManager = GetNode<ConfigManager>("/root/ConfigManager");
			
			_dependencyInstaller = new DependencyInstaller();
			AddChild(_dependencyInstaller);

			_downloadManager = new DownloadManager();
			AddChild(_downloadManager);

			_downloadManager.DownloadCompleted += OnModelDownloadCompleted;
			_downloadManager.DownloadProgress += OnModelDownloadProgress;

			if (BtnComenzar != null)
			{
				BtnComenzar.Pressed += () => SwitchState(WizardState.Dependencies);
			}

			if (BtnServidorRemoto != null)
			{
				BtnServidorRemoto.Pressed += () => SelectRemoteMode("http://127.0.0.1:8080");
			}

			if (BtnLocalHost != null)
			{
				BtnLocalHost.Pressed += SelectLocalMode;
			}

			if (BtnCopyCommand != null)
			{
				BtnCopyCommand.Pressed += OnCopyCommandPressed;
			}

			SwitchState(WizardState.Welcome);
		}

		/// <summary>
		/// Ejecuta la lógica asíncrona de inicialización de estado.
		/// Invoca las operaciones delegadas según el ciclo de vida de la configuración de inicio,
		/// anexando la invocación del administrador de descargas.
		/// </summary>
		/// <param name="state">El estado de la máquina que está siendo procesado.</param>
		private async void HandleStateInitialization(WizardState state)
		{
			switch (state)
			{
				case WizardState.Dependencies:
					var result = await _dependencyInstaller.AuditSystemDependenciesAsync();
					
					if (result.HasDocker)
					{
						SwitchState(WizardState.ModeSelection);
					}
					else
					{
						if (PanelDependencies != null) PanelDependencies.Visible = true;
						
						if (TxtCommandDisplay != null) 
						{
							string displayText = result.RequiredCommand;
							if (displayText.Contains("aria2"))
							{
								displayText = "# Sugerencia: Se ha incluido aria2 en el comando para habilitar descargas de mayor velocidad.\n" + displayText;
							}
							TxtCommandDisplay.Text = displayText;
						}

						if (LblRestartWarning != null)
						{
							LblRestartWarning.Text = "Por favor, ejecuta este comando en tu terminal, luego REINICIA esta aplicación.";
						}
					}
					break;
					
				case WizardState.ModelSelection:
					PopulateModelPresets();
					break;

				case WizardState.Downloading:
					StartModelDownload();
					break;
			}
		}

        /// <summary>
		/// Registra el modelo seleccionado, vincula su URL de origen en la configuración persistente 
		/// e inicia la transición hacia el estado de red.
		/// </summary>
		/// <param name="preset">El objeto de configuración del modelo seleccionado por el usuario.</param>
		private void OnModelSelected(ConfigManager.ModelPreset preset)
		{
			_configManager.ActiveModelName = preset.Name;
			
			if (preset.DownloadLinks != null && preset.DownloadLinks.Count > 0)
			{
				_configManager.ActiveModelUrl = preset.DownloadLinks[0];
			}

			_configManager.SaveConfiguration();
			
			SwitchState(WizardState.Downloading);
		}

		/// <summary>
		/// Prepara el entorno del sistema de archivos, establece la retroalimentación visual 
		/// y delega la ejecución asíncrona de obtención del binario al DownloadManager.
		/// Garantiza la estandarización nominal del archivo en el sistema operativo local.
		/// </summary>
		/// <summary>
		/// Prepara el entorno del sistema de archivos, establece la retroalimentación visual 
		/// y delega la ejecución asíncrona de obtención del binario al DownloadManager.
		/// Garantiza la estandarización nominal del archivo en el sistema operativo local.
		/// </summary>
		private async void StartModelDownload()
		{
			if (ModelDownloadStatus != null)
			{
				ModelDownloadStatus.Text = "Iniciando descarga del modelo...";
			}

			if (ModelDownloadProgress != null)
			{
				ModelDownloadProgress.Value = 0;
			}

			string safeFileName = _configManager.ActiveModelName.Replace(" ", "_") + ".gguf";
			
			_configManager.ActiveModelPath = ProjectSettings.GlobalizePath("user://models/" + safeFileName);
			_configManager.SaveConfiguration();

			await _downloadManager.DownloadFileAsync(_configManager.ActiveModelUrl, "user://models", safeFileName);
		}

		/// <summary>
		/// Manejador suscrito al evento de actualización de progreso del DownloadManager.
		/// Refleja de forma interpolada la transferencia de bytes sobre los componentes de interfaz de usuario.
		/// </summary>
		/// <param name="fileName">Identificador físico del archivo en tránsito.</param>
		/// <param name="percentage">Fracción procesada respecto a la longitud de contenido total reportada (0 - 100).</param>
		private void OnModelDownloadProgress(string fileName, float percentage)
		{
			if (ModelDownloadProgress != null)
			{
				ModelDownloadProgress.Value = percentage;
			}

			if (ModelDownloadStatus != null)
			{
				ModelDownloadStatus.Text = $"Descargando {fileName}... {percentage:F1}%";
			}
		}

		/// <summary>
		/// Manejador de eventos que evalúa el resultado de la transferencia binaria, realizando la transición
		/// de escena en caso de éxito o restaurando la interfaz de forma explícita ante un fallo de integridad o red.
		/// </summary>
		/// <param name="fileName">Identificador del archivo procesado.</param>
		/// <param name="success">Bandera de confirmación de integridad pos-descarga.</param>
		private void OnModelDownloadCompleted(string fileName, bool success)
		{
			if (success)
			{
				if (ModelDownloadStatus != null)
				{
					ModelDownloadStatus.Text = "Descarga completada con éxito. Inicializando entorno...";
				}
				
				if (ModelDownloadProgress != null)
				{
					ModelDownloadProgress.Value = ModelDownloadProgress.MaxValue;
				}

				TransitionToMainScene();
			}
			else
			{
				if (ModelDownloadStatus != null)
				{
					ModelDownloadStatus.Text = "Error en la descarga. Por favor, reinicia la aplicación o verifica tu conexión.";
				}

				if (ModelDownloadProgress != null)
				{
					ModelDownloadProgress.Value = 0;
				}
				
				GD.PrintErr($"SetupWizard: Fallo reportado por DownloadManager durante la obtención de {fileName}");
			}
		}

		/// <summary>
		/// Transfiere el comando bash generado al portapapeles del servidor gráfico (DisplayServer).
		/// Intercala un temporizador no bloqueante en el árbol de escenas para proveer retroalimentación
		/// visual efímera en el botón de copia, restaurando su estado original posteriormente.
		/// </summary>
		private async void OnCopyCommandPressed()
		{
			if (TxtCommandDisplay != null && !string.IsNullOrEmpty(TxtCommandDisplay.Text))
			{
				DisplayServer.ClipboardSet(TxtCommandDisplay.Text);
				GD.Print("SetupWizard: Comando bash copiado al portapapeles de forma exitosa.");

				if (BtnCopyCommand != null)
				{
					string originalText = BtnCopyCommand.Text;
					BtnCopyCommand.Text = "¡Copiado!";
					
					// Retardo asíncrono atado al ciclo de ejecución de Godot
					await ToSignal(GetTree().CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);
					
					BtnCopyCommand.Text = originalText;
				}
			}
		}

		/// <summary>
		/// Transitions the internal state and updates the visibility of the corresponding UI panels.
		/// </summary>
		/// <param name="newState">The target state to transition into.</param>
		public void SwitchState(WizardState newState)
		{
			_currentState = newState;

			if (PanelWelcome != null) PanelWelcome.Visible = (newState == WizardState.Welcome);
			if (PanelDependencies != null) PanelDependencies.Visible = (newState == WizardState.Dependencies);
			if (PanelModeSelection != null) PanelModeSelection.Visible = (newState == WizardState.ModeSelection);
			if (PanelModelSelection != null) PanelModelSelection.Visible = (newState == WizardState.ModelSelection);
			if (PanelDownloading != null) PanelDownloading.Visible = (newState == WizardState.Downloading);

			HandleStateInitialization(newState);
		}

		/// <summary>
		/// Executes specific logic required when entering a new state.
		/// </summary>
		/// <param name="state">The state being initialized.</param>

		/// <summary>
		/// Appends real-time output from the dependency installation process to the UI log
		/// and automatically scrolls to the latest entry.
		/// </summary>
		/// <param name="logLine">The raw terminal output string.</param>

		private async void ScrollToBottom()
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			
			if (TerminalLog != null)
			{
				var scrollBar = TerminalLog.GetVScrollBar();
				if (scrollBar != null)
				{
					scrollBar.Value = scrollBar.MaxValue;
				}
			}
		}

		/// <summary>
		/// Evaluates the outcome of the installation sequence and determines the next state transition.
		/// </summary>
		/// <param name="success">Indicates whether the installation process exited gracefully.</param>
		private void OnInstallationCompleted(bool success)
		{
			if (success)
			{
				SwitchState(WizardState.ModeSelection);
			}
			else
			{
				if (TerminalLog != null)
				{
					TerminalLog.AppendText("\n[SYSTEM] Installation failed. Please review the logs above.\n");
				}
			}
		}

		/// <summary>
		/// Triggers the transition to the remote configuration flow.
		/// Connect this method to the 'Remote Mode' Button's Pressed signal.
		/// </summary>
		public void SelectRemoteMode(string hostUrl)
		{
			_configManager.CurrentMode = ConfigManager.AppMode.RemoteUI;
			_configManager.RemoteHostUrl = hostUrl;
			_configManager.SaveConfiguration();
			
			TransitionToMainScene();
		}

		/// <summary>
		/// Triggers the transition to the local configuration flow.
		/// Connect this method to the 'Local Mode' Button's Pressed signal.
		/// </summary>
		public void SelectLocalMode()
		{
			_configManager.CurrentMode = ConfigManager.AppMode.LocalHost;
			SwitchState(WizardState.ModelSelection);
		}

		/// <summary>
		/// Retrieves pre-configured models from the ConfigManager to populate the UI.
		/// </summary>
		/// <summary>
		/// Limpia el contenedor de nodos de forma iterativa y espera la resolución de la tarea asíncrona 
		/// de obtención de datos del ConfigManager. Posteriormente instancia dinámicamente los contenedores 
		/// y controles gráficos de la lista de modelos.
		/// </summary>
		private async void PopulateModelPresets()
		{
			if (ModelListContainer != null)
			{
				foreach (Node child in ModelListContainer.GetChildren())
				{
					child.QueueFree();
				}
			}

			List<ConfigManager.ModelPreset> presets = await _configManager.GetOrDownloadPresetsAsync();
			
			if (presets == null || presets.Count == 0)
			{
				Label noModelsLabel = new Label
				{
					Text = "No se encontraron modelos. Verifica tu conexión a internet o el archivo presets.json."
				};
				
				if (ModelListContainer != null)
				{
					ModelListContainer.AddChild(noModelsLabel);
				}
				
				GD.PrintErr("SetupWizard: La operación asíncrona retornó una lista de presets vacía o nula.");
				return;
			}

			foreach (ConfigManager.ModelPreset preset in presets)
			{
				PanelContainer cardPanel = new PanelContainer();
				HBoxContainer cardLayout = new HBoxContainer();
				VBoxContainer textContainer = new VBoxContainer();
				
				textContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

				Label nameLabel = new Label
				{
					Text = preset.Name
				};

				Label descLabel = new Label
				{
					Text = preset.Description
				};

				Button actionButton = new Button
				{
					Text = "Descargar / Seleccionar"
				};

				actionButton.Pressed += () => OnModelSelected(preset);

				textContainer.AddChild(nameLabel);
				textContainer.AddChild(descLabel);
				
				cardLayout.AddChild(textContainer);
				cardLayout.AddChild(actionButton);
				
				cardPanel.AddChild(cardLayout);
				
				if (ModelListContainer != null)
				{
					ModelListContainer.AddChild(cardPanel);
				}
			}
			
			GD.Print($"SetupWizard: Población asíncrona finalizada. Se renderizaron {presets.Count} presets en la interfaz.");
		}

		/// <summary>
		/// Validates the selected model's integrity before allowing application progression.
		/// </summary>
		/// <param name="expectedSize">The required byte size for validation.</param>
		public void ConfirmModelSelection(long expectedSize)
		{
			var validationResult = _configManager.ValidateModelIntegrity(expectedSize);

			if (!validationResult.IsValid)
			{
				GD.PrintErr($"SetupWizard: Validation Error - {validationResult.ErrorMessage}");
				
				// Expected Implementation:
				// Show an error dialog to the user requesting a re-download or path correction.
				// Optionally transition to WizardState.Downloading if a download is initiated.
				return;
			}

			_configManager.SaveConfiguration();
			TransitionToMainScene();
		}

		/// <summary>
		/// Finalizes the setup sequence and swaps the active scene.
		/// </summary>
		private void TransitionToMainScene()
		{
			GetTree().ChangeSceneToFile(MainChatScenePath);
		}
	}
}
