import asyncio
import websockets
import json
import soundfile as sf
import io
import argparse
import os
from kokoro_onnx import Kokoro

# Configure command line arguments
parser = argparse.ArgumentParser()
parser.add_argument("--port", type=int, default=8888)
parser.add_argument("--models-dir", type=str, default="") 
args = parser.parse_args()

# Determine model paths
model_path = os.path.join(args.models_dir, "model.onnx") if args.models_dir else "model.onnx"
# IMPORTANT: Use the specific Python voices file to avoid C++ ABI collisions
voices_path = os.path.join(args.models_dir, "voices_python.bin") if args.models_dir else "voices_python.bin"

# Attempt to initialize Kokoro
try:
    print(f"Loading Kokoro ONNX from: {model_path}")
    print(f"Loading Python Voices from: {voices_path}")
    kokoro = Kokoro(model_path, voices_path)
except Exception as e:
    print(f"Critical failure starting Kokoro: {e}")
    exit(1)

async def tts_handler(websocket):
    print("Godot client connected to TTS bridge.")
    async for message in websocket:
        try:
            data = json.loads(message)
            text = data.get("text", "")
            
            if not text:
                continue

            print(f"Generating audio for: {text}")
            
            # Generate audio using Kokoro
            audio, sample_rate = kokoro.create(text, voice="es_es", speed=1.0, lang="es")
            
            # Convert raw numpy array to PCM bytes (WAV format)
            with io.BytesIO() as wav_io:
                sf.write(wav_io, audio, sample_rate, format='WAV')
                wav_bytes = wav_io.getvalue()
                
            # Send binary audio back to Godot via WebSocket
            await websocket.send(wav_bytes)
            
        except Exception as e:
            print(f"Error processing TTS: {e}")

async def main():
    print(f"Starting Kokoro TTS Server on port {args.port}...")
    async with websockets.serve(tts_handler, "127.0.0.1", args.port):
        await asyncio.Future()  # Run forever

if __name__ == "__main__":
    asyncio.run(main())