"""PaddleOCR (PP-OCRv5) runner. Executed with the isolated PaddleOCR Python (3.8-3.12), never imported
by Frappe. Usage: python paddle_runner.py text <image> [<image> ...]   -> {"text": "..."} (production post-processing)
       python paddle_runner.py <ar|en> <image> [...]          -> raw boxes per page
Raw form: [{"width": w, "height": h, "items": [{"text", "score", "box": [x0, y0, x1, y1]}]}]
"""
import json
import os
import sys


def _tolist(v):
    return v.tolist() if hasattr(v, "tolist") else list(v or [])


def _fields(res):
    """PaddleOCR 3.x results are dict-like; fall back to the .json form."""
    try:
        return res["rec_texts"], res["rec_scores"], res.get("rec_boxes") if hasattr(res, "get") else res["rec_boxes"]
    except (KeyError, TypeError):
        data = getattr(res, "json", {}) or {}
        data = data.get("res", data)
        return data.get("rec_texts", []), data.get("rec_scores", []), data.get("rec_boxes", [])


_ENGINES = {}


def _engine(lang):
    from paddleocr import PaddleOCR

    if lang not in _ENGINES:
        _ENGINES[lang] = PaddleOCR(
        lang=lang,
        text_detection_model_name=os.environ.get("PADDLEOCR_DET_MODEL", "PP-OCRv5_mobile_det"),
        use_doc_orientation_classify=False,
        use_doc_unwarping=False,
        use_textline_orientation=False,
        device=os.environ.get("PADDLEOCR_DEVICE", "cpu"),
        )
    return _ENGINES[lang]


def run(lang, paths):
    from PIL import Image

    ocr = _engine(lang)
    pages = []
    for path in paths:
        with Image.open(path) as im:
            w, h = im.size
        items = []
        for res in ocr.predict(path):
            texts, scores, boxes = _fields(res)
            boxes = _tolist(boxes)
            for i, t in enumerate(texts or []):
                if not t or i >= len(boxes):
                    continue
                b = [float(x) for x in _tolist(boxes[i])[:4]]
                items.append({"text": t, "score": float(_tolist(scores)[i]) if i < len(scores) else None, "box": b})
        pages.append({"width": w, "height": h, "items": items})
    return pages


def read_text(paths):
    """Arabic model first (it also reads English); mostly-English pages are re-read with the English
    model. Lines are ordered (right to left for Arabic, columns kept apart) and reversed Arabic fixed."""
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from utils import arabic_ratio, fix_reversed_arabic, order_ocr_lines

    def render(pages):
        return "\n\n".join(order_ocr_lines(p["items"], p.get("width") or 0) for p in pages)

    text = fix_reversed_arabic(render(run("ar", paths)))
    if arabic_ratio(text) < 0.05:
        text = render(run("en", paths))
    return text


def main():
    mode, paths = sys.argv[1], sys.argv[2:]
    out = {"text": read_text(paths)} if mode == "text" else run(mode, paths)
    sys.stdout.write("\n@@JSON@@" + json.dumps(out, ensure_ascii=False))


if __name__ == "__main__":
    main()
