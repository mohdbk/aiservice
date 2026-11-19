# MoET recruitment backoffice (Ministry of Economy & Tourism): the staff app, the API and the recruitment assistant in one
# image, the same app as the backoffice site on IIS. Build context: the repository root
# (docker build -f deploy/hrms.Dockerfile .).
FROM node:22-slim AS web
WORKDIR /web
COPY hrms/web/package.json ./
RUN npm install --no-audit --no-fund
COPY hrms/web/ ./
RUN npm run build && test -f dist/index.html

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/Directory.Build.props ./
COPY backend/src/Moet.Hrms.Domain/Moet.Hrms.Domain.csproj src/Moet.Hrms.Domain/
COPY backend/src/Moet.Hrms.Application/Moet.Hrms.Application.csproj src/Moet.Hrms.Application/
COPY backend/src/Moet.Hrms.Infrastructure/Moet.Hrms.Infrastructure.csproj src/Moet.Hrms.Infrastructure/
COPY backend/src/Moet.Hrms.Agent/Moet.Hrms.Agent.csproj src/Moet.Hrms.Agent/
COPY backend/src/Moet.Hrms.Api/Moet.Hrms.Api.csproj src/Moet.Hrms.Api/
RUN dotnet restore src/Moet.Hrms.Api/Moet.Hrms.Api.csproj
COPY backend/src/ src/
RUN dotnet publish src/Moet.Hrms.Api/Moet.Hrms.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Fonts for PDF letters (Arabic and Latin), and fontconfig so Chromium finds them.
RUN apt-get update && apt-get install -y --no-install-recommends fonts-noto-core fontconfig && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
COPY --from=web /web/dist wwwroot
# Chromium (headless shell) for PDF letters, with the libraries it needs, installed by Playwright's own driver from the
# published app. Kept in the image, so letters print offline.
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN /app/.playwright/node/linux-x64/node /app/.playwright/package/cli.js install --with-deps --only-shell chromium \
    && rm -rf /var/lib/apt/lists/* && chmod -R a+rX /ms-playwright /app/.playwright && chmod a+x /app/.playwright/node/linux-x64/node
ENV ASPNETCORE_HTTP_PORTS=8080 TRUST_PROXY=1
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Moet.Hrms.Api.dll"]
