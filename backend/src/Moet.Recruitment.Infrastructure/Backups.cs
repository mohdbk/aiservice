using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Moet.Recruitment.Infrastructure;

/// <summary>Where backups go: any S3-compatible bucket (a Railway bucket, MinIO on-prem, AWS S3).</summary>
public sealed record BackupTarget(string Endpoint, string Bucket, string Region, string AccessKey, string SecretKey, bool PathStyle, string Prefix)
{
    public static BackupTarget? From(IConfiguration cfg)
    {
        string V(string k) => (cfg[k] ?? "").Trim();
        var endpoint = V("BACKUP_S3_ENDPOINT");
        if (endpoint.Length == 0 || V("BACKUP_S3_BUCKET").Length == 0 || V("BACKUP_S3_ACCESS_KEY").Length == 0) return null;
        if (!endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase)) endpoint = "https://" + endpoint;
        return new BackupTarget(endpoint.TrimEnd('/'), V("BACKUP_S3_BUCKET"), V("BACKUP_S3_REGION") is { Length: > 0 } r ? r : "auto",
            V("BACKUP_S3_ACCESS_KEY"), V("BACKUP_S3_SECRET_KEY"), V("BACKUP_S3_PATH_STYLE") == "1", V("BACKUP_PREFIX") is { Length: > 0 } p ? p.Trim('/') : "recruitment");
    }
}

/// <summary>A minimal S3 client (AWS Signature Version 4): put, get, list and delete objects.</summary>
public sealed class S3Client(HttpClient http, BackupTarget t)
{
    Uri Url(string key, string query = "") =>
        new(t.PathStyle ? $"{t.Endpoint}/{t.Bucket}/{Escape(key)}{query}" : $"{new Uri(t.Endpoint).Scheme}://{t.Bucket}.{new Uri(t.Endpoint).Authority}/{Escape(key)}{query}");

    static string Escape(string key) => string.Join("/", key.Split('/').Select(Uri.EscapeDataString));

    async Task<HttpResponseMessage> SendAsync(HttpMethod method, string key, string query, byte[]? body, CancellationToken ct)
    {
        var url = Url(key, query.Length > 0 ? "?" + query : "");
        var now = DateTime.UtcNow;
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var day = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var payloadHash = Hex(SHA256.HashData(body ?? []));
        var host = url.IsDefaultPort ? url.Host : $"{url.Host}:{url.Port}";
        var canonicalQuery = string.Join("&", query.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2))
            .Select(p => (K: Uri.EscapeDataString(Uri.UnescapeDataString(p[0])), V: Uri.EscapeDataString(Uri.UnescapeDataString(p.Length > 1 ? p[1] : ""))))
            .OrderBy(p => p.K, StringComparer.Ordinal).Select(p => $"{p.K}={p.V}"));
        var canonical = $"{method.Method}\n{url.AbsolutePath}\n{canonicalQuery}\nhost:{host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{amzDate}\n\nhost;x-amz-content-sha256;x-amz-date\n{payloadHash}";
        var scope = $"{day}/{t.Region}/s3/aws4_request";
        var toSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))}";
        var key0 = Hmac(Encoding.UTF8.GetBytes("AWS4" + t.SecretKey), day);
        var signing = Hmac(Hmac(Hmac(key0, t.Region), "s3"), "aws4_request");
        var signature = Hex(Hmac(signing, toSign));
        using var req = new HttpRequestMessage(method, url);
        req.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        req.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        req.Headers.TryAddWithoutValidation("Authorization",
            $"AWS4-HMAC-SHA256 Credential={t.AccessKey}/{scope}, SignedHeaders=host;x-amz-content-sha256;x-amz-date, Signature={signature}");
        if (body != null) req.Content = new ByteArrayContent(body);
        var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode && !(method == HttpMethod.Delete && res.StatusCode == System.Net.HttpStatusCode.NotFound))
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            res.Dispose();
            throw new HttpRequestException($"Bucket {method} {key}: HTTP {(int)res.StatusCode} {(text.Length > 300 ? text[..300] : text)}");
        }
        return res;
    }

    static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
    static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();

    public async Task PutAsync(string key, byte[] body, CancellationToken ct) => (await SendAsync(HttpMethod.Put, key, "", body, ct)).Dispose();

    public async Task<byte[]> GetAsync(string key, CancellationToken ct)
    {
        using var res = await SendAsync(HttpMethod.Get, key, "", null, ct);
        return await res.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task DeleteAsync(string key, CancellationToken ct) => (await SendAsync(HttpMethod.Delete, key, "", null, ct)).Dispose();

    /// <summary>Keys and sizes under a prefix (up to 1000, enough for the kept backups).</summary>
    public async Task<List<(string Key, long Size, DateTime Modified)>> ListAsync(string prefix, CancellationToken ct)
    {
        using var res = await SendAsync(HttpMethod.Get, "", $"list-type=2&prefix={Uri.EscapeDataString(prefix)}", null, ct);
        var xml = XDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return xml.Descendants().Where(e => e.Name.LocalName == "Contents").Select(c => (
            c.Elements().First(e => e.Name.LocalName == "Key").Value,
            long.Parse(c.Elements().First(e => e.Name.LocalName == "Size").Value, CultureInfo.InvariantCulture),
            DateTime.Parse(c.Elements().First(e => e.Name.LocalName == "LastModified").Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal))).ToList();
    }
}

