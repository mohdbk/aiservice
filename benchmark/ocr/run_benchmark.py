"""OCR head-to-head on the synthetic CV set. Runs in its own container (see Dockerfile).

Engines
  tesseract        Tesseract 5, ara+eng
  paddleocr        PP-OCRv5 (production runner: Arabic+English, RTL ordering, reversed-Arabic fix)
  paddleocr_vl     PaddleOCR-VL document parser (CPU here; no text classes dropped)
  qwen_vision      Qwen3.8 27B reading the page image (production prompt)
  hybrid_paddle    PP-OCRv5 draft + image -> Qwen3.8 (production hybrid prompt and guard)
  hybrid_vl        PaddleOCR-VL draft + image -> Qwen3.8

Local Paddle engines run in worker subprocesses so a native crash only loses one page; API engines run
in a thread pool and are recorded as they finish. Results are printed as lines starting with BENCH.
"""
import base64
import concurrent.futures as cf
import glob
import json
import os
import pathlib
import subprocess
import sys
import tempfile
import threading
import time
import traceback

HERE = pathlib.Path(__file__).parent
sys.path.insert(0, str(HERE / "app"))
import utils as U  # noqa: E402  (production ai_recruitment/utils.py)

DATA = HERE / "data"
MODEL = os.environ.get("LLM_MODEL", "qwen/qwen3.8-27b")
BASE = os.environ.get("LLM_BASE_URL", "https://openrouter.ai/api/v1").rstrip("/")
KEY = os.environ.get("LLM_API_KEY", "")
ONLY = [e for e in os.environ.get("BENCH_ENGINES", "").split(",") if e]
MARK = "@@R@@"
os.environ.setdefault("FLAGS_use_mkldnn", "0")


def log(*a):
    print(*a, flush=True)


# ---------------- local engines (run inside worker processes) ----------------
def tesseract(path):
    r = subprocess.run(["tesseract", path, "stdout", "-l", "ara+eng"], capture_output=True, timeout=300)
    return r.stdout.decode("utf-8", "ignore")


def paddleocr(path):
    import paddle_runner  # production runner (app/)

    return paddle_runner.read_text([path])


_VL = None


def paddleocr_vl(path):
    global _VL
    from paddleocr import PaddleOCRVL

    if _VL is None:
        base = {"device": "cpu", "enable_mkldnn": False}
        for extra in ({"markdown_ignore_labels": []}, {}):
            try:
                _VL = PaddleOCRVL(**extra, **base)
                break
            except TypeError:
                continue
        print("BENCH-INFO paddleocr_vl ready", file=sys.stderr, flush=True)
    with tempfile.TemporaryDirectory() as tmp:
        for res in _VL.predict(path):
            res.save_to_markdown(save_path=tmp)
        md = "\n".join(pathlib.Path(f).read_text(encoding="utf-8", errors="ignore")
                       for f in sorted(glob.glob(os.path.join(tmp, "**", "*.md"), recursive=True)))
    return U.markdown_to_text(md)


def worker(engine, paths):
    """Process pages one by one, emitting one result line per page."""
    fn = globals()[engine]
    for path in paths:
        t0 = time.time()
        try:
            text, err = fn(path), None
        except Exception as e:
            traceback.print_exc()
            text, err = "", f"{type(e).__name__}: {str(e)[:200]}"
        sys.stdout.write(MARK + json.dumps({"path": path, "text": text, "seconds": round(time.time() - t0, 1),
                                            "error": err}, ensure_ascii=False) + "\n")
        sys.stdout.flush()


def run_worker(engine, paths, on_result):
    """Run a worker; if it dies, record the page it was on as failed and restart after it."""
    remaining = list(paths)
    while remaining:
        proc = subprocess.Popen([sys.executable, __file__, "--worker", engine, *remaining],
                                stdout=subprocess.PIPE, text=True, encoding="utf-8")
        done = []
        for line in proc.stdout:
            if line.startswith(MARK):
                r = json.loads(line[len(MARK):])
                done.append(r["path"])
                on_result(r)
        proc.wait()
        remaining = [p for p in remaining if p not in done]
        if remaining:
            on_result({"path": remaining[0], "text": "", "seconds": 0.0,
                       "error": f"worker crashed (exit code {proc.returncode})"})
            remaining = remaining[1:]


# ---------------- API engines ----------------
def _img_part(path):
    mime = "image/jpeg" if path.endswith(".jpg") else "image/png"
    with open(path, "rb") as f:
        return {"type": "image_url", "image_url": {"url": f"data:{mime};base64," + base64.b64encode(f.read()).decode()}}


def _chat(prompt, path):
    import requests

    body = {"model": MODEL, "temperature": 0, "max_tokens": 8000,
            "messages": [{"role": "user", "content": [{"type": "text", "text": prompt}, _img_part(path)]}]}
    if "openrouter.ai" in BASE:
        body["provider"] = {"data_collection": "deny"}
        body["reasoning"] = {"enabled": False}
    for attempt in range(4):
        r = requests.post(f"{BASE}/chat/completions", json=body, timeout=300,
                          headers={"Authorization": f"Bearer {KEY}", "X-Title": "MoET OCR benchmark"})
        if r.status_code == 400 and "reasoning" in body:
            body.pop("reasoning")
            continue
        if r.status_code in (408, 429, 500, 502, 503, 504) and attempt < 3:
            time.sleep(10 * (attempt + 1))
            continue
        if r.status_code >= 400:
            raise RuntimeError(f"HTTP {r.status_code}: {r.text[:200]}")
        return r.json()["choices"][0]["message"].get("content") or ""
    raise RuntimeError("LLM request failed after retries")


