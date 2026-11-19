# Settings for Install-Moet.ps1 (MoET recruitment system on IIS, Ministry of Economy & Tourism).
# Copy this file next to the script as moet.settings.psd1, fill it in, and run the script as an administrator.
# The file holds passwords and keys: keep it where only administrators can read it, or delete it after installing.
# Every setting is listed in docs/ON_PREM.md and docs/SERVICES.md.
@{
    # Each app gets its own folder underneath: api, agent, backoffice, portal (and ms-playwright for PDF letters).
    InstallRoot = 'D:\MoET\Recruitment'

    # node.exe (Node.js 22 LTS) for the careers portal.
    NodePath = 'C:\Program Files\nodejs\node.exe'

    # Staff site. Public to staff only (internal network or VPN).
    Backoffice = @{
        SiteName = 'MoET Recruitment Backoffice'
        AppPool = 'MoetRecruitmentBackoffice'
        HostName = 'recruitment.moet.gov.ae'    # empty answers on any name
        IpAddress = '*'
        HttpsPort = 443
        HttpPort = 0                            # 0: no plain HTTP binding
        # Thumbprint of the certificate in Cert:\LocalMachine\My. Empty: HTTP only (testing; staff sign-in needs HTTPS
        # unless the API has COOKIE_SECURE = '0').
        CertificateThumbprint = ''
        # Where the API answers, as this server sees it.
        ApiUrl = 'http://127.0.0.1:5080'
    }

    # Candidate site. Public on the internet, usually on a DMZ server.
    Portal = @{
        SiteName = 'MoET Careers Portal'
        AppPool = 'MoetCareersPortal'
        HostName = 'careers.moet.gov.ae'
        IpAddress = '*'
        HttpsPort = 443
        HttpPort = 0
        CertificateThumbprint = ''
        Settings = @{
            # The API as the portal server sees it. On a DMZ server: the API server's internal address (HTTPS if the
            # traffic crosses networks), allowed through the firewall from the portal server only.
            RECRUITMENT_API_URL = 'http://127.0.0.1:5080'
            # The same pair as PORTAL_API_KEY and PORTAL_API_SECRET on the API.
            CAREERS_PORTAL_API_KEY = ''
            CAREERS_PORTAL_API_SECRET = ''
            # At least 32 random characters; signs the candidates' session cookie.
            SESSION_SECRET = ''
        }
    }

    # The API. Not public: bound to this machine (or to the internal address the portal server reaches).
    Api = @{
        SiteName = 'MoET Recruitment API'
        AppPool = 'MoetRecruitmentApi'
        IpAddress = '127.0.0.1'
        HttpPort = 5080
        HttpsPort = 0
        HostName = ''
        CertificateThumbprint = ''
        # Run the app pool as this account (for example a gMSA, 'MOET\svc-recruit$', with an empty password) when
        # SQL Server uses Windows sign-in. Empty: the app pool's own identity (IIS AppPool\MoetRecruitmentApi).
        Identity = @{ User = ''; Password = '' }
        Settings = @{
            DB_CONNECTION = 'Server=sql01;Database=MoetRecruitment;Integrated Security=True;TrustServerCertificate=True;Encrypt=True'
            PORTAL_API_KEY = ''
            PORTAL_API_SECRET = ''
            PORTAL_URL = 'https://careers.moet.gov.ae'
            BACKOFFICE_URL = 'https://recruitment.moet.gov.ae'
            ADMIN_EMAIL = ''
            ADMIN_PASSWORD = ''                 # first start only; at least 12 characters
            AGENT_URL = 'http://127.0.0.1:5090'
            AGENT_KEY = ''                      # same value on the agent; changing it later breaks stored secrets
            LLM_BASE_URL = ''
            LLM_MODEL = ''
            LLM_API_KEY = ''
            SMTP_HOST = ''
            SMTP_PORT = '587'
            SMTP_USER = ''
            SMTP_PASSWORD = ''
            SMTP_FROM = ''
            SMTP_TLS = '1'
            TIME_ZONE = 'Asia/Dubai'
            # CLAMAV_HOST = 'clamav01:3310'
            # Built-in exports to MinIO (BACKUP_S3_*): keep a new database empty instead of refilling it from the store.
            BACKUP_RESTORE_ON_EMPTY = '0'
        }
    }

    # The assistant. Not public; only the API calls it.
    Agent = @{
        SiteName = 'MoET Recruitment Agent'
        AppPool = 'MoetRecruitmentAgent'
        IpAddress = '127.0.0.1'
        HttpPort = 5090
        HttpsPort = 0
        HostName = ''
        CertificateThumbprint = ''
        Settings = @{
            BACKEND_URL = 'http://127.0.0.1:5080'
            AGENT_KEY = ''
            LLM_BASE_URL = ''
            LLM_MODEL = ''
            LLM_API_KEY = ''
        }
    }
}
