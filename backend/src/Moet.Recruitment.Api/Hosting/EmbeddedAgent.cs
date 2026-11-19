using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Moet.Recruitment.Agent.Hosting;

namespace Moet.Recruitment.Api.Hosting;

/// <summary>
/// Runs the recruitment assistant inside the API's process, so it needs no service or site of its own. It is served
/// by a small web host of its own on this machine's loopback address only (a free port, chosen at start), exactly as
/// it would be served on its own, and the API's /api/backoffice/agent/run forwards each run to it with the staff
/// member's session. The assistant's tools call the backoffice API in memory (<see cref="InProcessBackoffice"/>), as
/// that same person.
/// </summary>
public sealed class EmbeddedAgent(IConfiguration cfg, InProcessBackoffice backoffice, ILoggerFactory loggers) : IHostedService, IAsyncDisposable
{
    IHost? host;

    /// <summary>The assistant's address on the loopback interface, or null until it has started.</summary>
    public string? BaseUrl { get; private set; }

    public async Task StartAsync(CancellationToken ct)
    {
        // A plain host (no web defaults): under IIS the defaults would try to use IIS for this host too.
        host = new HostBuilder()
            .ConfigureAppConfiguration(c => c.AddConfiguration(cfg))
            .ConfigureServices(s =>
            {
                s.AddSingleton(loggers);
                s.AddRouting();
                s.AddAGUIServer();
                s.AddRecruitmentAgent(cfg, c => c.ConfigurePrimaryHttpMessageHandler(() => backoffice.CreateHandler()));
            })
            .ConfigureWebHost(w => w
                .UseKestrel(k => k.Listen(IPAddress.Loopback, 0))
                .Configure(app => AgentSetup.UseStandalone(app)))
            .Build();
        await host.StartAsync(ct);
        var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        BaseUrl = addresses?.FirstOrDefault()?.Replace("[::1]", "127.0.0.1");
        loggers.CreateLogger<EmbeddedAgent>().LogInformation("Assistant started on {Url}", BaseUrl);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (host != null) await host.StopAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (host is IAsyncDisposable d) await d.DisposeAsync();
        else host?.Dispose();
    }
}

/// <summary>
/// Sends the assistant's calls to the backoffice API through the API's own request pipeline in memory: no network, no
/// address to configure, and the same middleware as a browser request (sign-in from the staff member's cookie, the
/// same-origin check, rate limits, the audit trail). Program.cs captures the pipeline when it is built.
/// </summary>
public sealed class InProcessBackoffice(IServiceScopeFactory scopes, IHttpContextAccessor accessor)
{
    /// <summary>The API's pipeline from routing on, set by Program.cs.</summary>
    public RequestDelegate? Pipeline { get; set; }

    public HttpMessageHandler CreateHandler() => new Handler(this);

    sealed class Handler(InProcessBackoffice owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
            // The call runs in a fresh execution context, so the caller's HttpContext (the assistant's own request) is
            // left as it was when the call returns.
            Task<HttpResponseMessage> run;
            using (ExecutionContext.SuppressFlow()) run = Task.Run(() => owner.InvokeAsync(request, body, ct), ct);
            return await run;
        }
    }

    async Task<HttpResponseMessage> InvokeAsync(HttpRequestMessage request, byte[] body, CancellationToken ct)
    {
        var pipeline = Pipeline ?? throw new InvalidOperationException("The API pipeline is not ready yet.");
        var uri = request.RequestUri!;
        await using var scope = scopes.CreateAsyncScope();

        var headers = new HeaderDictionary();
        foreach (var h in request.Headers) headers[h.Key] = h.Value.ToArray();
        if (request.Content != null) foreach (var h in request.Content.Headers) headers[h.Key] = h.Value.ToArray();
        headers["Host"] = "localhost";

        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature
        {
            Protocol = "HTTP/1.1", Method = request.Method.Method, Scheme = "http", PathBase = "",
            Path = PathString.FromUriComponent(uri).Value ?? "/", QueryString = uri.Query, RawTarget = uri.PathAndQuery,
            Headers = headers, Body = new MemoryStream(body, writable: false),
        });
        var response = new HttpResponseFeature();
        features.Set<IHttpResponseFeature>(response);
        var output = new MemoryStream();
        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(output));
        features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection(body.Length > 0));
        features.Set<IHttpRequestLifetimeFeature>(new HttpRequestLifetimeFeature { RequestAborted = ct });
        features.Set<IHttpConnectionFeature>(new HttpConnectionFeature
        {
            ConnectionId = Guid.NewGuid().ToString("N"), RemoteIpAddress = IPAddress.Loopback, LocalIpAddress = IPAddress.Loopback,
        });
        features.Set<IServiceProvidersFeature>(new ServiceProvidersFeature { RequestServices = scope.ServiceProvider });
        features.Set<IItemsFeature>(new ItemsFeature());
        features.Set<IHttpRequestIdentifierFeature>(new HttpRequestIdentifierFeature());

        var ctx = new DefaultHttpContext(features);
        accessor.HttpContext = ctx;
        try
        {
            await pipeline(ctx);
        }
        finally
        {
            accessor.HttpContext = null;
        }

        var result = new HttpResponseMessage((HttpStatusCode)ctx.Response.StatusCode) { RequestMessage = request, Content = new ByteArrayContent(output.ToArray()) };
        foreach (var h in ctx.Response.Headers)
            if (!result.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray())) result.Content.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray());
        return result;
    }

    sealed class BodyDetection(bool canHaveBody) : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody { get; } = canHaveBody;
    }
}
