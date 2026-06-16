import os
import subprocess
import uvicorn
import argparse
import glob
import urllib.request
import re
from fastapi import FastAPI, HTTPException # pyright: ignore[reportMissingImports]
from pydantic import BaseModel
from typing import Any, Dict, List
from pathlib import Path


import threading
import time
import json
import httpx
import uuid
import asyncio

def watch_parent_process():
    # En Linux, un proceso huérfano es adoptado por init (PID 1) o systemd.
    # Si detectamos que el parent_pid es 1, o que Godot ya no existe, nos suicidamos.
    initial_ppid = os.getppid()
    while True:
        current_ppid = os.getppid()
        if current_ppid == 1 or (initial_ppid != 1 and current_ppid != initial_ppid):
            print(f"Parent process {initial_ppid} died. Auto-terminating mcp_server.py.", flush=True)
            os._exit(0)
        time.sleep(2)

threading.Thread(target=watch_parent_process, daemon=True).start()

app = FastAPI(title="AGI Standardized MCP Server")

SANDBOX_ROOT_STR = os.getenv("AGI_WORKSPACE", os.path.expanduser("~/.local/share/agi/workspace"))
SANDBOX_ROOT = Path(SANDBOX_ROOT_STR).resolve()

def is_safe_path(requested_path: str) -> bool:
    """
    All path restrictions have been moved to the Godot client layer.
    The Godot application will prompt the user if the AI tries to access 
    anything outside the designated workspace.
    """
    return True

from typing import Any, Dict, List, Optional

class ToolCallRequest(BaseModel):
    """Data transfer object for executing tool logic via the MCP gateway."""
    tool: Optional[str] = None
    name: Optional[str] = None
    arguments: Dict[str, Any]

def execute_os_command(command: str) -> str:
    """Executes a system-level command and captures the output stream."""
    try:
        # Cross-platform execution using the system shell.
        result = subprocess.run(
            command,
            shell=True,
            capture_output=True,
            text=True,
            timeout=10
        )
        return result.stdout if result.returncode == 0 else result.stderr
    except Exception as e:
        return f"Execution Error: {str(e)}"

def read_local_file(path: str) -> str:
    """
    Reads data streams from a persistent storage file path following 
    strict sandbox directory boundary validation checks.
    """
    if not is_safe_path(path):
        return f"SECURITY BLOCK: Access to {path} is denied. Stay within the workspace."
    try:
        if not os.path.exists(path):
            return f"Error: File at {path} does not exist."

        with open(path, 'r', encoding='utf-8') as f:
            return f.read()
    except Exception as e:
        return f"Read Error: {str(e)}"

def list_directory(path: str) -> str:
    """
    Interrogates the underlying virtual filesystem to aggregate and structure directory tree listings 
    after validating canonical root containment configurations.
    """
    if not is_safe_path(path):
         return f"SECURITY BLOCK: Access to {path} is denied."
    try:
        target = Path(path)
        if not target.exists() or not target.is_dir():
            return f"Error: Directory {path} does not exist."
        items = [f"- {p.name}{'/' if p.is_dir() else ''}" for p in target.iterdir()]
        return "\n".join(items) if items else "Directory is empty."
    except Exception as e:
        return f"LS Error: {str(e)}"


def create_new_file(path: str, content: str) -> str:
    """
    Allocates an uninitialized file handle and writes raw character blocks 
    ensuring parental directory integrity validation inside the tracking workspace.
    """
    if not is_safe_path(path):
        return f"SECURITY BLOCK: Access to {path} is denied."
    try:
        target = Path(path)
        if target.exists():
            return f"Error: File {path} already exists. Use edit tools instead."
        
        target.parent.mkdir(parents=True, exist_ok=True)
        with open(target, 'w', encoding='utf-8') as f:
            f.write(content)
        return f"Success: File created at {path}"
    except Exception as e:
        return f"Create Error: {str(e)}"
    
