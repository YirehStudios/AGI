import uvicorn
import argparse
import asyncio
import json
import os
import uuid
import random
import glob
import httpx
import threading
import time
from pathlib import Path
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

def watch_parent_process():
    initial_ppid = os.getppid()
    while True:
        current_ppid = os.getppid()
        if current_ppid == 1 or (initial_ppid != 1 and current_ppid != initial_ppid):
            print(f"Parent process {initial_ppid} died. Auto-terminating image_server.py.", flush=True)
            os._exit(0)
        time.sleep(2)

threading.Thread(target=watch_parent_process, daemon=True).start()

app = FastAPI(title="AGI Image Generation Microservice")

class GenerateRequest(BaseModel):
    prompt: str
    unet_name: str | None = None
    vae_name: str | None = None
    clip_l: str | None = None
    clip_t5: str | None = None
    safetensors_name: str | None = None

class ComfyWorkflowFactory:
    @staticmethod
    def build_safetensors_workflow(prompt: str, checkpoint_name: str, width: int = 512, height: int = 512, seed: int = None):
        seed = seed or random.randint(1, 1125899906842624)
        return {
            "3": {
                "class_type": "KSampler",
                "inputs": {
                    "seed": seed,
                    "steps": 20,
                    "cfg": 7.0,
                    "sampler_name": "euler_ancestral",
                    "scheduler": "karras",
                    "denoise": 1.0,
                    "model": ["4", 0],
                    "positive": ["6", 0],
                    "negative": ["7", 0],
                    "latent_image": ["5", 0]
                }
            },
            "4": {
                "class_type": "CheckpointLoaderSimple",
                "inputs": {"ckpt_name": checkpoint_name}
            },
            "5": {
                "class_type": "EmptyLatentImage",
                "inputs": {"batch_size": 1, "width": width, "height": height}
            },
            "6": {
                "class_type": "CLIPTextEncode",
                "inputs": {"text": prompt, "clip": ["4", 1]}
            },
            "7": {
                "class_type": "CLIPTextEncode",
                "inputs": {"text": "text, watermark, ugly, low quality, blurry, deformed", "clip": ["4", 1]}
            },
            "8": {
                "class_type": "VAEDecode",
                "inputs": {"samples": ["3", 0], "vae": ["4", 2]}
            },
            "9": {
                "class_type": "SaveImage",
                "inputs": {"filename_prefix": "agi_img", "images": ["8", 0]}
            }
        }

    @staticmethod
    def build_gguf_workflow(prompt: str, unet_name: str, vae_name: str, clip_l: str, clip_t5: str, width: int = 1024, height: int = 1024, seed: int = None):
        seed = seed or random.randint(1, 1125899906842624)
        return {
            "1": {
                "class_type": "UnetLoaderGGUF",
                "inputs": {"unet_name": unet_name}
            },
            "2": {
                "class_type": "VAELoader",
                "inputs": {"vae_name": vae_name}
            },
            "3": {
                "class_type": "DualCLIPLoader",
                "inputs": {"clip_name1": clip_l, "clip_name2": clip_t5, "type": "sdxl"}
            },
            "4": {
                "class_type": "CLIPTextEncode",
                "inputs": {"text": prompt, "clip": ["3", 0]}
            },
            "5": {
                "class_type": "CLIPTextEncode",
                "inputs": {"text": "score_1, score_2, score_3, score_4, score_5, score_6, monochrome, 3d, photo, photorealistic, realistic, real, ugly, text, watermark", "clip": ["3", 0]}
            },
            "6": {
                "class_type": "EmptyLatentImage",
                "inputs": {"batch_size": 1, "width": width, "height": height}
            },
            "7": {
                "class_type": "KSampler",
                "inputs": {
                    "seed": seed,
                    "steps": 20,
                    "cfg": 7.0,
                    "sampler_name": "euler_ancestral",
                    "scheduler": "karras",
                    "denoise": 1.0,
                    "model": ["1", 0],
                    "positive": ["4", 0],
                    "negative": ["5", 0],
                    "latent_image": ["6", 0]
                }
            },
            "8": {
                "class_type": "VAEDecode",
                "inputs": {"samples": ["7", 0], "vae": ["2", 0]}
            },
            "9": {
                "class_type": "SaveImage",
                "inputs": {"filename_prefix": "agi_img", "images": ["8", 0]}
            }
        }

def find_preferences_path() -> Path:
    script_dir = Path(__file__).resolve().parent
    path1 = script_dir.parent / "settings" / "preferences.json"
    if path1.exists():
        return path1
    path2 = script_dir / "settings" / "preferences.json"
    if path2.exists():
        return path2
    return script_dir / "preferences.json"

def cleanup_comfyui_output(output_dir):
    try:
        if not os.path.exists(output_dir):
            return
        files = glob.glob(os.path.join(output_dir, "*"))
        files.sort(key=os.path.getmtime)
        if len(files) > 30:
            for f in files[:-30]:
                try:
                    if os.path.isfile(f):
                        os.remove(f)
                except Exception as ex:
                    print(f"[ImageServer] Error deleting cached file {f}: {ex}", flush=True)
    except Exception as e:
        print(f"[ImageServer] Error in cleanup: {e}", flush=True)

