using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moet.Recruitment.Application.Common;

namespace Moet.Recruitment.Infrastructure.Files;

/// <summary>
/// Scans uploads with ClamAV (clamd's INSTREAM command over TCP). CLAMAV_HOST is "host" or "host:port" (3310 by default);
/// without it scanning is off. CLAMAV_REQUIRED=1 refuses uploads while the scanner cannot be reached (otherwise they are
/// accepted and the failure is logged).
/// </summary>
public sealed class ClamAvScanner(IConfiguration cfg, ILogger<ClamAvScanner> log) : IVirusScanner
{
    const int Chunk = 64 * 1024;

    (string Host, int Port)? Target
    {
        get
        {
            var v = (cfg["CLAMAV_HOST"] ?? "").Trim();
            if (v.Length == 0) return null;
            var i = v.LastIndexOf(':');
            return i > 0 && int.TryParse(v[(i + 1)..], out var p) ? (v[..i], p) : (v, 3310);
        }
    }

    public bool Required => cfg["CLAMAV_REQUIRED"] == "1";

    async Task<string> TalkAsync(Func<NetworkStream, CancellationToken, Task> send, CancellationToken ct)
    {
        var (host, port) = Target!.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, timeout.Token);
        await using var stream = client.GetStream();
        await send(stream, timeout.Token);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return ((await reader.ReadToEndAsync(timeout.Token)) ?? "").Trim('\0', ' ', '\n', '\r');
    }

    public async Task<(ScanOutcome Outcome, string Detail)> ScanAsync(byte[] data, CancellationToken ct = default)
    {
        if (Target == null) return (ScanOutcome.Off, "");
        try
        {
            var reply = await TalkAsync(async (s, t) =>
            {
                await s.WriteAsync("zINSTREAM\0"u8.ToArray(), t);
                var size = new byte[4];
                for (var o = 0; o < data.Length; o += Chunk)
                {
                    var n = Math.Min(Chunk, data.Length - o);
                    BinaryPrimitives.WriteInt32BigEndian(size, n);
                    await s.WriteAsync(size, t);
                    await s.WriteAsync(data.AsMemory(o, n), t);
                }
                BinaryPrimitives.WriteInt32BigEndian(size, 0);
                await s.WriteAsync(size, t);
                await s.FlushAsync(t);
            }, ct);
            // "stream: OK", "stream: Eicar-Signature FOUND", or "... ERROR".
            if (reply.EndsWith("OK", StringComparison.Ordinal)) return (ScanOutcome.Clean, "");
            if (reply.EndsWith("FOUND", StringComparison.Ordinal))
            {
                var name = reply.Replace("stream:", "").Replace("FOUND", "").Trim();
                log.LogWarning("Upload refused: virus scanner found {Signature}", name);
                return (ScanOutcome.Infected, name);
            }
            log.LogError("Virus scanner error: {Reply}", reply);
            return (ScanOutcome.Unavailable, reply);
        }
        catch (Exception e) when (e is SocketException or IOException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            log.LogError(e, "Virus scanner unreachable");
            return (ScanOutcome.Unavailable, e.Message);
        }
    }

    public async Task<string> StateAsync(CancellationToken ct = default)
    {
        if (Target == null) return "off";
        try
        {
            var reply = await TalkAsync((s, t) => s.WriteAsync("zPING\0"u8.ToArray(), t).AsTask(), ct);
            return reply == "PONG" ? "ok" : "down";
        }
        catch (Exception e) when (e is SocketException or IOException or OperationCanceledException) { return "down"; }
    }
}
