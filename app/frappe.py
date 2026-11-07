"""Minimal async client for the Frappe REST API."""
import json
from typing import Any
from urllib.parse import quote

import httpx


class FrappeError(Exception):
    def __init__(self, status: int, message: str):
        super().__init__(f"Frappe {status}: {message[:500]}")
        self.status = status


class Frappe:
    def __init__(self, base_url: str, api_key: str = "", api_secret: str = ""):
        headers = {"Accept": "application/json"}
        if api_key and api_secret:
            headers["Authorization"] = f"token {api_key}:{api_secret}"
        self.client = httpx.AsyncClient(
            base_url=base_url, headers=headers, timeout=60, follow_redirects=True
        )

    async def close(self):
        await self.client.aclose()

    @staticmethod
    def _check(r: httpx.Response):
        if r.status_code >= 400:
            raise FrappeError(r.status_code, r.text)

    # --- auth ---
    async def login(self, usr: str, pwd: str):
        r = await self.client.post("/api/method/login", json={"usr": usr, "pwd": pwd})
        self._check(r)

    async def ping(self) -> bool:
        try:
            r = await self.client.get("/api/method/ping", timeout=10)
            return r.status_code == 200
        except httpx.HTTPError:
            return False

    # --- documents ---
    @staticmethod
    def _path(doctype: str, name: str | None = None) -> str:
        p = f"/api/resource/{quote(doctype)}"
        if name is not None:
            p += f"/{quote(str(name), safe='')}"
        return p

    async def get_doc(self, doctype: str, name: str) -> dict:
        r = await self.client.get(self._path(doctype, name))
        self._check(r)
        return r.json()["data"]

    async def exists(self, doctype: str, name: str) -> bool:
        r = await self.client.get(self._path(doctype, name))
        if r.status_code == 404:
            return False
        self._check(r)
        return True

    async def get_list(
        self,
        doctype: str,
        fields: list[str] | None = None,
        filters: list | dict | None = None,
        order_by: str | None = None,
        limit: int = 100,
    ) -> list[dict]:
        params: dict[str, Any] = {
            "fields": json.dumps(fields or ["name"]),
            "limit_page_length": limit,
        }
        if filters:
            params["filters"] = json.dumps(filters)
        if order_by:
            params["order_by"] = order_by
        r = await self.client.get(self._path(doctype), params=params)
        self._check(r)
        return r.json()["data"]

    async def insert(self, doc: dict) -> dict:
        r = await self.client.post(self._path(doc["doctype"]), json=doc)
        self._check(r)
        return r.json()["data"]

    async def update(self, doctype: str, name: str, data: dict) -> dict:
        r = await self.client.put(self._path(doctype, name), json=data)
        self._check(r)
        return r.json()["data"]

    async def call(self, method: str, **kwargs) -> Any:
        r = await self.client.post(f"/api/method/{method}", json=kwargs)
        self._check(r)
        return r.json().get("message")

    async def get_file(self, file_url: str) -> bytes:
        r = await self.client.get(file_url)
        self._check(r)
        return r.content
