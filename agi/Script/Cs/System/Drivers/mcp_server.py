import os
import subprocess
import uvicorn
import argparse
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import Any, Dict, List

app = FastAPI(title="AGI Standardized MCP Server")

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
    """Provides the AI with read access to the local filesystem for context grounding."""
    try:
        # Resolves paths relative to the Godot user directory for safety.
        if not os.path.exists(path):
            return f"Error: File at {path} does not exist."

        with open(path, 'r', encoding='utf-8') as f:
            return f.read()
    except Exception as e:
        return f"Read Error: {str(e)}"

@app.get("/list_tools")
async def list_tools():
    """
    Exposes the available tool schema to the AGI Agent.
    This allows the LLM to understand the capabilities and required parameters
    for the os_command, web_search, and file_read tools.
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
                "description": "Execute terminal commands for system management.",
                "parameters": {"command": "string"}
            },
            {
                "name": "file_read",
                "description": "Read content from a local file to analyze code or logs.",
                "parameters": {"path": "string"}
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

    elif name == "file_read":
        return {"result": read_local_file(args.get("path", ""))}

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
