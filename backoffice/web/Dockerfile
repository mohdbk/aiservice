# MoET recruitment backoffice (Ministry of Economy & Tourism): the React app served by nginx, which also forwards
# /api to the recruitment API so the browser talks to one origin. Build context: backoffice/web.
FROM node:22-slim AS build
WORKDIR /web
COPY package.json ./
RUN npm install --no-audit --no-fund
COPY . .
RUN npm run build && test -f dist/index.html

FROM nginx:1.27-alpine
ENV PORT=8080 API_URL=http://localhost:5080
COPY deploy/default.conf.template /etc/nginx/templates/default.conf.template
COPY deploy/15-resolver.sh /docker-entrypoint.d/15-resolver.sh
COPY --from=build /web/dist /usr/share/nginx/html
RUN chmod +x /docker-entrypoint.d/15-resolver.sh && rm -f /etc/nginx/conf.d/default.conf
EXPOSE 8080
