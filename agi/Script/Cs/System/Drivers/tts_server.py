import asyncio
import websockets
import json
import sys
import numpy as np
from kokoro_onnx import Kokoro

# AGI High-Fidelity TTS Driver - Kokoro-ONNX Bridge

async def tts_server(port, model_path, voices_path, voice_name):
    """
    Inicializa el entorno de inferencia acústica de Kokoro y expone el servicio mediante WebSocket.
    Mantiene los tensores del modelo en memoria para evitar latencias de E/S en peticiones subsecuentes.
    """
    print(f'[Kokoro TTS] Inicializando motor de Alta Fidelidad en puerto {port}...', flush=True)
    
    try:
        # Carga del grafo computacional de ONNX y el manifiesto de descriptores de voz en la memoria de la VM de Python.
        kokoro = Kokoro(model_path, voices_path)
        print(f'[Kokoro TTS] Motor Listo. Voz activa: {voice_name}. Esperando conexiones WebSocket...', flush=True)
    except Exception as e:
        print(f'[Kokoro TTS] Error fatal procesando los pesos del modelo: {e}', flush=True)
        return

    async def handler(websocket):
        """
        Gestiona la conexión bidireccional por socket. 
        Desempaqueta las tramas JSON, dirige la predicción fonética y retransmite los vectores de audio al cliente.
        """
        try:
            async for message in websocket:
                data = json.loads(message)
                text = data.get('text', '')
                
                if text:
                    # Extrae el prefijo de localización estructural de la voz para asignar las reglas fonéticas correctas.
                    lang_char = voice_name[0] if voice_name else 'e'
                    lang_map = {
                        'e': 'es',      # Español
                        'a': 'en-us',   # Inglés Americano
                        'b': 'en-gb',   # Inglés Británico
                        'f': 'fr',      # Francés
                        'i': 'it',      # Italiano
                        'j': 'ja',      # Japonés
                        'h': 'hi',      # Hindi
                        'z': 'zh'       # Mandarín
                    }
                    lang_code = lang_map.get(lang_char, 'es')
                    
                    # Genera la síntesis espectral y recupera los valores escalares en coma flotante y la frecuencia de muestreo.
                    samples, sample_rate = kokoro.create(text, voice=voice_name, speed=1.0, lang=lang_code)
                    
                    # Interpola los valores normalizados de amplitud [-1.0, 1.0] hacia el espacio de bits de PCM 16.
                    pcm_samples = (np.array(samples) * 32767).astype(np.int16)
                    
                    # Retransmite la trama codificada mediante flujos binarios puros.
                    await websocket.send(pcm_samples.tobytes())
                    
                    # Emite delimitador lógico para confirmar el término del flujo acústico al proceso integrador.
                    await websocket.send('Done')
                    
        except websockets.exceptions.ConnectionClosed:
            pass
        except Exception as e:
            print(f'[Kokoro TTS] Error en tiempo de ejecución durante la inferencia: {e}', flush=True)

    # Vincula el servidor de sockets asíncronos a la capa de red local del host.
    async with websockets.serve(handler, '127.0.0.1', port):
        await asyncio.Future()

if __name__ == '__main__':
    # Valida la integridad paramétrica de invocación delegada por el sistema gestor.
    if len(sys.argv) < 5:
        print("Uso: python tts_server.py <puerto> <ruta_kokoro.onnx> <ruta_voices.json> <nombre_de_voz>")
        sys.exit(1)
        
    try:
        # Asigna la ejecución a la jerarquía asíncrona principal.
        asyncio.run(tts_server(int(sys.argv[1]), sys.argv[2], sys.argv[3], sys.argv[4]))
    except KeyboardInterrupt:
        print("[Kokoro TTS] Terminación de ejecución forzada interponiendo apagado seguro del puerto...")