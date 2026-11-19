using System.Collections.Concurrent;

namespace Moet.Careers.Portal;

/// <summary>
/// In-memory fixed-window rate limits, per address or per account. One portal server per DMZ host is expected; with
/// several, put the limits on the reverse proxy as well.
/// </summary>
public class RateLimiter
{
    sealed class Window { public int Count; public long Reset; }

    readonly ConcurrentDictionary<string, Window> hits = new();
    long lastSweep;

    public static readonly (int Count, int Seconds) All = (300, 60), LoginIp = (20, 900), LoginEmail = (8, 900), Signup = (5, 3600),
        Apply = (20, 3600), Correction = (10, 3600), AccountEmail = (3, 3600), AccountIp = (10, 3600), Verify = (20, 3600),
        Reset = (10, 3600), AssessmentWrite = (1200, 3600), CvDraft = (5, 3600);

    /// <summary>Counts a hit; false when the limit for the window is passed.</summary>
    public virtual bool Hit(string key, (int Count, int Seconds) limit)
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref lastSweep) > 60_000)
        {
            Interlocked.Exchange(ref lastSweep, now);
            foreach (var (k, w) in hits) if (w.Reset <= now) hits.TryRemove(k, out _);
        }
        var e = hits.GetOrAdd(key, _ => new Window { Count = 0, Reset = now + limit.Seconds * 1000L });
        lock (e)
        {
            if (e.Reset <= now) { e.Count = 0; e.Reset = now + limit.Seconds * 1000L; }
            e.Count++;
            return e.Count <= limit.Count;
        }
    }
}