/// <summary>
/// Database backups as files in a bucket: every table's rows (except the schema record) in one zip, taken on a schedule and
/// on demand, with the newest kept. A new, empty database (no staff accounts yet) is filled from the newest backup when it
/// starts, so a demo that loses its database comes back by itself. The schema itself comes from the scripts, so a backup
/// taken by an older version restores into the current tables (columns that no longer exist are skipped).
/// </summary>
public sealed class BackupService(IConfiguration cfg, IHttpClientFactory http, ILogger<BackupService> log) : Moet.Recruitment.Application.Common.IBackups
{
    readonly BackupTarget? target = BackupTarget.From(cfg);
    static readonly string[] Skip = ["ops.SchemaVersion"];

    public bool IsConfigured => target != null;
    public int Keep => int.TryParse(cfg["BACKUP_KEEP"], out var k) && k > 0 ? k : 28;
    public double IntervalHours => double.TryParse(cfg["BACKUP_INTERVAL_HOURS"], CultureInfo.InvariantCulture, out var h) && h >= 1 ? h : 6;

    S3Client Client() => new(http.CreateClient("backups"), target ?? throw new InvalidOperationException("Backups are not set up (BACKUP_S3_*)."));
    string Conn => DependencyInjection.ConnectionString(cfg);

    public async Task<List<(string Key, long Size, DateTime Modified)>> ListAsync(CancellationToken ct) =>
        (await Client().ListAsync(target!.Prefix + "/", ct)).Where(x => x.Key.EndsWith(".zip", StringComparison.Ordinal)).OrderByDescending(x => x.Key).ToList();

    /// <summary>Takes a backup now and removes the oldest beyond the number kept. Returns the file name and size.</summary>
    public async Task<(string Key, long Size)> BackUpAsync(CancellationToken ct)
    {
        var bytes = await ExportAsync(ct);
        var key = $"{target!.Prefix}/{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
        var client = Client();
        await client.PutAsync(key, bytes, ct);
        foreach (var old in (await ListAsync(ct)).Skip(Keep)) await client.DeleteAsync(old.Key, ct);
        log.LogInformation("Backup {Key} written ({Size} bytes)", key, bytes.Length);
        return (key, bytes.Length);
    }

