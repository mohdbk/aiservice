import asyncio
from dataclasses import dataclass, field

from .frappe import Frappe


@dataclass
class State:
    svc: Frappe | None = None          # least-privilege service user client
    ready: bool = False
    phase: str = "starting"
    last_error: str = ""
    queue: asyncio.Queue = field(default_factory=asyncio.Queue)
    queued: set = field(default_factory=set)

    def enqueue(self, applicant: str) -> bool:
        if not applicant or applicant in self.queued:
            return False
        self.queued.add(applicant)
        self.queue.put_nowait(applicant)
        return True


state = State()
