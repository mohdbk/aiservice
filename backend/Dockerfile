# MoET recruitment API (Ministry of Economy & Tourism). Serves only the API; the backoffice and portal are separate services.
# Build context: backend/ (docker build backend).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Moet.Recruitment.Domain/Moet.Recruitment.Domain.csproj src/Moet.Recruitment.Domain/
COPY src/Moet.Recruitment.Application/Moet.Recruitment.Application.csproj src/Moet.Recruitment.Application/
COPY src/Moet.Recruitment.Infrastructure/Moet.Recruitment.Infrastructure.csproj src/Moet.Recruitment.Infrastructure/
COPY src/Moet.Recruitment.Api/Moet.Recruitment.Api.csproj src/Moet.Recruitment.Api/
RUN dotnet restore src/Moet.Recruitment.Api/Moet.Recruitment.Api.csproj
COPY src/ src/
RUN dotnet publish src/Moet.Recruitment.Api/Moet.Recruitment.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Fonts for PDF letters (Arabic and Latin), and fontconfig so Chromium finds them.
RUN apt-get update && apt-get install -y --no-install-recommends fonts-noto-core fontconfig && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
# Chromium (headless shell) for PDF letters, with the libraries it needs, installed by Playwright's own driver from the
# published app. Kept in the image, so letters print offline.
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN /app/.playwright/node/linux-x64/node /app/.playwright/package/cli.js install --with-deps --only-shell chromium \
    && rm -rf /var/lib/apt/lists/* && chmod -R a+rX /ms-playwright /app/.playwright && chmod a+x /app/.playwright/node/linux-x64/node
ENV ASPNETCORE_HTTP_PORTS=8080 TRUST_PROXY=1
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Moet.Recruitment.Api.dll"]
