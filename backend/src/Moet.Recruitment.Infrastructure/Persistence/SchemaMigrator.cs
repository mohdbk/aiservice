using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Moet.Recruitment.Infrastructure.Persistence;

/// <summary>
/// Applies the numbered SQL scripts in Persistence/Schema that have not run yet, each in one transaction, and
/// records them in ops.SchemaVersion. DBAs can read and run the same scripts by hand (GO separates batches).
/// </summary>
public static partial class SchemaMigrator
{
    [GeneratedRegex(@"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)] private static partial Regex GoRe();

    public static IReadOnlyList<(string Name, string Sql)> Scripts()
    {
        var asm = Assembly.GetExecutingAssembly();
        return asm.GetManifestResourceNames().Where(n => n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(n =>
            {
                using var s = asm.GetManifestResourceStream(n)!;
                using var r = new StreamReader(s);
                var name = n.Split('.')[^2];
                return (name, r.ReadToEnd());
            })
            .OrderBy(x => x.name, StringComparer.Ordinal).ToList();
    }

    public static async Task MigrateAsync(string connectionString, ILogger logger, CancellationToken ct = default)
    {
        await EnsureDatabaseAsync(connectionString, logger, ct);
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        var applied = new HashSet<string>();
        await using (var check = new SqlCommand("SELECT OBJECT_ID('ops.SchemaVersion', 'U')", conn))
        {
            if (await check.ExecuteScalarAsync(ct) is not DBNull and not null)
            {
                await using var cmd = new SqlCommand("SELECT ScriptName FROM ops.SchemaVersion", conn);
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct)) applied.Add(rd.GetString(0));
            }
        }
        foreach (var (name, sql) in Scripts())
        {
            if (applied.Contains(name)) continue;
            logger.LogInformation("Applying schema script {Script}", name);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            foreach (var batch in GoRe().Split(sql).Where(b => b.Trim().Length > 0))
            {
                await using var cmd = new SqlCommand(batch, conn, tx) { CommandTimeout = 300 };
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await using (var rec = new SqlCommand("INSERT INTO ops.SchemaVersion (ScriptName, AppliedAt) VALUES (@n, SYSUTCDATETIME())", conn, tx))
            {
                rec.Parameters.AddWithValue("@n", name);
                await rec.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
    }

    /// <summary>Creates the database when the server allows it (handy for development and demos).</summary>
    static async Task EnsureDatabaseAsync(string connectionString, ILogger logger, CancellationToken ct)
    {
        var b = new SqlConnectionStringBuilder(connectionString);
        var db = b.InitialCatalog;
        if (string.IsNullOrEmpty(db)) return;
        b.InitialCatalog = "master";
        try
        {
            await using var conn = new SqlConnection(b.ConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("IF DB_ID(@db) IS NULL EXEC('CREATE DATABASE ' + @q)", conn);
            cmd.Parameters.AddWithValue("@db", db);
            cmd.Parameters.AddWithValue("@q", "[" + db.Replace("]", "]]") + "]");
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException e)
        {
            logger.LogWarning("Could not check or create database {Db} (it must already exist): {Message}", db, e.Message);
        }
    }
}
