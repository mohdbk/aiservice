# Frappe HR + ai_recruitment image (Ministry of Economy & Tourism)
#
# Base: the official frappe/erpnext image (frappe_docker). Frappe HR and our app are
# built into the image the way frappe_docker custom images do it.
#
# DEMO ONLY: the Railway runtime scripts (entrypoint, nginx config, backup/restore)
# come from the two-workspaces Railway template at a pinned commit, fetched at build
# time. They are not part of this repo and are not needed on-prem. See
# docs/ON_PREM.md, which uses frappe_docker's compose setup instead.

ARG ERPNEXT_IMAGE=frappe/erpnext:v16.54.0

FROM alpine/git:latest AS railway_runtime
ARG TEMPLATE_REPO=https://github.com/two-workspaces/erpnext
ARG TEMPLATE_REF=29c26472c4bd094b53a4b6e574ecb00d99bfd0d2
RUN git clone "$TEMPLATE_REPO" /t && git -C /t checkout -q "$TEMPLATE_REF"

FROM ${ERPNEXT_IMAGE}
ARG HRMS_VERSION=v16.53.1

# Apps: Frappe HR (pinned tag) + ai_recruitment (from this repo).
COPY --chown=frappe:frappe apps/ai_recruitment /home/frappe/src/ai_recruitment
RUN cd /home/frappe/src/ai_recruitment \
    && git init -q && git add -A \
    && git -c user.name=build -c user.email=build@localhost commit -qm "build" \
    && cd /home/frappe/frappe-bench \
    && ln -s ../assets sites/assets \
    && echo '{"socketio_port": 9000}' > sites/common_site_config.json \
    && bench get-app --branch "$HRMS_VERSION" https://github.com/frappe/hrms \
    && bench get-app /home/frappe/src/ai_recruitment \
    && (bench compile-po-to-mo --app ai_recruitment || echo "compile-po-to-mo unavailable; CSV translations used") \
    && rm sites/assets \
    && grep -qx ai_recruitment sites/apps.txt

USER root

# Railway runs everything in one container: Redis for cache + queue.
# poppler renders PDF pages for OCR; libgl/glib/gomp are PaddleOCR runtime libraries.
RUN apt-get update \
    && apt-get install -y --no-install-recommends redis-server \
       poppler-utils libgl1 libglib2.0-0 libgomp1 \
    && rm -rf /var/lib/apt/lists/*

# PaddleOCR PP-OCRv5 (Arabic + English) in its own Python 3.12 environment: PaddleOCR supports
# Python 3.8-3.12 and Frappe v16 runs a newer Python. Models are downloaded at build time.
# If any step fails the image still builds and CVs are read by the vision model alone.
ENV PADDLE_PDX_CACHE_HOME=/home/frappe/.paddlex PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK=True
RUN ( python3 -m venv /opt/uvtool && /opt/uvtool/bin/pip install --no-cache-dir "uv>=0.5" \
      && UV_PYTHON_INSTALL_DIR=/opt/python /opt/uvtool/bin/uv venv --python 3.12 /home/frappe/paddleocr-env \
      && /opt/uvtool/bin/uv pip install --python /home/frappe/paddleocr-env/bin/python --no-cache \
           --index-strategy unsafe-best-match --extra-index-url https://www.paddlepaddle.org.cn/packages/stable/cpu/ \
           "paddlepaddle>=3.2.1,<4" "paddleocr>=3.3,<4" \
      && /home/frappe/paddleocr-env/bin/python -c "import sys; sys.path.insert(0, '/home/frappe/frappe-bench/apps/ai_recruitment/ai_recruitment'); import paddle_runner as r; r._engine('ar'); r._engine('en'); print('PaddleOCR models ready')" \
      && mkdir -p /home/frappe/.paddlex && chown -R "$(id -u frappe):$(id -g frappe)" /home/frappe/paddleocr-env /home/frappe/.paddlex \
      && touch /home/frappe/paddleocr-env/.ready \
    ) || (rm -f /home/frappe/paddleocr-env/.ready; echo "PaddleOCR not installed: CVs will be read by the vision model alone")

RUN sed -i -e 's/^worker_processes .*/worker_processes 2;/' \
      -e 's|^error_log .*|error_log stderr;|' \
      -e 's|access_log /var/log/nginx/access.log;|access_log off;|' /etc/nginx/nginx.conf

# The Railway volume mounts over sites/; the entrypoint restores these from here.
RUN cp sites/apps.txt sites/apps.json /home/frappe/

RUN env/bin/python -W ignore -m compileall -q env/lib/python3.*/site-packages/whoosh || true

COPY --from=railway_runtime /t/nginx.conf /templates/nginx/railway.conf.template
COPY --from=railway_runtime --chmod=0755 /t/railway-entrypoint.sh /railway-entrypoint.sh
COPY --from=railway_runtime --chmod=0755 /t/erpnext-backup /t/erpnext-restore /usr/local/bin/

ENTRYPOINT ["/railway-entrypoint.sh"]
CMD []