def check_comfy_vram_errors() -> str:
    try:
        # Check comfyui.log in user://bin/ directory
        script_dir = Path(__file__).resolve().parent
        log_path = script_dir / "comfyui.log"
        if not log_path.exists():
            return ""
        
        with open(log_path, "r", encoding="utf-8", errors="ignore") as f:
            lines = f.readlines()
        
        last_lines = lines[-50:]
        log_snippet = "".join(last_lines)
        
        vram_keywords = [
            "OutOfMemoryError",
            "CUDA out of memory",
            "allocation failed",
            "not enough memory",
            "out of VRAM",
            "out of memory",
            "device-side assert triggered",
            "cuDNN error"
        ]
        
        for kw in vram_keywords:
            if kw.lower() in log_snippet.lower():
                return (
                    f"\n[GPU/VRAM ERROR DETECTED]: ComfyUI system failed due to insufficient graphics memory.\n"
                    f"Details from ComfyUI execution logs:\n"
                    f"----------------------------------------\n"
                    f"{log_snippet}\n"
                    f"----------------------------------------\n"
                    f"Suggestion: Lower the generation resolution or switch to a lighter model preset in AGI Settings."
                )
    except Exception as e:
        return f" (Error reading comfyui.log: {str(e)})"
    return ""

@app.post("/generate")
async def generate_image(request: GenerateRequest):
    prompt = request.prompt
    print(f"[ImageServer] Received prompt request: '{prompt}'", flush=True)

    try:
        # Check if prompt is a raw JSON workflow (advanced usage)
        try:
            workflow = json.loads(prompt)
        except (json.JSONDecodeError, TypeError):
            # Dynamic Construction via Request Parameters
            width = 1024 if "Pony" in str(request.unet_name or request.safetensors_name or "") else 512
            height = width

            if request.unet_name and request.unet_name.endswith(".gguf"):
                workflow = ComfyWorkflowFactory.build_gguf_workflow(
                    prompt=prompt,
                    unet_name=request.unet_name,
                    vae_name=request.vae_name or "sdxl_vae.safetensors",
                    clip_l=request.clip_l or "clip_l.safetensors",
                    clip_t5=request.clip_t5 or "t5xxl_fp16.safetensors",
                    width=width,
                    height=height
                )
            else:
                workflow = ComfyWorkflowFactory.build_safetensors_workflow(
                    prompt=prompt,
                    checkpoint_name=request.safetensors_name or "v1-5-pruned-emaonly.ckpt",
                    width=width,
                    height=height
                )

        payload = {"prompt": workflow}
        
        async with httpx.AsyncClient(timeout=None) as client:
            try:
                resp = await client.post("http://127.0.0.1:8188/prompt", json=payload)
            except httpx.ConnectError:
                return {"result": "Error: ComfyUI engine is not running or failed to initialize. Please verify GPU memory availability or reboot AGI."}
            
            if resp.status_code == 200:
                data = resp.json()
                prompt_id = data.get("prompt_id")
                
                # Poll history to wait for completion (60 attempts x 2 seconds = 120 seconds)
                for attempt in range(60):
                    await asyncio.sleep(2)
                    try:
                        hist_resp = await client.get(f"http://127.0.0.1:8188/history/{prompt_id}")
                    except Exception as poll_ex:
                        print(f"[ImageServer] Polling error: {poll_ex}", flush=True)
                        continue

                    if hist_resp.status_code == 200:
                        hist_data = hist_resp.json()
                        if prompt_id in hist_data:
                            outputs = hist_data[prompt_id].get("outputs", {})
                            files = []
                            for node_id, node_output in outputs.items():
                                if "images" in node_output:
                                    for img in node_output["images"]:
                                        files.append(img.get("filename"))
                            
                            if files:
                                script_dir = Path(__file__).resolve().parent
                                for platform in ["linux", "windows"]:
                                    cleanup_comfyui_output(script_dir / platform / "comfyui" / "output")

                                return {"result": f"Success: Image generated. The files are located in the ComfyUI output directory: {', '.join(files)}. You MUST present this to the user by outputting the tag: [media]{files[0]}[/media]."}
                            else:
                                log_err = check_comfy_vram_errors()
                                if log_err:
                                    return {"result": log_err}
                                return {"result": "Error: Generation finished but no output files were found. The model might have failed to compute the latent space."}
                
                log_err = check_comfy_vram_errors()
                if log_err:
                    return {"result": log_err}
                return {"result": f"Error: Image generation timed out (exceeded 120 seconds). The engine is either under heavy load or compiling models. Please try again in a moment."}
            else:
                log_err = check_comfy_vram_errors()
                if log_err:
                    return {"result": log_err}
                return {"result": f"Error: ComfyUI returned status code {resp.status_code} - {resp.text}"}

    except Exception as e:
        log_err = check_comfy_vram_errors()
        if log_err:
            return {"result": log_err}
        return {"result": f"Image Server Exception: {str(e)}"}

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=8004)
    args = parser.parse_args()

    uvicorn.run(app, host="127.0.0.1", port=args.port)
