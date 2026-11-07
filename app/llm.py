"""OpenAI-compatible chat client. Works with OpenRouter, vLLM, Ollama, LiteLLM..."""
import json
import re

import httpx

from .config import settings


class LLMError(Exception):
    pass


def _extract_json(text: str) -> dict:
    text = (text or "").strip()
    text = re.sub(r"^```(?:json)?\s*|\s*```$", "", text)
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        m = re.search(r"\{.*\}", text, re.S)
        if not m:
            raise LLMError("Model did not return JSON")
        return json.loads(m.group(0))


async def chat_json(messages: list[dict], schema: dict, schema_name: str) -> dict:
    headers = {"Content-Type": "application/json"}
    if settings.llm_api_key:
        headers["Authorization"] = f"Bearer {settings.llm_api_key}"
    is_openrouter = "openrouter.ai" in settings.llm_base_url
    if is_openrouter:
        headers["X-Title"] = "Ministry Career Portal (demo)"

    body: dict = {
        "model": settings.llm_model,
        "messages": messages,
        "temperature": 0.1,
        "response_format": {
            "type": "json_schema",
            "json_schema": {"name": schema_name, "strict": True, "schema": schema},
        },
    }
    if is_openrouter:
        # Only route to providers that honour the schema and do not retain prompts.
        body["provider"] = {"require_parameters": True, "data_collection": "deny"}
        body["reasoning"] = {"enabled": False}

    url = f"{settings.llm_base_url}/chat/completions"
    async with httpx.AsyncClient(timeout=180) as client:
        r = await client.post(url, headers=headers, json=body)
        if r.status_code == 400:
            # Fallback for endpoints without json_schema / reasoning support.
            body.pop("reasoning", None)
            body["response_format"] = {"type": "json_object"}
            r = await client.post(url, headers=headers, json=body)
        if r.status_code >= 400:
            raise LLMError(f"LLM {r.status_code}: {r.text[:400]}")
        data = r.json()

    try:
        content = data["choices"][0]["message"]["content"]
    except (KeyError, IndexError, TypeError):
        raise LLMError(f"Unexpected LLM response: {str(data)[:400]}")
    return _extract_json(content)
