import uvicorn
import argparse
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from ddgs import DDGS
import trafilatura

# Define the FastAPI application instance
app = FastAPI(title="AI Search & Research Microservice")

class SearchRequest(BaseModel):
    """
    Data transfer object for incoming search requests.
    Validates that query is a string and deep_research is a boolean (defaults to False).
    """
    query: str
    deep_research: bool = False

def execute_basic_search(query: str) -> str:
    """
    Performs a standard search using the DuckDuckGo API.
    Retrieves the top 5 snippets and formats them into a Markdown string.
    """
    results_list = []
    # Injected telemetry to monitor the query string being processed.
    print(f"--> [SEARCH MICROSERVICE] Executing Basic Query: '{query}'", flush=True)
    with DDGS() as ddgs:
        # Fetching the top 5 text-based results for the provided query.
        search_results = ddgs.text(query, max_results=5)
        
        for result in search_results:
            title = result.get("title", "No Title")
            body = result.get("body", "No Content")
            href = result.get("href", "#")
            
            # Formatting each result as a Markdown block.
            markdown_block = f"# Source: [{title}]({href})\nContent: {body}\n\n---\n"
            results_list.append(markdown_block)
            
    # Reports the final count of snippets retrieved from the search engine.
    print(f"--> [SEARCH MICROSERVICE] Found {len(results_list)} snippets.", flush=True)
    return "\n".join(results_list)

def execute_deep_research(query: str) -> str:
    """
    Performs an advanced search by fetching the top 2 links and 
    extracting full article content using the Trafilatura library.
    """
    # Injected telemetry to track the initiation of high-depth crawling operations.
    print(f"--> [SEARCH MICROSERVICE] Executing Deep Research Query: '{query}'", flush=True)
    results_list = []
    with DDGS() as ddgs:
        # Retrieving the top 2 links to minimize latency and focus on relevance.
        search_results = ddgs.text(query, max_results=2)
        
        for result in search_results:
            url = result.get("href")
            title = result.get("title", "No Title")
            
            if not url:
                continue
                
            try:
                # Downloading and extracting the main text from the target URL.
                downloaded = trafilatura.fetch_url(url)
                content = trafilatura.extract(downloaded)
                
                if content:
                    # Formatting extracted text into the final Markdown structure.
                    markdown_block = f"# Source: [{title}]({url})\nContent:\n{content}\n\n---\n"
                    results_list.append(markdown_block)
                else:
                    # Fallback to the snippet if extraction yields no main text.
                    results_list.append(f"# Source: [{title}]({url})\nContent: [Extraction Failed]\n\n---\n")
            except Exception:
                # Prevents service interruption if a specific URL fails to load.
                continue
                
    return "\n".join(results_list)

@app.post("/search")
async def search_endpoint(request: SearchRequest):
    """
    Primary API endpoint for search operations.
    Routes the logic between basic snippets and deep content extraction.
    """
    try:
        if request.deep_research:
            compiled_markdown = execute_deep_research(request.query)
        else:
            compiled_markdown = execute_basic_search(request.query)
            
        return {"results": compiled_markdown}
    
    except Exception as e:
        # Centralized exception handling for the search process
        raise HTTPException(status_code=500, detail=str(e))

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=8000)
    args = parser.parse_args()

    uvicorn.run(app, host="127.0.0.1", port=args.port)