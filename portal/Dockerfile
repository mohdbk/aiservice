# MoET careers portal: React app + DMZ backend in one small image (Ministry of Economy & Tourism).
# Build context: the portal/ folder.
FROM node:22-slim AS web
WORKDIR /web
COPY web/package.json ./
RUN npm install --no-audit --no-fund
COPY web/ ./
RUN npm run build && test -f dist/index.html

FROM node:22-slim
ENV NODE_ENV=production PORT=8080 WEB_DIST=/app/web/dist
WORKDIR /app
COPY server/package.json server/package.json
COPY server/src server/src
COPY --from=web /web/dist web/dist
USER node
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s CMD node -e "fetch('http://127.0.0.1:'+(process.env.PORT||8080)+'/healthz').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"
CMD ["node", "server/src/server.js"]
