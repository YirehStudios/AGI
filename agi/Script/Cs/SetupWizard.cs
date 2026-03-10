using Godot;
using System;
using System.Collections.Generic;
using Logic.System.Config;
using Logic.System.Drivers;

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
		
		[Export] public string MainChatScenePath = "res://Scenes/IAScene/MainApp.tscn";

		private WizardState _currentState;
		private DependencyInstaller _dependencyInstaller;
		private ConfigManager _configManager;

		public override void _Ready()
		{
			_configManager = GetNode<ConfigManager>("/root/ConfigManager");
			
			_dependencyInstaller = new DependencyInstaller();
			AddChild(_dependencyInstaller);
			
			_dependencyInstaller.TerminalOutputReceived += OnTerminalOutputReceived;
			_dependencyInstaller.InstallationCompleted += OnInstallationCompleted;

			// Vincula mediante delegados y lambdas las transiciones de estado a los eventos 'Pressed' de los botones exportados
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

			SwitchState(WizardState.Welcome);
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
		private void HandleStateInitialization(WizardState state)
		{
			switch (state)
			{
				case WizardState.Dependencies:
					if (TerminalLog != null) TerminalLog.Text = string.Empty;
					_ = _dependencyInstaller.BeginInstallationAsync();
					break;
					
				case WizardState.ModelSelection:
					PopulateModelPresets();
					break;
			}
		}

		/// <summary>
		/// Appends real-time output from the dependency installation process to the UI log
		/// and automatically scrolls to the latest entry.
		/// </summary>
		/// <param name="logLine">The raw terminal output string.</param>
		private void OnTerminalOutputReceived(string logLine)
		{
			if (TerminalLog != null)
			{
				TerminalLog.AppendText(logLine + "\n");
				ScrollToBottom(); // Llamamos al método seguro
			}

			if (InstallProgress != null)
			{
				InstallProgress.Value += 2;
			}
		}

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
		private void PopulateModelPresets()
		{
			List<ConfigManager.ModelPreset> presets = _configManager.ListAvailablePresets();
			
			// Expected Implementation:
			// Iterate over 'presets' and instantiate UI elements within PanelModelSelection.
			// Bind the selection event to trigger 'ConfirmModelSelection(preset)'.
			
			GD.Print($"SetupWizard: Loaded {presets.Count} model presets for UI population.");
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
