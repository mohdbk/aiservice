# MoET Recruitment on IIS

Made by the Ministry of Economy & Tourism. The full guide is `docs/ON_PREM.md`, section 3; this is the short version.

Two IIS sites, both plain ASP.NET Core apps. IIS needs only the .NET 10 Hosting Bundle.

| IIS site | Who reaches it | What is in it |
|---|---|---|
| MoET Recruitment Backoffice | Staff | The staff app, its API and the recruitment assistant, in one app |
| MoET Careers Portal | Internet (often a DMZ server) | The candidate site and its small server, which calls the backoffice's API with a service key |

1. On the server: IIS (with Application Initialization) and the .NET 10 Hosting Bundle.
2. Get the **built** package (not GitHub's "Download ZIP", which is the source code): GitHub Actions, **IIS Package**,
   latest green run, artifact `moet-recruitment-iis`. Unzip it twice (the download holds `moet-recruitment-iis.zip`),
   for example to `C:\MoET\package`. It has the folders `backoffice`, `portal`, `ms-playwright` and `iis`.
3. In Windows PowerShell, run as administrator:
   ```powershell
   cd C:\MoET\package
   copy iis\moet.settings.example.psd1 iis\moet.settings.psd1
   notepad iis\moet.settings.psd1
   ```
   Fill it in and save. `InstallRoot` is a different folder, for example `D:\MoET\Recruitment`.
4. Then, one line at a time:
   ```powershell
   Set-ExecutionPolicy -Scope Process Bypass
   .\iis\Install-Moet.ps1
   ```
   On a separate DMZ server for the portal: `.\iis\Install-Moet.ps1 -Roles Portal`; on the internal server: `-Roles Backoffice`.
5. Updating: unzip the new package and run the same two lines with the same settings file.
