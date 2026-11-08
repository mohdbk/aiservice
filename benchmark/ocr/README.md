# OCR benchmark (CVs, Arabic and English)

Head-to-head of OCR engines on fictional CVs with exact ground truth.

| Layout | What it simulates |
|---|---|
| `ats` | single-column, ATS-friendly CV rendered at about 200 DPI |
| `designed` | two columns, coloured sidebar and header band, skills table, Naskh-style serif font |
| `scanned` | the ATS page scanned: about 150 DPI, grey, tilted, noisy, blurred, JPEG |
| `photo` | the designed page as a phone photo: perspective, uneven light, low resolution, heavy JPEG |

Contents: 2 English, 2 Arabic, 1 mixed Arabic/English CV, 20 pages in total.

Engines: Tesseract 5 (ara+eng), PaddleOCR PP-OCRv5 (production runner), PaddleOCR-VL, Qwen3.8 27B vision,
PP-OCRv5 + Qwen3.8 hybrid, and PaddleOCR-VL + Qwen3.8 hybrid. The vision and hybrid prompts, the guards and the
PaddleOCR post-processing are imported from the production app (`ai_recruitment/utils.py`, `paddle_runner.py`).

Metrics: word F1 (order-insensitive content accuracy; recall = words found, precision = words not invented),
CER (order-sensitive, best of the allowed reading orders for two-column pages), seconds per page.

## Regenerate the set
```bash
python make_dataset.py && node render.js && python degrade.py
```

## Run
```bash
docker build -f benchmark/ocr/Dockerfile -t ocr-bench .
docker run --rm -e LLM_API_KEY=... -e LLM_MODEL=qwen/qwen3.8-27b ocr-bench
# optional: -e BENCH_ENGINES=tesseract,paddleocr
```

## Limitations
Synthetic pages with three fonts are cleaner and more uniform than real CVs. Use the results to choose an
approach, then confirm on 10 to 20 real CVs with **AI OCR Test** in Frappe. PaddleOCR-VL speed here is
CPU-only. On a GPU it is much faster.