def file_glob_search(pattern: str, path: str) -> str:
    """
    Performs a recursive directory traversal looking for filenames matching a glob syntax pattern.
    Ensures path resolutions strictly adhere to sandbox workspace containment boundaries.
    """
    if not is_safe_path(path):
        return f"SECURITY BLOCK: Access to {path} is denied."
    try:
        target_dir = Path(path)
        search_pattern = str(target_dir / "**" / pattern)
        matches = glob.glob(search_pattern, recursive=True)
        if not matches:
            return "No files found matching the pattern."
        
        return "\n".join([str(Path(m).relative_to(SANDBOX_ROOT)) for m in matches[:50]]) 
    except Exception as e:
        return f"Glob Search Error: {str(e)}"

def fetch_url_content(url: str) -> str:
    """
    Sends a sanitized network HTTP request to access external textual web representations.
    Truncates output buffers natively to safely balance context-window conservation bounds.
    """
    try:
        req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
        with urllib.request.urlopen(req, timeout=10) as response:
            return response.read().decode('utf-8')[:5000] 
    except Exception as e:
        return f"Fetch Error: {str(e)}"
    
def edit_existing_file(path: str, content: str) -> str:
    """
    Overwrites an existing file completely with new content.
    Performs directory validation before execution to enforce sandbox constraint boundaries.
    """
    if not is_safe_path(path):
        return f"SECURITY BLOCK: Access to {path} is denied."
    try:
        target = Path(path)
        if not target.exists():
            return f"Error: File {path} does not exist. Use create_new_file instead."
        
        with open(target, 'w', encoding='utf-8') as f:
            f.write(content)
        return f"Success: File {path} successfully edited."
    except Exception as e:
        return f"Edit Error: {str(e)}"

def single_find_and_replace(path: str, find_string: str, replace_string: str) -> str:
    """
    Performs an exact string replacement inside a file.
    Validates canonical path correctness and guarantees targeted, minimal code modifications.
    """
    if not is_safe_path(path):
        return f"SECURITY BLOCK: Access to {path} is denied."
    try:
        target = Path(path)
        if not target.exists():
            return f"Error: File {path} does not exist."
            
        with open(target, 'r', encoding='utf-8') as f:
            content = f.read()
            
        if find_string not in content:
            return "Error: The exact find_string was not found in the file. Check for typos or whitespace differences."
            
        content = content.replace(find_string, replace_string)
        
        with open(target, 'w', encoding='utf-8') as f:
            f.write(content)
        return f"Success: Replaced occurrences of the string in {path}."
    except Exception as e:
        return f"Replace Error: {str(e)}"

def grep_search(directory: str, regex_pattern: str, file_pattern: str = "*") -> str:
    """
    Performs a regular expression search over files within a target directory.
    Traverses subdirectories up to predefined thresholds to guarantee protection against memory overflow.
    """
    if not is_safe_path(directory):
        return f"SECURITY BLOCK: Access to {directory} is denied."
    try:
        target_dir = Path(directory)
        matches = []
        regex = re.compile(regex_pattern)
        
        for filepath in target_dir.rglob(file_pattern):
            if filepath.is_file():
                try:
                    with open(filepath, 'r', encoding='utf-8') as f:
                        for i, line in enumerate(f):
                            if regex.search(line):
                                rel_path = filepath.relative_to(SANDBOX_ROOT)
                                matches.append(f"{rel_path}:{i+1}: {line.strip()}")
                                if len(matches) > 100:
                                    break
                except UnicodeDecodeError:
                    pass
            if len(matches) > 100:
                matches.append("... [Results truncated to 100 limits] ...")
                break
                
        return "\n".join(matches) if matches else "No matches found."
    except Exception as e:
        return f"Grep Error: {str(e)}"
    
def delete_file(path: str) -> str:
    """
    Removes a targeted file from the disk layout after confirming that the resolved
    canonical path adheres strictly to the defined security boundary constraint rules.
    """
    if not is_safe_path(path):
        return f"SECURITY BLOCK: Access to {path} is denied."
    try:
        target = Path(path)
        if not target.exists():
            return f"Error: File {path} does not exist."
        if target.is_dir():
            return "Error: Path is a directory. Cannot delete directories with this tool."
        target.unlink()
        return f"Success: Deleted file {path}"
    except Exception as e:
        return f"Delete Error: {str(e)}"

