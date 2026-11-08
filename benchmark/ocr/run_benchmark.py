"""OCR head-to-head on the synthetic CV set. Runs in its own container (see Dockerfile).

Engines
  tesseract        Tesseract 5, ara+eng
  paddleocr        PP-OCRv5 (production runner: Arabic+English, RTL ordering, reversed-Arabic fix)
  paddleocr_vl     PaddleOCR-VL document parser (CPU here; no text classes dropped)
  qwen_vision      Qwen3.8 27B reading the page image (production prompt)
  hybrid_paddle    PP-OCRv5 draft + image -> Qwen3.8 (production hybrid prompt and guard)
  hybrid_vl        PaddleOCR-VL draft + image -> Qwen3.8

Metrics per page: word recall / precision / F1 (order-insensitive content), CER (order-sensitive,
best of the allowed reading orders), seconds. Results are printed as lines starting with BENCH.
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
import time
import traceback

import requests

HERE = pathlib.Path(__file__).parent
sys.path.insert(0, str(HERE / "app"))
import utils as U  # noqa: E402  (production ai_recruitment/utils.py)

DATA = HERE / "data"
MODEL = os.environ.get("LLM_MODEL", "qwen/qwen3.8-27b")
BASE = os.environ.get("LLM_BASE_URL", "https://openrouter.ai/api/v1").rstrip("/")
KEY = os.environ.get("LLM_API_KEY", "")
ONLY = [e for e in os.environ.get("BENCH_ENGINES", "").split(",") if e]


def log(*a):
    print(*a, flush=True)


# ---------------- engines ----------------
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
        kwargs = {"device": "cpu"}
        try:
            _VL = PaddleOCRVL(markdown_ignore_labels=[], **kwargs)
        except TypeError:
            _VL = PaddleOCRVL(**kwargs)
        log("BENCH-INFO paddleocr_vl ready")
    with tempfile.TemporaryDirectory() as tmp:
        for res in _VL.predict(path):
            res.save_to_markdown(save_path=tmp)
        md = "\n".join(pathlib.Path(f).read_text(encoding="utf-8", errors="ignore")
                       for f in sorted(glob.glob(os.path.join(tmp, "**", "*.md"), recursive=True)))
    return U.markdown_to_text(md)


def _img_part(path):
    mime = "image/jpeg" if path.endswith(".jpg") else "image/png"
    with open(path, "rb") as f:
        return {"type": "image_url", "image_url": {"url": f"data:{mime};base64," + base64.b64encode(f.read()).decode()}}


def _chat(prompt, path):
    body = {"model": MODEL, "temperature": 0, "max_tokens": 8000,
            "messages": [{"role": "user", "content": [{"type": "text", "text": prompt}, _img_part(path)]}]}
    if "openrouter.ai" in BASE:
        body["provider"] = {"data_collection": "deny"}
        body["reasoning"] = {"enabled": False}
    for attempt in range(3):
        r = requests.post(f"{BASE}/chat/completions", json=body, timeout=300,
                          headers={"Authorization": f"Bearer {KEY}", "X-Title": "MoET OCR benchmark"})
        if r.status_code == 400 and "reasoning" in body:
            body.pop("reasoning")
            continue
        if r.status_code in (429, 500, 502, 503) and attempt < 2:
            time.sleep(10 * (attempt + 1))
            continue
        r.raise_for_status()
        return r.json()["choices"][0]["message"].get("content") or ""
    raise RuntimeError("LLM request failed")


def qwen_vision(path):
    text = _chat(U.VISION_OCR_PROMPT, path)
    return "" if U.has_repetition_loop(text) else text


def hybrid(path, draft, source):
    prompt = U.HYBRID_OCR_PROMPT.format(source=source, draft=(draft or "").strip()[:20000] or "(empty)")
    return U.hybrid_result(_chat(prompt, path), draft)


# ---------------- scoring ----------------
def score(text, truths):
    f1 = max((U.word_f1(t, text) for t in truths), key=lambda r: r["f1"])
    cer = min(U.error_rates(t, text)["cer"] for t in truths)
    return {**f1, "cer": cer}


def timed(fn, *a):
    t0 = time.time()
    try:
        out = fn(*a)
        return out, round(time.time() - t0, 1), None
    except Exception as e:
        traceback.print_exc()
        return "", round(time.time() - t0, 1), f"{type(e).__name__}: {str(e)[:200]}"


def main():
    truth = json.loads((DATA / "truth.json").read_text(encoding="utf-8"))
    engines = ["tesseract", "paddleocr", "paddleocr_vl", "qwen_vision", "hybrid_paddle", "hybrid_vl"]
    if ONLY:
        engines = [e for e in engines if e in ONLY]
    if not KEY:
        log("BENCH-INFO no LLM_API_KEY: skipping Qwen engines")
        engines = [e for e in engines if not e.startswith(("qwen", "hybrid"))]
    log(f"BENCH-INFO pages={len(truth)} engines={engines} model={MODEL}")

    results = {}  # (page, engine) -> dict
    outputs = {}
    pool = cf.ThreadPoolExecutor(max_workers=4)
    futures = {}

    def record(page, eng, text, secs, err):
        r = {"page": page, "engine": eng, "seconds": secs, "error": err, **score(text, truth[page]["truth"])}
        results[(page, eng)] = r
        outputs[(page, eng)] = text
        log("BENCH " + json.dumps({k: r[k] for k in ("page", "engine", "f1", "recall", "precision", "cer", "seconds", "error")},
                                  ensure_ascii=False))

    pages = sorted(truth)
    # Local engines run sequentially (CPU-bound); API engines run in a thread pool meanwhile.
    for page in pages:
        path = str(DATA / truth[page]["image"])
        if "qwen_vision" in engines:
            futures[pool.submit(timed, qwen_vision, path)] = (page, "qwen_vision")
        for eng in ("tesseract", "paddleocr"):
            if eng in engines:
                record(page, eng, *timed(globals()[eng], path))
        if "hybrid_paddle" in engines:
            draft = outputs.get((page, "paddleocr")) or outputs.get((page, "tesseract")) or ""
            src = "the PaddleOCR engine" if outputs.get((page, "paddleocr")) else "the Tesseract OCR engine"
            futures[pool.submit(timed, hybrid, path, draft, src)] = (page, "hybrid_paddle")
    for page in pages:
        path = str(DATA / truth[page]["image"])
        if "paddleocr_vl" in engines:
            record(page, "paddleocr_vl", *timed(paddleocr_vl, path))
            if "hybrid_vl" in engines and outputs.get((page, "paddleocr_vl")):
                futures[pool.submit(timed, hybrid, path, outputs[(page, "paddleocr_vl")],
                                    "the PaddleOCR-VL document parser")] = (page, "hybrid_vl")
    for fut in cf.as_completed(futures):
        page, eng = futures[fut]
        record(page, eng, *fut.result())
    pool.shutdown()

    # ---------------- summary ----------------
    def agg(rows):
        ok = [r for r in rows if not r["error"]]
        n = len(rows)
        if not n:
            return None
        mean = lambda k: round(sum(r[k] for r in rows) / n, 3)  # errors count as zero-accuracy pages
        return {"pages": n, "errors": n - len(ok), "f1": mean("f1"), "recall": mean("recall"),
                "precision": mean("precision"), "cer": round(sum(r["cer"] for r in rows) / n, 3),
                "sec_per_page": round(sum(r["seconds"] for r in rows) / n, 1)}

    summary = {}
    for eng in engines:
        rows = [r for (p, e), r in results.items() if e == eng]
        summary[eng] = {"all": agg(rows)}
        for seg_key in ("lang", "layout"):
            for seg in sorted({truth[p][seg_key] for p in pages}):
                summary[eng][f"{seg_key}:{seg}"] = agg([r for r in rows if truth[r["page"]][seg_key] == seg])
    for eng, segs in summary.items():
        log("BENCH-SUMMARY " + json.dumps({"engine": eng, **segs}, ensure_ascii=False))
    ranked = sorted((s["all"]["f1"], eng) for eng, s in summary.items() if s["all"])
    log("BENCH-RANKING " + " > ".join(f"{e} ({f:.3f})" for f, e in reversed(ranked)))
    # A few worst outputs for inspection
    for (page, eng), text in sorted(outputs.items()):
        if results[(page, eng)]["f1"] < 0.6:
            log("BENCH-SAMPLE " + json.dumps({"page": page, "engine": eng, "text": text[:600]}, ensure_ascii=False))
    log("BENCH-DONE")


if __name__ == "__main__":
    main()
