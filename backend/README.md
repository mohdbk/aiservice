# Recruitment API

.NET 10 API for the MoET recruitment system (Ministry of Economy & Tourism), in Clean Architecture layers:
`Domain` (rules), `Application` (use cases and ports), `Infrastructure` (SQL Server, email, model, files) and `Api` (HTTP).

- Build the image with `docker build backend`. It listens on 8080 and serves only the API.
- The schema scripts in `src/Moet.Recruitment.Infrastructure/Persistence/Schema` run on start (`MIGRATE_ON_START`).
- Settings and how the services connect: [docs/SERVICES.md](../docs/SERVICES.md).