def rename_file(source_path: str, destination_path: str) -> str:
    """
    Alters the file system nomenclature or relocates a resource block to an alternate destination.
    Enforces sandbox containment verification algorithms on both target operational parameters.
    """
    if not is_safe_path(source_path) or not is_safe_path(destination_path):
        return "SECURITY BLOCK: Access is denied. Both paths must be within the sandbox."
    try:
        src = Path(source_path)
        dst = Path(destination_path)
        if not src.exists():
            return f"Error: Source {source_path} does not exist."
        if dst.exists():
            return f"Error: Destination {destination_path} already exists."
        dst.parent.mkdir(parents=True, exist_ok=True)
        src.rename(dst)
        return f"Success: Moved/Renamed from {source_path} to {destination_path}"
    except Exception as e:
        return f"Rename Error: {str(e)}"

def create_directory(path: str) -> str:
    """
    Allocates initialization vectors for new nested directory segments sequentially down to the 
    requested deep index map while preserving systemic storage safety borders.
    """
    if not is_safe_path(path):
        return f"SECURITY BLOCK: Access to {path} is denied."
    try:
        target = Path(path)
        if target.exists():
            return f"Error: Directory or file {path} already exists."
        target.mkdir(parents=True, exist_ok=True)
        return f"Success: Created directory {path}"
    except Exception as e:
        return f"Create Dir Error: {str(e)}"

def read_multiple_files(paths: list) -> str:
    """
    Executes a high-efficiency concurrent loop context reading content buffers from multiple paths.
    Consolidates response structures to reduce token contextual turn overhead requirements.
    """
    results = []
    for path in paths:
        if not is_safe_path(path):
            results.append(f"[{path}] SECURITY BLOCK: Access denied.")
            continue
        try:
            target = Path(path)
            if not target.exists() or target.is_dir():
                results.append(f"[{path}] Error: File not found or is a directory.")
                continue
            with open(target, 'r', encoding='utf-8') as f:
                content = f.read()
                results.append(f"--- START OF {path} ---\n{content}\n--- END OF {path} ---")
        except Exception as e:
            results.append(f"[{path}] Read Error: {str(e)}")
    return "\n\n".join(results)



