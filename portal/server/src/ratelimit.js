// In-memory fixed-window rate limiter. One portal instance per DMZ host is expected; with several instances
// put the limits on the reverse proxy as well (see docs/ON_PREM.md).
export class RateLimiter {
  constructor() {
    this.hits = new Map();
    this.timer = setInterval(() => this.sweep(), 60_000);
    this.timer.unref?.();
  }
  hit(key, limit, windowSec, now = Date.now()) {
    const e = this.hits.get(key);
    if (!e || e.reset <= now) {
      this.hits.set(key, { count: 1, reset: now + windowSec * 1000 });
      return true;
    }
    e.count += 1;
    return e.count <= limit;
  }
  sweep(now = Date.now()) {
    for (const [k, e] of this.hits) if (e.reset <= now) this.hits.delete(k);
  }
}

export const LIMITS = {
  all: [300, 60],
  login_ip: [20, 900],
  login_email: [8, 900],
  signup: [5, 3600],
  apply: [20, 3600],
  correction: [10, 3600],
  account_email: [3, 3600],
  account_ip: [10, 3600],
  verify: [20, 3600],
  reset: [10, 3600],
  assessment_write: [1200, 3600],
};
