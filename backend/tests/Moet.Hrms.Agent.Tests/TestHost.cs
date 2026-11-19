using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Moet.Hrms.Agent.Backoffice;
using Moet.Hrms.Agent.Hosting;

namespace Moet.Hrms.Agent.Tests;

/// <summary>
/// The agent served on its own at /agent (AG-UI) in an in-memory test server, with a stand-in backoffice API and,
/// optionally, a stand-in model.
/// </summary>
sealed class TestHost : IDisposable
{
    readonly WebApplication app;

    public TestHost(HttpMessageHandler backoffice, IChatClient? model, IDictionary<string, string?> settings)
    {
        var b = WebApplication.CreateBuilder();
        b.WebHost.UseTestServer();
        foreach (var (k, v) in settings) b.Configuration[k] = v;
        b.Services.AddAGUIServer();
        b.Services.AddRecruitmentAgent(b.Configuration, c => c.ConfigurePrimaryHttpMessageHandler(() => backoffice));
        if (model != null) b.Services.AddSingleton(model);
        app = b.Build();
        AgentSetup.UseStandalone(app);
        app.StartAsync().GetAwaiter().GetResult();
    }

    public HttpClient CreateClient() => app.GetTestClient();

    public void Dispose() => app.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
