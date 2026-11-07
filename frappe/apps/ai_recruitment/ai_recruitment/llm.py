"""OpenAI-compatible chat client (OpenRouter, vLLM, Ollama, LiteLLM...)."""
import json
import os
import re

import frappe
import requests

SETTINGS = "AI Recruitment Settings"


def config() -> frappe._dict:
    s = frappe.get_cached_doc(SETTINGS)
    key = s.get_password("llm_api_key", raise_exception=False) or os.environ.get("LLM_API_KEY", "")
    base = (s.llm_base_url or os.environ.get("LLM_BASE_URL") or "https://openrouter.ai/api/v1").strip().rstrip("/")
    model = (s.llm_model or os.environ.get("LLM_MODEL") or "qwen/qwen3.8-27b").strip()
    return frappe._dict(settings=s, key=key, base=base, model=model)


def is_configured(c=None) -> bool:
    c = c or config()
    if "openrouter.ai" in c.base:
        return bool(c.key)
    return bool(c.base)


def chat_completion(messages, tools=None, schema=None, schema_name="output", temperature=0.1, timeout=180):
    c = config()
    if not is_configured(c):
        frappe.throw("The language model is not configured. Set it in AI Recruitment Settings.")

    headers = {"Content-Type": "application/json"}
    if c.key:
        headers["Authorization"] = f"Bearer {c.key}"
    openrouter = "openrouter.ai" in c.base
    if openrouter:
        headers["X-Title"] = "Frappe HR AI Recruitment"

    body = {"model": c.model, "messages": messages, "temperature": temperature}
    if tools:
        body["tools"] = tools
        body["tool_choice"] = "auto"
    if schema:
        body["response_format"] = {
            "type": "json_schema",
            "json_schema": {"name": schema_name, "strict": True, "schema": schema},
        }
    if openrouter:
        # Only providers that honour tools/schema and do not retain prompts.
        body["provider"] = {"require_parameters": True, "data_collection": "deny"}
        body["reasoning"] = {"enabled": False}

    url = f"{c.base}/chat/completions"
    r = requests.post(url, headers=headers, json=body, timeout=timeout)
    if r.status_code == 400:
        # Fallback for servers without json_schema / reasoning support.
        body.pop("reasoning", None)
        if schema:
            body["response_format"] = {"type": "json_object"}
        r = requests.post(url, headers=headers, json=body, timeout=timeout)
    if r.status_code >= 400:
        frappe.throw(f"Language model error {r.status_code}: {r.text[:300]}")
    try:
        return r.json()["choices"][0]["message"]
    except (KeyError, IndexError, ValueError):
        frappe.throw(f"Unexpected language model response: {r.text[:300]}")


def extract_json(text: str) -> dict:
    text = (text or "").strip()
    text = re.sub(r"^```(?:json)?\s*|\s*```$", "", text)
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        m = re.search(r"\{.*\}", text, re.S)
        if not m:
            frappe.throw("The model did not return JSON.")
        return json.loads(m.group(0))