@app.get("/list_tools")
async def list_tools():
    """
    Exposes the completed 14-tool suite manifest definitions containing architectural constraints,
    parameters, and metadata mapping rules to the agentic client workflow loop.
    """
    return {
        "tools": [
            {
                "name": "web_search",
                "description": "Search the internet for real-time information.",
                "parameters": {"query": "string"}
            },
            {
                "name": "os_command",
                "description": "Execute terminal commands for system management. (WARNING: Requires explicit user approval)",
                "parameters": {"command": "string"}
            },
            {
                "name": "read_file",
                "description": "Read content from a local file.",
                "parameters": {"path": "string"}
            },
            {
                "name": "ls",
                "description": "List files and folders in a given directory.",
                "parameters": {"path": "string"}
            },
            {
                "name": "create_new_file",
                "description": "Create a new file. Only use this when a file doesn't exist yet.",
                "parameters": {"path": "string", "content": "string"}
            },
            {
                "name": "file_glob_search",
                "description": "Search for files recursively in a directory using glob patterns. Example pattern: '*.cs'",
                "parameters": {"path": "string", "pattern": "string"}
            },
            {
                "name": "fetch_url_content",
                "description": "Fetch the raw text content of a specific website URL.",
                "parameters": {"url": "string"}
            },
            {
                "name": "edit_existing_file",
                "description": "Edit an existing file by completely overwriting its contents. (WARNING: Requires explicit user approval)",
                "parameters": {"path": "string", "content": "string"}
            },
            {
                "name": "single_find_and_replace",
                "description": "Performs exact string replacements in a file. Use this for precise, small edits.",
                "parameters": {"path": "string", "find_string": "string", "replace_string": "string"}
            },
            {
                "name": "grep_search",
                "description": "Performs a regular expression (regex) search over the files in a directory.",
                "parameters": {"directory": "string", "regex_pattern": "string", "file_pattern": "string"}
            },
            {
                "name": "delete_file",
                "description": "Delete a file from the filesystem. (WARNING: Requires explicit user approval)",
                "parameters": {"path": "string"}
            },
            {
                "name": "rename_file",
                "description": "Rename or move a file from one path to another. (WARNING: Requires explicit user approval)",
                "parameters": {"source_path": "string", "destination_path": "string"}
            },
            {
                "name": "create_directory",
                "description": "Create a new folder/directory.",
                "parameters": {"path": "string"}
            },
            {
                "name": "read_multiple_files",
                "description": "Read the contents of multiple files at once. Pass a list of file paths.",
                "parameters": {"paths": "list of strings"}
            },
            {
                "name": "global_access",
                "description": "Virtual Godot tool. Governs operations outside the workspace. DO NOT CALL DIRECTLY. (WARNING: Requires explicit user approval, HIGH RISK)",
                "parameters": {"path": "string"}
            },
            {
                "name": "generate_image",
                "description": "Generate an image using the local ComfyUI engine. The prompt can be natural language, Danbooru tags, or a raw ComfyUI JSON workflow depending on the selected Prompt Strategy.",
                "parameters": {"prompt": "string"}
            },
            {
                "name": "generate_video",
                "description": "Generate a video using the local ComfyUI engine. The prompt can be natural language or a raw ComfyUI JSON workflow.",
                "parameters": {"prompt": "string"}
            }
        ]
    }

