using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Moet.Recruitment.Api.Hosting;

/// <summary>
/// OpenTelemetry traces, metrics and logs, sent over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set (Azure Monitor, Grafana,
/// Elastic, Jaeger or any OpenTelemetry collector). Without it nothing is exported and nothing changes.
/// </summary>
public static class Telemetry
{
    public static WebApplicationBuilder AddTelemetry(this WebApplicationBuilder builder, string service, params string[] meters)
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])) return builder;
        var resource = ResourceBuilder.CreateDefault().AddService(builder.Configuration["OTEL_SERVICE_NAME"] ?? service, serviceVersion: "1.0.0");
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(builder.Configuration["OTEL_SERVICE_NAME"] ?? service, serviceVersion: "1.0.0"))
            .WithTracing(t => t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => ctx.Request.Path != "/healthz").AddHttpClientInstrumentation().AddOtlpExporter())
            .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation().AddMeter(meters).AddOtlpExporter());
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.SetResourceBuilder(resource);
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
            o.AddOtlpExporter();
        });
        return builder;
    }
}
