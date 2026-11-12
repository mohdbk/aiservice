# One-off job that fills an empty recruitment database with demo content through the APIs, then exits.
# Build context: backoffice/scripts. Settings: API, ADMIN_EMAIL, ADMIN_PASSWORD, PORTAL_KEY (key:secret).
FROM node:22-slim
WORKDIR /job
COPY seed-demo.mjs ./
USER node
CMD ["node", "seed-demo.mjs"]