def qwen_vision(path):
    text = _chat(U.VISION_OCR_PROMPT, path)
    return "" if U.has_repetition_loop(text) else text


def hybrid(path, draft, source):
    prompt = U.HYBRID_OCR_PROMPT.format(source=source, draft=(draft or "").strip()[:20000] or "(empty)")
    return U.hybrid_result(_chat(prompt, path), draft)


def timed(fn, *a):
    t0 = time.time()
    try:
        return fn(*a), round(time.time() - t0, 1), None
    except Exception as e:
        traceback.print_exc()
        return "", round(time.time() - t0, 1), f"{type(e).__name__}: {str(e)[:200]}"


# ---------------- scoring ----------------
def score(text, truths):
    f1 = max((U.word_f1(t, text) for t in truths), key=lambda r: r["f1"])
    cer = min(U.error_rates(t, text)["cer"] for t in truths)
    return {**f1, "cer": cer}


def main():
    truth = json.loads((DATA / "truth.json").read_text(encoding="utf-8"))
    engines = ["tesseract", "paddleocr", "paddleocr_vl", "qwen_vision", "hybrid_paddle", "hybrid_vl"]
    if ONLY:
        engines = [e for e in engines if e in ONLY]
    if not KEY:
        log("BENCH-INFO no LLM_API_KEY: skipping Qwen engines")
        engines = [e for e in engines if not e.startswith(("qwen", "hybrid"))]
    log(f"BENCH-INFO pages={len(truth)} engines={engines} model={MODEL}")

    pages = sorted(truth)
    path_of = {p: str(DATA / truth[p]["image"]) for p in pages}
    page_of = {v: k for k, v in path_of.items()}
    results, outputs, lock = {}, {}, threading.Lock()
    pool = cf.ThreadPoolExecutor(max_workers=4)
    futures = []

    def record(page, eng, text, secs, err):
        r = {"page": page, "engine": eng, "seconds": secs, "error": err, **score(text, truth[page]["truth"])}
        with lock:
            results[(page, eng)] = r
            outputs[(page, eng)] = text
        log("BENCH " + json.dumps({k: r[k] for k in ("page", "engine", "f1", "recall", "precision", "cer", "seconds", "error")},
                                  ensure_ascii=False))

    def submit(eng, fn, *args, page):
        fut = pool.submit(timed, fn, *args)
        fut.add_done_callback(lambda f: record(page, eng, *f.result()))
        futures.append(fut)

    if "qwen_vision" in engines:
        for p in pages:
            submit("qwen_vision", qwen_vision, path_of[p], page=p)

    def local(eng, hybrid_name, source):
        def on_result(r):
            page = page_of[r["path"]]
            record(page, eng, r["text"], r["seconds"], r["error"])
            if hybrid_name in engines:
                draft = r["text"] or outputs.get((page, "tesseract"), "")
                src = source if r["text"] else "the Tesseract OCR engine"
                submit(hybrid_name, hybrid, r["path"], draft, src, page=page)
        run_worker(eng, [path_of[p] for p in pages], on_result)

    if "tesseract" in engines:
        run_worker("tesseract", [path_of[p] for p in pages],
                   lambda r: record(page_of[r["path"]], "tesseract", r["text"], r["seconds"], r["error"]))
    if "paddleocr" in engines:
        local("paddleocr", "hybrid_paddle", "the PaddleOCR engine")
    if "paddleocr_vl" in engines:
        local("paddleocr_vl", "hybrid_vl", "the PaddleOCR-VL document parser")
    cf.wait(futures)
    pool.shutdown()

    # ---------------- summary ----------------
    def agg(rows):
        n = len(rows)
        if not n:
            return None
        mean = lambda k: round(sum(r[k] for r in rows) / n, 3)  # failed pages count as zero accuracy
        return {"pages": n, "errors": sum(1 for r in rows if r["error"]), "f1": mean("f1"), "recall": mean("recall"),
                "precision": mean("precision"), "cer": mean("cer"),
                "sec_per_page": round(sum(r["seconds"] for r in rows) / n, 1)}

    summary = {}
    for eng in engines:
        rows = [r for (p, e), r in results.items() if e == eng]
        summary[eng] = {"all": agg(rows)}
        for key in ("lang", "layout"):
            for seg in sorted({truth[p][key] for p in pages}):
                summary[eng][f"{key}:{seg}"] = agg([r for r in rows if truth[r["page"]][key] == seg])
    for eng, segs in summary.items():
        log("BENCH-SUMMARY " + json.dumps({"engine": eng, **segs}, ensure_ascii=False))
    ranked = sorted((s["all"]["f1"], eng) for eng, s in summary.items() if s["all"])
    log("BENCH-RANKING " + " > ".join(f"{e} ({f:.3f})" for f, e in reversed(ranked)))
    for (page, eng), text in sorted(outputs.items()):
        if results[(page, eng)]["f1"] < 0.6:
            log("BENCH-SAMPLE " + json.dumps({"page": page, "engine": eng, "error": results[(page, eng)]["error"],
                                              "text": text[:400]}, ensure_ascii=False))
    log("BENCH-DONE")


if __name__ == "__main__":
    if len(sys.argv) > 2 and sys.argv[1] == "--worker":
        worker(sys.argv[2], sys.argv[3:])
    else:
        main()
