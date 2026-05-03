import asyncio
import websockets
import json
import soundfile as sf
import io
import argparse
import os
from kokoro_onnx import Kokoro

# Configurar argumentos de línea de comandos
parser = argparse.ArgumentParser()
parser.add_argument("--port", type=int, default=8888)
# Añadimos un argumento para que Godot nos pueda decir dónde están los modelos
parser.add_argument("--models-dir", type=str, default="") 
args = parser.parse_args()

# Determinar las rutas de los modelos
# Si no le pasamos ruta por comando, asume que está en una subcarpeta de donde se ejecuta el script
model_path = os.path.join(args.models_dir, "model.onnx") if args.models_dir else "model.onnx"
voices_path = os.path.join(args.models_dir, "voices.bin") if args.models_dir else "voices.bin"

# Intentar inicializar Kokoro
try:
    print(f"Cargando Kokoro ONNX desde: {model_path}")
    kokoro = Kokoro(model_path, voices_path)
except Exception as e:
    print(f"Fallo crítico al iniciar Kokoro: {e}")
    # Si falla, salimos limpiamente para que Godot capture el print de error
    exit(1)

async def tts_handler(websocket):
    print("Cliente de Godot conectado al puente TTS.")
    async for message in websocket:
        try:
            data = json.loads(message)
            text = data.get("text", "")
            
            if not text:
                continue

            print(f"Generando audio para: {text}")
            
            # Generar audio usando Kokoro
            audio, sample_rate = kokoro.create(text, voice="es_es", speed=1.0, lang="es")
            
            # Convertir el numpy array crudo a bytes PCM (Formato WAV)
            with io.BytesIO() as wav_io:
                sf.write(wav_io, audio, sample_rate, format='WAV')
                wav_bytes = wav_io.getvalue()
                
            # Enviar el audio binario de vuelta a Godot por WebSocket
            await websocket.send(wav_bytes)
            
        except Exception as e:
            print(f"Error procesando TTS: {e}")

async def main():
    print(f"Iniciando Servidor TTS Kokoro en puerto {args.port}...")
    async with websockets.serve(tts_handler, "127.0.0.1", args.port):
        await asyncio.Future()  # Correr para siempre

if __name__ == "__main__":
    asyncio.run(main())