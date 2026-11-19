# HRMS API

.NET 10 API for the MoET recruitment system (Ministry of Economy & Tourism), in Clean Architecture layers:
`Domain` (rules), `Application` (use cases and ports), `Infrastructure` (SQL Server, email, model, files) and `Api` (HTTP).

- Build the image from the repository root with `docker build -f deploy/hrms.Dockerfile .`. It listens on 8080 and serves the staff app, the API and the assistant.
- The schema scripts in `src/Moet.Hrms.Infrastructure/Persistence/Schema` run on start (`MIGRATE_ON_START`).
- Settings and how the services connect: [docs/SERVICES.md](../docs/SERVICES.md).
