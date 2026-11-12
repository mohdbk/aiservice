using System.Diagnostics.Metrics;

namespace Moet.Recruitment.Infrastructure;

/// <summary>Counters for the background work (exported with OpenTelemetry when it is set up).</summary>
public static class AppMetrics
{
    public const string MeterName = "Moet.Recruitment";
    static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> EmailsSent = Meter.CreateCounter<long>("recruitment.emails.sent");
    public static readonly Counter<long> EmailsFailed = Meter.CreateCounter<long>("recruitment.emails.failed");
    public static readonly Counter<long> TextsSent = Meter.CreateCounter<long>("recruitment.texts.sent");
    public static readonly Counter<long> TextsFailed = Meter.CreateCounter<long>("recruitment.texts.failed");
    public static readonly Counter<long> JobsDone = Meter.CreateCounter<long>("recruitment.jobs.done");
    public static readonly Counter<long> JobsFailed = Meter.CreateCounter<long>("recruitment.jobs.failed");
    public static readonly Counter<long> Backups = Meter.CreateCounter<long>("recruitment.backups");
}
