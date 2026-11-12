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
# Fonts for PDF letters (Arabic and Latin), and fontconfig so they are found.
RUN apt-get update && apt-get install -y --no-install-recommends fonts-noto-core fontconfig && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 TRUST_PROXY=1
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Moet.Recruitment.Api.dll"]