    async Task<List<string>> TablesAsync(SqlConnection conn, CancellationToken ct)
    {
        var list = new List<string>();
        await using var cmd = new SqlCommand("SELECT s.name + '.' + t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.is_ms_shipped = 0 ORDER BY 1", conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(r.GetString(0));
        return list.Where(t => !Skip.Contains(t)).ToList();
    }

    static string Quote(string table) => string.Join(".", table.Split('.').Select(p => $"[{p.Replace("]", "]]")}]"));

    /// <summary>Every table's rows as XML (types and nulls kept), one entry per table, zipped.</summary>
    public async Task<byte[]> ExportAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(Conn);
        await conn.OpenAsync(ct);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var table in await TablesAsync(conn, ct))
            {
                // Columns that cannot be written back (row versions, computed columns) are left out.
                var cols = new List<string>();
                await using (var c = new SqlCommand("""
                    SELECT c.name FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                    WHERE c.object_id = OBJECT_ID(@t) AND c.is_computed = 0 AND ty.name NOT IN ('timestamp', 'rowversion') ORDER BY c.column_id
                    """, conn))
                {
                    c.Parameters.AddWithValue("@t", table);
                    await using var r = await c.ExecuteReaderAsync(ct);
                    while (await r.ReadAsync(ct)) cols.Add(r.GetString(0));
                }
                var dt = new DataTable(table);
                await using (var cmd = new SqlCommand($"SELECT {string.Join(", ", cols.Select(x => $"[{x}]"))} FROM {Quote(table)}", conn) { CommandTimeout = 600 })
                await using (var reader = await cmd.ExecuteReaderAsync(ct)) dt.Load(reader);
                foreach (DataColumn col in dt.Columns) if (col.DataType == typeof(DateTime)) col.DateTimeMode = DataSetDateTime.Unspecified;
                await using var entry = zip.CreateEntry(table + ".xml", CompressionLevel.Optimal).Open();
                dt.WriteXml(entry, XmlWriteMode.WriteSchema);
            }
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Fills a new, empty database from the newest backup (BACKUP_RESTORE_ON_EMPTY, on by default when backups are set up).
    /// Runs after the schema scripts and before the first administrator is created. True when it restored.
    /// </summary>
    public async Task<bool> RestoreIfEmptyAsync(CancellationToken ct)
    {
        if (target == null || cfg["BACKUP_RESTORE_ON_EMPTY"] == "0") return false;
        await using (var conn = new SqlConnection(Conn))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("SELECT COUNT(*) FROM [identity].StaffUser", conn);
            if ((int)(await cmd.ExecuteScalarAsync(ct))! > 0) return false;
        }
        List<(string Key, long Size, DateTime Modified)> backups;
        try { backups = await ListAsync(ct); }
        catch (HttpRequestException e)
        {
            log.LogWarning("Backups cannot be listed, starting empty: {Error}", e.Message);
            return false;
        }
        if (backups.Count == 0) return false;
        log.LogWarning("The database is empty: restoring the newest backup {Key}", backups[0].Key);
        await RestoreAsync(await Client().GetAsync(backups[0].Key, ct), ct);
        return true;
    }

    /// <summary>Replaces every table's rows with the backup's, in one transaction, with constraints off while loading.</summary>
    public async Task RestoreAsync(byte[] zipBytes, CancellationToken ct)
    {
        await using var conn = new SqlConnection(Conn);
        await conn.OpenAsync(ct);
        var tables = await TablesAsync(conn, ct);
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        async Task Exec(string sql)
        {
            await using var c = new SqlCommand(sql, conn, tx) { CommandTimeout = 600 };
            await c.ExecuteNonQueryAsync(ct);
        }
        foreach (var t in tables) await Exec($"ALTER TABLE {Quote(t)} NOCHECK CONSTRAINT ALL");
        foreach (var t in tables.Where(t => zip.GetEntry(t + ".xml") != null)) await Exec($"DELETE FROM {Quote(t)}");
        foreach (var t in tables)
        {
            var entry = zip.GetEntry(t + ".xml");
            if (entry == null) continue;
            var dt = new DataTable();
            await using (var s = entry.Open()) dt.ReadXml(s);
            if (dt.Rows.Count == 0) continue;
            var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var c = new SqlCommand("""
                SELECT c.name FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                WHERE c.object_id = OBJECT_ID(@t) AND c.is_computed = 0 AND ty.name NOT IN ('timestamp', 'rowversion')
                """, conn, tx))
            {
                c.Parameters.AddWithValue("@t", t);
                await using var r = await c.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) current.Add(r.GetString(0));
            }
            using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls, tx)
            {
                DestinationTableName = Quote(t), BulkCopyTimeout = 600, BatchSize = 500,
            };
            foreach (DataColumn col in dt.Columns)
                if (current.Contains(col.ColumnName)) bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
            await bulk.WriteToServerAsync(dt, ct);
        }
        foreach (var t in tables) await Exec($"ALTER TABLE {Quote(t)} WITH CHECK CHECK CONSTRAINT ALL");
        await tx.CommitAsync(ct);
        log.LogWarning("Restore finished: {Count} tables", tables.Count);
    }
}
