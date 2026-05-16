import os
import subprocess
import uvicorn
import argparse
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import Any, Dict, List
from pathlib import Path

app = FastAPI(title="AGI Standardized MCP Server")

SANDBOX_ROOT = Path(os.path.expanduser("~/.local/share/agi/workspace")).resolve()

def is_safe_path(requested_path: str) -> bool:
    """
    Validates structural canonical compliance of the target resource route, 
    ensuring full resolution strictly resides within the configured sandbox root.
    """
    try:
        target_path = Path(requested_path).resolve()
        return target_path.is_relative_to(SANDBOX_ROOT)
    except Exception:
        return False

class ToolCallRequest(BaseModel):
    """Data transfer object for executing tool logic via the MCP gateway."""
    tool: str
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

@app.get("/list_tools")
async def list_tools():
    """
    Exposes the available tool schema to the AGI Agent.
    This allows the LLM to understand the capabilities and required parameters
    for the os_command, web_search, read_file, ls, and create_new_file tools.
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
            }
        ]
    }

@app.post("/call_tool")
async def call_tool(request: ToolCallRequest):
    """
    Universal entry point for tool execution.
    Routes requests to specialized handlers based on the tool identifier,
    ensuring downstream responses conform to standard payload contract constraints.
    """
    name = request.tool
    args = request.arguments

    if name == "os_command":
        return {"result": execute_os_command(args.get("command", ""))}
    elif name == "read_file":
        return {"result": read_local_file(args.get("path", ""))}
    elif name == "ls":
        return {"result": list_directory(args.get("path", ""))}
    elif name == "create_new_file":
        return {"result": create_new_file(args.get("path", ""), args.get("content", ""))}
    elif name == "web_search":
        # Note: This routes internally to the existing search microservice
        # normally running on port 8000.
        import httpx
        async with httpx.AsyncClient() as client:
            resp = await client.post("http://127.0.0.1:8000/search", json=args)
            data = resp.json()
            return {"result": data.get("results", "Error: No data retrieved from search.")}

    raise HTTPException(status_code=404, detail=f"Tool '{name}' not found.")

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=8002)
    args = parser.parse_args()

    # Mantenemos 127.0.0.1 y ahora respetamos el puerto de Godot
    uvicorn.run(app, host="127.0.0.1", port=args.port)
