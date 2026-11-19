# MoET careers portal (Ministry of Economy & Tourism): the candidate app and its .NET server in one small image.
# Build context: the portal/ folder.
FROM node:22-slim AS web
WORKDIR /web
COPY web/package.json ./
RUN npm install --no-audit --no-fund
COPY web/ ./
RUN npm run build && test -f dist/index.html

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server
WORKDIR /src
COPY host/Directory.Build.props ./
COPY host/Moet.Careers.Portal/Moet.Careers.Portal.csproj Moet.Careers.Portal/
RUN dotnet restore Moet.Careers.Portal/Moet.Careers.Portal.csproj
COPY host/Moet.Careers.Portal/ Moet.Careers.Portal/
RUN dotnet publish Moet.Careers.Portal/Moet.Careers.Portal.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=server /app .
COPY --from=web /web/dist wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Moet.Careers.Portal.dll"]
