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
            print(f"Parent process {initial_ppid} died. Auto-terminating video_server.py.", flush=True)
            os._exit(0)
        time.sleep(2)

threading.Thread(target=watch_parent_process, daemon=True).start()

app = FastAPI(title="AGI Video Generation Microservice")

class GenerateRequest(BaseModel):
    prompt: str
    unet_name: str | None = None
    vae_name: str | None = None
    clip_l: str | None = None
    clip_t5: str | None = None
    safetensors_name: str | None = None

class ComfyWorkflowFactory:
    @staticmethod
    def build_svd_workflow(prompt: str, checkpoint_name: str, width: int = 512, height: int = 512, seed: int = None):
        seed = seed or random.randint(1, 1125899906842624)
        return {
            "3": {
                "class_type": "KSampler",
                "inputs": {
                    "seed": random.randint(1, 1125899906842624),
                    "steps": 20,
                    "cfg": 7.0,
                    "sampler_name": "euler",
                    "scheduler": "normal",
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
                "inputs": {"width": width, "height": height, "batch_size": 1}
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
            "10": {
                "class_type": "ImageOnlyCheckpointLoader",
                "inputs": {"ckpt_name": "svd_xt.safetensors"}
            },
            "11": {
                "class_type": "SVD_img2vid_Conditioning",
                "inputs": {
                    "width": width,
                    "height": height,
                    "video_frames": 25,
                    "motion_bucket_id": 127,
                    "fps": 6,
                    "augmentation_level": 0.0,
                    "clip_vision": ["10", 1],
                    "init_image": ["8", 0]
                }
            },
            "12": {
                "class_type": "KSampler",
                "inputs": {
                    "seed": seed,
                    "steps": 20,
                    "cfg": 2.5,
                    "sampler_name": "euler",
                    "scheduler": "karras",
                    "denoise": 1.0,
                    "model": ["10", 0],
                    "positive": ["11", 0],
                    "negative": ["11", 1],
                    "latent_image": ["11", 2]
                }
            },
            "13": {
                "class_type": "VAEDecode",
                "inputs": {"samples": ["12", 0], "vae": ["10", 2]}
            },
            "14": {
                "class_type": "SaveImage",
                "inputs": {"filename_prefix": f"agi_video_{uuid.uuid4().hex[:8]}", "images": ["13", 0]}
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
                    print(f"[VideoServer] Error deleting cached file {f}: {ex}", flush=True)
    except Exception as e:
        print(f"[VideoServer] Error in cleanup: {e}", flush=True)

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
                    f"\n[GPU/VRAM ERROR DETECTED]: ComfyUI system failed during Video (SVD) generation due to insufficient graphics memory.\n"
                    f"Details from ComfyUI execution logs:\n"
                    f"----------------------------------------\n"
                    f"{log_snippet}\n"
                    f"----------------------------------------\n"
                    f"Suggestion: Video generation with SVD requires high GPU capacity. Close other memory-intensive GPU processes or ensure your GPU meets SVD VRAM requirements."
                )
    except Exception as e:
        return f" (Error reading comfyui.log: {str(e)})"
    return ""

@app.post("/generate")
async def generate_video(request: GenerateRequest):
    prompt = request.prompt
    print(f"[VideoServer] Received prompt request: '{prompt}'", flush=True)

    try:
        # Check if prompt is a raw JSON workflow
        try:
            workflow = json.loads(prompt)
        except (json.JSONDecodeError, TypeError):
            # Dynamic Construction via Request Parameters
            width = 512
            height = 512

            workflow = ComfyWorkflowFactory.build_svd_workflow(
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
                        print(f"[VideoServer] Polling error: {poll_ex}", flush=True)
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
                                if "gifs" in node_output:
                                    for gif in node_output["gifs"]:
                                        files.append(gif.get("filename"))
                            
                            if files:
                                script_dir = Path(__file__).resolve().parent
                                for platform in ["linux", "windows"]:
                                    cleanup_comfyui_output(script_dir / platform / "comfyui" / "output")

                                return {"result": f"Success: Video generated. The files are located in the ComfyUI output directory: {', '.join(files)}. You MUST present this to the user by outputting the tag: [media]{files[0]}[/media]."}
                            else:
                                log_err = check_comfy_vram_errors()
                                if log_err:
                                    return {"result": log_err}
                                return {"result": "Error: Generation finished but no output files were found. The models might have failed to compute the latent space."}
                
                log_err = check_comfy_vram_errors()
                if log_err:
                    return {"result": log_err}
                return {"result": f"Error: Video generation timed out (exceeded 120 seconds). The engine is either under heavy load, compiling models, or rendering frames. Please try again in a moment."}
            else:
                log_err = check_comfy_vram_errors()
                if log_err:
                    return {"result": log_err}
                return {"result": f"Error: ComfyUI returned status code {resp.status_code} - {resp.text}"}

    except Exception as e:
        log_err = check_comfy_vram_errors()
        if log_err:
            return {"result": log_err}
        return {"result": f"Video Server Exception: {str(e)}"}

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=8006)
    args = parser.parse_args()

    uvicorn.run(app, host="127.0.0.1", port=args.port)
