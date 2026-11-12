using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Moet.Recruitment.Agent.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddTelemetry("moet-recruitment-agent", "Microsoft.Agents.AI", "Experimental.Microsoft.Extensions.AI");

builder.Services.AddAGUIServer();
builder.Services.AddRecruitmentAgent(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<AgentKeyCheck>();
app.MapGet("/healthz", () => Results.Ok(new { ok = true }));

// AG-UI: the backoffice posts a RunAgentInput and receives the run as server-sent events (text, tool calls, tool results).
app.MapAGUIServer("/agent", app.Services.GetRequiredService<AIAgent>());

app.Run();

public partial class Program;
