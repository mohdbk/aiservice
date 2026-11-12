# The recruitment agent service (Microsoft Agent Framework, AG-UI). Runs beside the API; no database of its own.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Moet.Recruitment.Agent/Moet.Recruitment.Agent.csproj src/Moet.Recruitment.Agent/
RUN dotnet restore src/Moet.Recruitment.Agent/Moet.Recruitment.Agent.csproj
COPY src/ src/
RUN dotnet publish src/Moet.Recruitment.Agent/Moet.Recruitment.Agent.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Moet.Recruitment.Agent.dll"]
