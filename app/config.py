import os


def _env(name: str, default: str = "") -> str:
    return os.environ.get(name, default).strip()


class Settings:
    frappe_url = _env("FRAPPE_URL").rstrip("/")
    frappe_admin_password = _env("FRAPPE_ADMIN_PASSWORD")
    frappe_api_key = _env("FRAPPE_API_KEY")
    frappe_api_secret = _env("FRAPPE_API_SECRET")
    service_user_email = _env("SERVICE_USER_EMAIL", "ai-agent@example.com")

    service_public_url = _env("SERVICE_PUBLIC_URL").rstrip("/")
    webhook_token = _env("WEBHOOK_TOKEN")
    mcp_token = _env("MCP_TOKEN")

    llm_base_url = _env("LLM_BASE_URL", "https://openrouter.ai/api/v1").rstrip("/")
    llm_model = _env("LLM_MODEL", "qwen/qwen3.8-27b")
    llm_api_key = _env("LLM_API_KEY")

    seed_demo = _env("SEED_DEMO", "false").lower() in ("1", "true", "yes")

    # Max CV characters sent to the model
    max_cv_chars = int(_env("MAX_CV_CHARS", "40000"))

    @property
    def llm_configured(self) -> bool:
        # A hosted gateway needs a key; a local vLLM/Ollama endpoint may not.
        if "openrouter.ai" in self.llm_base_url:
            return bool(self.llm_api_key)
        return bool(self.llm_base_url)


settings = Settings()
