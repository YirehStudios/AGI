using Godot;
using System;
using System.Collections.Generic;

namespace Logic.UI
{
    /// <summary>
    /// Represents the category of the engine currently being configured.
    /// </summary>
    public enum ModelCategory
    {
        LLM,
        STT,
        TTS,
        ImageGen,
        VideoGen
    }

    /// <summary>
    /// Data structure defining an individual model option.
    /// </summary>
    public class ModelItemData
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string TargetExecutable { get; set; }
    }

    /// <summary>
    /// Data payload required to construct the dynamic selection panel.
    /// </summary>
    public class PanelDisplayData
    {
        public string Title { get; set; }
        public ModelCategory Category { get; set; }
        public List<ModelItemData> Items { get; set; } = new List<ModelItemData>();
    }

    public partial class DynamicSelectionPanel : Panel
    {
        [Signal]
        public delegate void ModelConfirmedEventHandler(int categoryIndex, string modelName, string targetExecutable);

        [Export] public Label TitleLabel;
        [Export] public VBoxContainer ItemContainer;

        private ModelCategory _currentCategory;
        private ModelItemData _selectedModelData;
        private List<Button> _activeButtons = new List<Button>();


        /// <summary>
        /// Purges old node hierarchies and structurally populates the container layout using the incoming data payload.
        /// Features defensive null-guards on container targets to prevent runtime reference exceptions.
        /// </summary>
        public void LoadPanelData(PanelDisplayData data)
        {
            if (data == null) return;

            _currentCategory = data.Category;
            _selectedModelData = null;
            _activeButtons.Clear();

            if (TitleLabel != null)
            {
                TitleLabel.Text = data.Title;
            }

            if (ItemContainer != null)
            {
                foreach (Node child in ItemContainer.GetChildren())
                {
                    child.QueueFree();
                }
            }

            foreach (var item in data.Items)
            {
                if (item == null) continue;

                PanelContainer cardPanel = new PanelContainer();
                MarginContainer margin = new MarginContainer();

                margin.AddThemeConstantOverride("margin_left", 15);
                margin.AddThemeConstantOverride("margin_top", 15);
                margin.AddThemeConstantOverride("margin_right", 15);
                margin.AddThemeConstantOverride("margin_bottom", 15);

                HBoxContainer cardLayout = new HBoxContainer();
                cardLayout.AddThemeConstantOverride("separation", 20);

                VBoxContainer textContainer = new VBoxContainer
                {
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
                };

                Label nameLabel = new Label { Text = item.Name };
                nameLabel.AddThemeFontSizeOverride("font_size", 20);

                Label descLabel = new Label
                {
                    Text = item.Description,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart
                };
                descLabel.CustomMinimumSize = new Vector2(350, 0);
                descLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));

                textContainer.AddChild(nameLabel);
                textContainer.AddChild(descLabel);

                Button selectButton = new Button
                {
                    Text = "Seleccionar",
                    ToggleMode = true,
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                    CustomMinimumSize = new Vector2(150, 50)
                };

                _activeButtons.Add(selectButton);

                selectButton.Toggled += (isPressed) =>
                {
                    if (isPressed)
                    {
                        ProcessSelection(item, selectButton);
                    }
                    else if (_selectedModelData == item)
                    {
                        _selectedModelData = null;
                        selectButton.Text = "Seleccionar";
                    }
                };

                cardLayout.AddChild(textContainer);
                cardLayout.AddChild(selectButton);

                margin.AddChild(cardLayout);
                cardPanel.AddChild(margin);

                if (ItemContainer != null)
                {
                    ItemContainer.AddChild(cardPanel);
                }
            }
        }

        /// <summary>
        /// Updates the visual selection state of the dynamic button matrix and triggers immediate confirmation propagation.
        /// </summary>
        private void ProcessSelection(ModelItemData selectedItem, Button clickedButton)
        {
            if (selectedItem == null || clickedButton == null) return;

            _selectedModelData = selectedItem;

            foreach (Button btn in _activeButtons)
            {
                if (btn != clickedButton)
                {
                    btn.SetPressedNoSignal(false);
                    btn.Text = "Seleccionar";
                }
            }

            clickedButton.Text = "¡Seleccionado!";

            OnConfirmPressed();
        }

        /// <summary>
        /// Emits the validated selection data back to the primary SetupWizard orchestrator.
        /// </summary>
        private void OnConfirmPressed()
        {
            if (_selectedModelData != null)
            {
                EmitSignal(SignalName.ModelConfirmed, (int)_currentCategory, _selectedModelData.Name, _selectedModelData.TargetExecutable);
            }
        }
    }
}