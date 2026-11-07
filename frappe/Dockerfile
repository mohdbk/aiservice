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
# OCR for scanned CVs: Tesseract with Arabic + English, and poppler for PDF pages.
RUN apt-get update \
    && apt-get install -y --no-install-recommends redis-server \
       tesseract-ocr tesseract-ocr-ara tesseract-ocr-eng poppler-utils \
    && rm -rf /var/lib/apt/lists/*

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
