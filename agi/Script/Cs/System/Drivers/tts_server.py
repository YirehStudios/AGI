import asyncio
import websockets
import json
import soundfile as sf
import io
import argparse
from kokoro_onnx import Kokoro

# Configurar argumentos de línea de comandos
parser = argparse.ArgumentParser()
parser.add_argument("--port", type=int, default=8888)
args = parser.parse_args()

# Inicializar Kokoro ONNX (Asegúrate de que los archivos model.onnx y voices.bin estén en esta ruta o pásalos como argumento)
# Puedes ajustar la ruta para que coincida con tu carpeta models de Godot
kokoro = Kokoro("model.onnx", "voices.bin")

async def tts_handler(websocket):
    print("Cliente de Godot conectado al puente TTS.")
    async for message in websocket:
        try:
            data = json.loads(message)
            text = data.get("text", "")

            if not text:
                continue

            print(f"Generando audio para: {text}")

            # Generar audio usando Kokoro (devuelve numpy array y sample rate)
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
