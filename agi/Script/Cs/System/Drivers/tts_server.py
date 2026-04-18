import asyncio
import websockets
import json
import sherpa_onnx
import sys
import numpy as np

# AGI Standalone TTS Driver - Python Bridge
# This script acts as a middleware between Godot (C#) and the Sherpa-ONNX engine.

async def tts_server(port, model_path, tokens_path, data_dir):
    print(f'[Python TTS] Initializing VITS engine on port {port}...', flush=True)
    
    # Engine Configuration
    tts_config = sherpa_onnx.OfflineTtsConfig(
        model=sherpa_onnx.OfflineTtsModelConfig(
            vits=sherpa_onnx.OfflineTtsVitsModelConfig(
                model=model_path, 
                lexicon='', 
                tokens=tokens_path, 
                data_dir=data_dir
            ),
            num_threads=2, 
            debug=False, 
            provider='cpu'
        )
    )
    
    # Instance initialization
    tts = sherpa_onnx.OfflineTts(tts_config)
    print('[Python TTS] Engine Ready. Waiting for WebSocket connections...', flush=True)

    async def handler(websocket):
        try:
            async for message in websocket:
                # Expecting JSON: {"text": "Hello world"}
                data = json.loads(message)
                text = data.get('text', '')
                
                if text:
                    # Generate raw floating point samples
                    audio = tts.generate(text, sid=0, speed=1.0)
                    
                    # Convert float32 [-1.0, 1.0] to int16 PCM
                    samples = (np.array(audio.samples) * 32767).astype(np.int16)
                    
                    # Send Binary PCM Data
                    await websocket.send(samples.tobytes())
                    # Send text delimiter to notify chunk completion
                    await websocket.send('Done')
        except websockets.exceptions.ConnectionClosed:
            pass
        except Exception as e:
            print(f'[Python TTS] Runtime Error: {e}', flush=True)

    # Start the local server
    async with websockets.serve(handler, '127.0.0.1', port):
        await asyncio.Future()  # Run forever

if __name__ == '__main__':
    if len(sys.argv) < 5:
        print("Usage: python tts_server.py <port> <model> <tokens> <data_dir>")
        sys.exit(1)
        
    try:
        asyncio.run(tts_server(int(sys.argv[1]), sys.argv[2], sys.argv[3], sys.argv[4]))
    except KeyboardInterrupt:
        print("[Python TTS] Shutting down...")