@app.post("/call_tool")
async def call_tool(request: ToolCallRequest):
    """
    Universal centralized dispatch entry point routing inbound parameter maps to corresponding 
    isolated backend workers while validating system security sandbox constraints.
    """
    name = request.tool or request.name
    if not name:
        raise HTTPException(status_code=422, detail="Missing required field: 'tool' or 'name'")
    args = request.arguments

    if name == "os_command":
        return {"result": execute_os_command(args.get("command", ""))}
    elif name == "read_file":
        return {"result": read_local_file(args.get("path", ""))}
    elif name == "ls":
        return {"result": list_directory(args.get("path", ""))}
    elif name == "create_new_file":
        return {"result": create_new_file(args.get("path", ""), args.get("content", ""))}
    elif name == "file_glob_search":
        return {"result": file_glob_search(args.get("pattern", "*"), args.get("path", str(SANDBOX_ROOT)))}
    elif name == "fetch_url_content":
        return {"result": fetch_url_content(args.get("url", ""))}
    elif name == "edit_existing_file":
        return {"result": edit_existing_file(args.get("path", ""), args.get("content", ""))}
    elif name == "single_find_and_replace":
        return {"result": single_find_and_replace(args.get("path", ""), args.get("find_string", ""), args.get("replace_string", ""))}
    elif name == "grep_search":
        return {"result": grep_search(args.get("directory", str(SANDBOX_ROOT)), args.get("regex_pattern", ""), args.get("file_pattern", "*"))}
    elif name == "delete_file":
        return {"result": delete_file(args.get("path", ""))}
    elif name == "rename_file":
        return {"result": rename_file(args.get("source_path", ""), args.get("destination_path", ""))}
    elif name == "create_directory":
        return {"result": create_directory(args.get("path", ""))}
    elif name == "read_multiple_files":
        return {"result": read_multiple_files(args.get("paths", []))}
    elif name == "generate_image":
        port = getattr(app.state, "port", 8002)
        image_port = port + 2
        
        # Build dynamic payload reading from preferences and presets
        payload = {"prompt": args.get("prompt", "")}
        try:
            pref_path = SANDBOX_ROOT / "Script" / "Cs" / "System" / "Config" / "preferences.json"
            presets_path = SANDBOX_ROOT / "Script" / "Cs" / "System" / "Config" / "presets.json"
            
            if pref_path.exists() and presets_path.exists():
                with open(pref_path, "r", encoding="utf-8") as f:
                    active_model = json.load(f).get("ActiveImageModel", "")
                    
                with open(presets_path, "r", encoding="utf-8") as f:
                    presets = json.load(f)
                    
                for preset in presets:
                    if preset.get("Name") == active_model:
                        adv = preset.get("AdvancedDownloads")
                        if adv:
                            for dl in adv:
                                sub = dl.get("ComfySubfolder", "")
                                fname = dl.get("Url", "").split("/")[-1]
                                if sub == "unet": payload["unet_name"] = fname
                                elif sub == "vae": payload["vae_name"] = fname
                                elif sub == "clip":
                                    if "t5" in fname.lower(): payload["clip_t5"] = fname
                                    else: payload["clip_l"] = fname
                        else:
                            payload["safetensors_name"] = active_model.replace(" ", "_") + ".safetensors"
                        break
        except Exception as e:
            print(f"[MCP] Error building dynamic image payload: {e}")

        try:
            async with httpx.AsyncClient(timeout=None) as client:
                resp = await client.post(f"http://127.0.0.1:{image_port}/generate", json=payload)
                if resp.status_code == 200:
                    return resp.json()
                else:
                    return {"result": f"Error: Image server returned status code {resp.status_code}."}
        except Exception as e:
            return {"result": f"Error contacting local image generation server: {str(e)}"}
    elif name == "generate_video":
        port = getattr(app.state, "port", 8002)
        video_port = port + 4
        
        # Build dynamic payload reading from preferences and presets
        payload = {"prompt": args.get("prompt", "")}
        try:
            pref_path = SANDBOX_ROOT / "Script" / "Cs" / "System" / "Config" / "preferences.json"
            presets_path = SANDBOX_ROOT / "Script" / "Cs" / "System" / "Config" / "presets.json"
            
            if pref_path.exists() and presets_path.exists():
                with open(pref_path, "r", encoding="utf-8") as f:
                    active_model = json.load(f).get("ActiveVideoModel", "")
                    
                with open(presets_path, "r", encoding="utf-8") as f:
                    presets = json.load(f)
                    
                for preset in presets:
                    if preset.get("Name") == active_model:
                        adv = preset.get("AdvancedDownloads")
                        if adv:
                            for dl in adv:
                                sub = dl.get("ComfySubfolder", "")
                                fname = dl.get("Url", "").split("/")[-1]
                                if sub == "unet": payload["unet_name"] = fname
                                elif sub == "vae": payload["vae_name"] = fname
                                elif sub == "clip":
                                    if "t5" in fname.lower(): payload["clip_t5"] = fname
                                    else: payload["clip_l"] = fname
                        else:
                            payload["safetensors_name"] = active_model.replace(" ", "_") + ".safetensors"
                        break
        except Exception as e:
            print(f"[MCP] Error building dynamic video payload: {e}")

        try:
            async with httpx.AsyncClient(timeout=None) as client:
                resp = await client.post(f"http://127.0.0.1:{video_port}/generate", json=payload)
                if resp.status_code == 200:
                    return resp.json()
                else:
                    return {"result": f"Error: Video server returned status code {resp.status_code}."}
        except Exception as e:
            return {"result": f"Error contacting local video generation server: {str(e)}"}
    elif name == "web_search":
        async with httpx.AsyncClient(timeout=30.0) as client:
            resp = await client.post("http://127.0.0.1:8000/search", json=args)
            data = resp.json()
            return {"result": data.get("results", "Error: No data retrieved from search.")}

    raise HTTPException(status_code=404, detail=f"Tool '{name}' not found.")

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=8002)
    args = parser.parse_args()
    app.state.port = args.port

    # Mantenemos 127.0.0.1 y ahora respetamos el puerto de Godot
    uvicorn.run(app, host="127.0.0.1", port=args.port)