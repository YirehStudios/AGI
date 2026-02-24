#!/bin/bash
# AGI Project: Environment Initializer for Linux
# This script ensures the Python environment is ready before Godot interaction.

DATA_DIR="$HOME/.local/share/AGI_Project"
VENV_PATH="$DATA_DIR/Engine/ComfyUI/venv"

# 1. Create directory if not exists
mkdir -p "$DATA_DIR"

# 2. Check if venv exists, if not, notice the user
if [ ! -d "$VENV_PATH" ]; then
    echo "AGI_LOG: Virtual Environment not found. Ready for deployment."
fi

# 3. Ensure internal scripts are executable
# Recursively finds all .sh files in the data directory and makes them executable
find "$DATA_DIR" -name "*.sh" -exec chmod +x {} +

echo "AGI_LOG: init_setup.sh execution complete."