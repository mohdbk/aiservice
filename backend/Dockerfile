# MoET recruitment API with the recruitment backoffice (Ministry of Economy & Tourism).
# Build context: the repository root (docker build -f backend/Dockerfile .).
FROM node:22-slim AS backoffice
WORKDIR /web
COPY backoffice/web/package.json ./
RUN npm install --no-audit --no-fund
COPY backoffice/web/ ./
RUN npm run build && test -f dist/index.html

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY backend/Directory.Build.props ./
COPY backend/src/Moet.Recruitment.Domain/Moet.Recruitment.Domain.csproj src/Moet.Recruitment.Domain/
COPY backend/src/Moet.Recruitment.Application/Moet.Recruitment.Application.csproj src/Moet.Recruitment.Application/
COPY backend/src/Moet.Recruitment.Infrastructure/Moet.Recruitment.Infrastructure.csproj src/Moet.Recruitment.Infrastructure/
COPY backend/src/Moet.Recruitment.Api/Moet.Recruitment.Api.csproj src/Moet.Recruitment.Api/
RUN dotnet restore src/Moet.Recruitment.Api/Moet.Recruitment.Api.csproj
COPY backend/src/ src/
RUN dotnet publish src/Moet.Recruitment.Api/Moet.Recruitment.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app .
COPY --from=backoffice /web/dist ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080 TRUST_PROXY=1
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Moet.Recruitment.Api.dll"]
