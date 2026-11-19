# Settings for Install-Moet.ps1 (MoET HRMS on IIS, Ministry of Economy & Tourism).
# Copy this file to moet.settings.psd1 in the same folder, fill it in, and run the script as an administrator.
# The file holds passwords and keys: keep it where only administrators can read it, or delete it after installing.
# Every setting is listed in docs/ON_PREM.md and docs/SERVICES.md.
@{
    # Where the sites go, a separate folder from this package: HRMS, portal (and ms-playwright for PDF letters).
    InstallRoot = 'D:\MoET\Hrms'

    # Site 1: the staff app, its API and the HRMS assistant, in one app. Staff only (internal network or VPN).
    Hrms = @{
        SiteName = 'MoET HRMS'
        AppPool = 'MoetHrms'
        HostName = 'hrms.moet.gov.ae'    # empty answers on any name
        IpAddress = '*'
        HttpsPort = 443
        HttpPort = 0                            # 0: no plain HTTP binding (for a test without a certificate: HttpsPort = 0, HttpPort = 8081)
        # Thumbprint of the certificate in Cert:\LocalMachine\My (needed when HttpsPort is set).
        CertificateThumbprint = ''
        # A plain HTTP binding on 127.0.0.1 only, for the portal when it is on this same server. Only the portal API
        # answers on it. 0: none.
        LocalPort = 5080
        # For the portal on another server (DMZ): an HTTPS port, on this server's internal address, where only the
        # portal API answers (same certificate). Open only this port to the portal server. 0: none.
        PortalPort = 0
        PortalIpAddress = '*'
        # Run the app pool as this account (for example a gMSA, 'MOET\svc-hrms$', with an empty password) when SQL
        # Server uses Windows sign-in. Empty: the app pool's own identity (IIS AppPool\MoetHrms).
        Identity = @{ User = ''; Password = '' }
        Settings = @{
            DB_CONNECTION = 'Server=sql01;Database=MoetHrms;Integrated Security=True;TrustServerCertificate=True;Encrypt=True'
            # Any long random values; the same two go in the Portal section below.
            PORTAL_API_KEY = ''
            PORTAL_API_SECRET = ''
            PORTAL_URL = 'https://careers.moet.gov.ae'
            HRMS_URL = 'https://hrms.moet.gov.ae'
            # The first administrator, created on the first start (password at least 12 characters).
            ADMIN_EMAIL = ''
            ADMIN_PASSWORD = ''
            # At least 16 random characters. Also encrypts secrets saved in the database: choose it once.
            AGENT_KEY = ''
            # The AI model (any OpenAI-compatible server). Empty: the AI features stay off.
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
            # Addresses or ranges allowed to call the portal API at all, even with the key: the portal server, and
            # 127.0.0.1 when the portal is on this server. Empty: any address with the key.
            PORTAL_API_ALLOWED_IPS = ''
            # CLAMAV_HOST = 'clamav01:3310'
            # Built-in exports to MinIO (BACKUP_S3_*): keep a new database empty instead of refilling it from the store.
            BACKUP_RESTORE_ON_EMPTY = '0'
        }
    }

    # Site 2: the candidate site. Public on the internet, often on a DMZ server.
    Portal = @{
        SiteName = 'MoET Careers Portal'
        AppPool = 'MoetCareersPortal'
        HostName = 'careers.moet.gov.ae'
        IpAddress = '*'
        HttpsPort = 443
        HttpPort = 0                            # for a test without a certificate: HttpsPort = 0, HttpPort = 8082
        CertificateThumbprint = ''
        LocalPort = 0
        Settings = @{
            # The HRMS's API as the portal server reaches it: http://127.0.0.1:5080 on the same server (the
            # HRMS's LocalPort); from a DMZ server, the HRMS's PortalPort, for example
            # https://hrms.moet.gov.ae:5443, allowed through the firewall from the portal server only.
            HRMS_API_URL = 'http://127.0.0.1:5080'
            # The same pair as PORTAL_API_KEY and PORTAL_API_SECRET in the HRMS section.
            CAREERS_PORTAL_API_KEY = ''
            CAREERS_PORTAL_API_SECRET = ''
            # At least 32 random characters; signs the candidates' session cookie.
            SESSION_SECRET = ''
        }
    }
}
