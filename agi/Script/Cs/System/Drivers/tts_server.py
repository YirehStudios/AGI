"""
tts_server.py - WebSocket server that utilizes Kokoro ONNX to generate TTS audio for Godot clients.
Version 1.1.0.0
"""

import asyncio
import websockets
import json
import soundfile as sf
import io
import argparse
import os
import sys

# Force Vulkan prioritization (if applicable via environment, otherwise handled in providers)
# (CUDA environment variables removed as requested)

from websockets.exceptions import ConnectionClosed
from kokoro_onnx import Kokoro

import threading
import time
import os

def watch_parent_process():
    # En Linux, un proceso huérfano es adoptado por init (PID 1) o systemd.
    # Si detectamos que el parent_pid es 1, o que Godot ya no existe, nos suicidamos.
    initial_ppid = os.getppid()
    while True:
        current_ppid = os.getppid()
        if current_ppid == 1 or (initial_ppid != 1 and current_ppid != initial_ppid):
            print(f"Parent process {initial_ppid} died. Auto-terminating.", flush=True)
            os._exit(0)
        time.sleep(2)

threading.Thread(target=watch_parent_process, daemon=True).start()


def parse_arguments() -> argparse.Namespace:
    """
    Parses command line arguments required for server configuration.
    
    Returns:
        argparse.Namespace: An object containing the parsed arguments 'port' and 'models_dir'.
    """
    parser = argparse.ArgumentParser(description="Kokoro ONNX TTS WebSocket Server")
    parser.add_argument("--port", type=int, default=8888, help="Port to bind the WebSocket server to.")
    parser.add_argument("--models-dir", type=str, default="", help="Directory containing model.onnx and voices_python.bin.")
    return parser.parse_args()

def initialize_kokoro(models_dir: str) -> tuple[Kokoro, list[str]]:
    """
    Initializes the Kokoro TTS engine and extracts available voice profiles.
    
    Args:
        models_dir (str): The directory containing the required ONNX model and voice binaries.
        
    Returns:
        tuple[Kokoro, list[str]]: The initialized Kokoro instance and a list of available voice keys.
        
    Raises:
        SystemExit: If the initialization fails due to missing files or corrupted binaries.
    """
    model_path = os.path.join(models_dir, "model.onnx") if models_dir else "model.onnx"
    # IMPORTANT: Use the specific Python voices file to avoid C++ ABI collisions
    voices_path = os.path.join(models_dir, "voices_python.bin") if models_dir else "voices_python.bin"

    try:
        print(f"Loading Kokoro ONNX from: {model_path}")
        print(f"Loading Python Voices from: {voices_path}")
        
        kokoro = Kokoro(model_path, voices_path)
        
        # Dynamically extract available voices
        available_voices = list(kokoro.get_voices())
        print(f"Initialization successful. Loaded {len(available_voices)} voices:")
        for voice in available_voices:
            print(f" - {voice}")
            
        if not available_voices:
            print("Warning: No voices found in the binary. The server might fail to generate audio.")
            
        return kokoro, available_voices
        
    except Exception as e:
        print(f"Critical failure starting Kokoro: {e}", file=sys.stderr)
        sys.exit(1)

async def tts_handler(websocket, kokoro: Kokoro, available_voices: list[str], *args, **kwargs) -> None:
    """
    Handles incoming WebSocket connections, processes TTS generation requests,
    and streams the resulting audio back to the client.
    Enforces a hardcoded primary voice profile to guarantee system stability.
    
    Args:
        websocket: The active WebSocket connection object.
        kokoro (Kokoro): The initialized TTS engine instance.
        available_voices (list[str]): A collection of loaded voice keys for fallback validation.
    """
    client_address = getattr(websocket, 'remote_address', 'Unknown Client')
    print(f"Client connected to TTS bridge from {client_address}.")
    
    # Strictly define the required architectural voice profile
    primary_voice = "ef_dora"
    fallback_voice = available_voices[0] if available_voices else "es_es"
    
    try:
        async for message in websocket:
            try:
                data = json.loads(message)
                text = data.get("text", "").strip()
                
                # Deliberately ignore data.get("voice") to prevent runtime crashes from the client
                
                if not text:
                    continue
                
                print(f"Generating audio for text: '{text[:50]}...'")
                
                try:
                    # Attempt synthesis with the hardcoded primary voice
                    audio, sample_rate = kokoro.create(text, voice=primary_voice, speed=1.0, lang="es")
                    print(f"Synthesis successful using primary voice: '{primary_voice}'")
                except Exception as primary_ex:
                    # Execute fallback logic if the primary voice fails or is missing from the binary
                    print(f"Warning: Primary voice '{primary_voice}' failed ({primary_ex}). Falling back to '{fallback_voice}'.", file=sys.stderr)
                    audio, sample_rate = kokoro.create(text, voice=fallback_voice, speed=1.0, lang="es")
                    print(f"Synthesis successful using fallback voice: '{fallback_voice}'")
                
                # Convert raw numpy array to PCM bytes (WAV format)
                with io.BytesIO() as wav_io:
                    sf.write(wav_io, audio, sample_rate, format='WAV')
                    wav_bytes = wav_io.getvalue()
                    
                # Send binary audio back to Godot via WebSocket
                await websocket.send(wav_bytes)
                print(f"Audio payload ({len(wav_bytes)} bytes) successfully transmitted.")
                
            except json.JSONDecodeError:
                print("Error: Received malformed JSON payload.", file=sys.stderr)
            except Exception as e:
                print(f"Error processing TTS generation: {e}", file=sys.stderr)
                
    except ConnectionClosed:
        print(f"Client {client_address} disconnected cleanly.")
    except Exception as e:
        print(f"Unexpected connection error with {client_address}: {e}", file=sys.stderr)
    finally:
        print(f"Connection with {client_address} closed.")

async def main() -> None:
    """
    Main entry point for the server. Configures the environment, initializes the TTS engine,
    and starts the asynchronous WebSocket listener.
    """
    args = parse_arguments()
    kokoro, available_voices = initialize_kokoro(args.models_dir)
    
    # Wrapper to pass kokoro and available_voices to the handler while handling potential path arguments from older websockets
    async def handler_wrapper(websocket, *args, **kwargs):
        await tts_handler(websocket, kokoro, available_voices, *args, **kwargs)
    
    print(f"Starting Kokoro TTS Server on ws://127.0.0.1:{args.port} ...")
    
    try:
        async with websockets.serve(handler_wrapper, "127.0.0.1", args.port):
            await asyncio.Future()  # Run forever
    except Exception as e:
        print(f"Server encountered a critical error: {e}", file=sys.stderr)

if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\nServer shutdown requested by user.")
        sys.exit(0)
