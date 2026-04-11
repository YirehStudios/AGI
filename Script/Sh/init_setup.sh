#!/bin/bash
# AGI Project: Environment Initializer for Linux
# This script ensures the required AGI binaries are present and executable.

DATA_DIR="$HOME/.local/share/AGI_Project"
ENGINE_DIR="$DATA_DIR/Engine"

# 1. Create directory if not exists
mkdir -p "$DATA_DIR"

# 2. Check if essential binaries exist
if [ ! -f "$ENGINE_DIR/llama-server" ] || [ ! -f "$ENGINE_DIR/whisper-cli" ] || [ ! -f "$ENGINE_DIR/piper" ]; then
    echo "AGI_LOG: Required binaries missing. Ready for deployment."
fi

# 3. Ensure internal binaries are executable
find "$ENGINE_DIR" -type f -name "llama-server" -exec chmod +x {} +
find "$ENGINE_DIR" -type f -name "whisper-cli" -exec chmod +x {} +
find "$ENGINE_DIR" -type f -name "piper" -exec chmod +x {} +

echo "AGI_LOG: init_setup.sh execution complete."