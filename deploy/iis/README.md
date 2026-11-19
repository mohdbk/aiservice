# MoET Recruitment on IIS

Made by the Ministry of Economy & Tourism. The full guide is `docs/ON_PREM.md`; this is the short version.

| IIS site | Public | What runs | Default binding |
|---|---|---|---|
| MoET Recruitment Backoffice | Staff | Built React app; `/api` forwarded to the API (URL Rewrite + ARR) | `https://recruitment.moet.gov.ae` |
| MoET Careers Portal | Internet (DMZ) | The portal's Node server, started by IIS through the ASP.NET Core Module | `https://careers.moet.gov.ae` |
| MoET Recruitment API | No | .NET 10 API (in-process), background jobs, PDF letters | `http://127.0.0.1:5080` |
| MoET Recruitment Agent | No | .NET 10 assistant, called by the API only | `http://127.0.0.1:5090` |

1. On the server: IIS with Application Initialization, the .NET 10 Hosting Bundle, URL Rewrite 2.1 and Application
   Request Routing 3.0 (backoffice server), Node.js 22 LTS (portal server).
2. Get the package: run the **IIS Package** workflow in GitHub Actions and download `moet-recruitment-iis`, or run
   `deploy\iis\Build-Package.ps1` on a Windows build machine.
3. Unzip it on the server, copy `iis\moet.settings.example.psd1` to `iis\moet.settings.psd1` and fill it in.
4. As an administrator: `powershell -ExecutionPolicy Bypass -File iis\Install-Moet.ps1`.
   On a separate DMZ server for the portal: `... -Roles Portal`; on the internal server: `... -Roles Api,Agent,Backoffice`.
5. Updating: unzip the new package and run the same command with the same settings file.
