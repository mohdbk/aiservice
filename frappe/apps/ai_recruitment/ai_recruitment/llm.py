"""OpenAI-compatible chat client (OpenRouter, vLLM, Ollama, LiteLLM...), with streaming."""
import json
import os

import frappe
import requests

from ai_recruitment.utils import WRITING_RULE, clean, extract_json as _extract_json, no_em_dash  # noqa: F401

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


def extract_json(text):
    try:
        return _extract_json(text)
    except ValueError as e:
        frappe.throw(str(e))


def _request(c, messages, tools=None, schema=None, schema_name="output", temperature=0.1, model=None, stream=False):
    headers = {"Content-Type": "application/json"}
    if c.key:
        headers["Authorization"] = f"Bearer {c.key}"
    openrouter = "openrouter.ai" in c.base
    if openrouter:
        headers["X-Title"] = "Frappe HR AI Recruitment"
    body = {"model": model or c.model, "messages": messages, "temperature": temperature}
    if tools:
        body["tools"] = tools
        body["tool_choice"] = "auto"
    if schema:
        body["response_format"] = {"type": "json_schema",
                                   "json_schema": {"name": schema_name, "strict": True, "schema": schema}}
    if stream:
        body["stream"] = True
        body["stream_options"] = {"include_usage": True}
    if openrouter:
        body["provider"] = {"require_parameters": True, "data_collection": "deny"}
        body["reasoning"] = {"enabled": False}
    return headers, body


def _post(c, headers, body, timeout, stream):
    url = f"{c.base}/chat/completions"
    r = requests.post(url, headers=headers, json=body, timeout=timeout, stream=stream)
    if r.status_code == 400:
        # Fallback for servers without json_schema / reasoning / stream_options support.
        body.pop("reasoning", None)
        body.pop("stream_options", None)
        if "response_format" in body:
            body["response_format"] = {"type": "json_object"}
        r = requests.post(url, headers=headers, json=body, timeout=timeout, stream=stream)
    if r.status_code >= 400:
        frappe.throw(f"Language model error {r.status_code}: {r.text[:300]}")
    return r


def complete(messages, tools=None, schema=None, schema_name="output", temperature=0.1, timeout=180,
             model=None, on_delta=None):
    """Returns (message_dict, usage_dict). With on_delta, streams and calls on_delta(text)."""
    c = config()
    if not is_configured(c):
        frappe.throw("The language model is not configured. Set it in AI Recruitment Settings.")
    stream = on_delta is not None
    headers, body = _request(c, messages, tools, schema, schema_name, temperature, model, stream)
    r = _post(c, headers, body, timeout, stream)

    if not stream:
        try:
            data = r.json()
            return data["choices"][0]["message"], data.get("usage") or {}
        except (KeyError, IndexError, ValueError):
            frappe.throw(f"Unexpected language model response: {r.text[:300]}")

    content, calls, usage = "", {}, {}
    for raw in r.iter_lines(decode_unicode=True):
        if not raw or raw.startswith(":") or not raw.startswith("data:"):
            continue
        data = raw[5:].strip()
        if data == "[DONE]":
            break
        try:
            chunk = json.loads(data)
        except json.JSONDecodeError:
            continue
        if chunk.get("usage"):
            usage = chunk["usage"]
        for ch in chunk.get("choices") or []:
            d = ch.get("delta") or {}
            if d.get("content"):
                content += d["content"]
                on_delta(d["content"])
            for tc in d.get("tool_calls") or []:
                t = calls.setdefault(tc.get("index", 0),
                                     {"id": "", "type": "function", "function": {"name": "", "arguments": ""}})
                if tc.get("id"):
                    t["id"] = tc["id"]
                f = tc.get("function") or {}
                if f.get("name"):
                    t["function"]["name"] += f["name"]
                if f.get("arguments"):
                    t["function"]["arguments"] += f["arguments"]
    msg = {"role": "assistant", "content": content}
    if calls:
        msg["tool_calls"] = [calls[k] for k in sorted(calls)]
        for i, t in enumerate(msg["tool_calls"]):
            t["id"] = t["id"] or f"call_{i}"
    return msg, usage


def chat_completion(messages, tools=None, schema=None, schema_name="output", temperature=0.1, timeout=180, model=None):
    """Non-streaming helper returning only the message (used by scoring and OCR)."""
    return complete(messages, tools, schema, schema_name, temperature, timeout, model)[0]
