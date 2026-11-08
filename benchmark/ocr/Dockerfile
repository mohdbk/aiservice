# Temporary OCR benchmark container (build context: repository root).
# PaddleOCR supports Python 3.8-3.12, so this uses 3.12 (Frappe v16 itself runs a newer Python).
FROM python:3.12-slim

ENV PYTHONUNBUFFERED=1 PIP_NO_CACHE_DIR=1 PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK=True

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
       tesseract-ocr tesseract-ocr-ara tesseract-ocr-eng libgl1 libglib2.0-0 libgomp1 \
    && rm -rf /var/lib/apt/lists/*

RUN pip install "paddlepaddle>=3.2.1,<4" --extra-index-url https://www.paddlepaddle.org.cn/packages/stable/cpu/ \
    && pip install "paddleocr[doc-parser]>=3.3,<4" requests \
    && python -c "import paddle, paddleocr; print('paddle', paddle.__version__, 'paddleocr', paddleocr.__version__)"

WORKDIR /bench
COPY benchmark/ocr/run_benchmark.py ./
COPY benchmark/ocr/data ./data
COPY frappe/apps/ai_recruitment/ai_recruitment/utils.py frappe/apps/ai_recruitment/ai_recruitment/paddle_runner.py ./app/

CMD ["python", "run_benchmark.py"]
