using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Readiness is a flag we can flip at runtime so you can watch Kubernetes pull a
// pod out of the Service endpoints without killing it. Liveness stays healthy
// the whole time, which is exactly the distinction the two probes exist for.
var readiness = new ReadinessState();
builder.Services.AddSingleton(readiness);

builder.Services
    .AddHealthChecks()
    // Tagged "ready" only: the liveness probe filters these out.
    .AddCheck("readiness-flag", () => readiness.IsReady
        ? HealthCheckResult.Healthy("Accepting traffic")
        : HealthCheckResult.Unhealthy("Marked not ready"), tags: ["ready"]);

var app = builder.Build();

// Identity of the pod that answered. Populated by the downward API in the Helm
// chart; falls back to the machine name when you run this outside Kubernetes.
var instance = new
{
    pod = Environment.GetEnvironmentVariable("POD_NAME") ?? Environment.MachineName,
    ns = Environment.GetEnvironmentVariable("POD_NAMESPACE") ?? "(none)",
    node = Environment.GetEnvironmentVariable("NODE_NAME") ?? "(none)",
};

app.MapGet("/", () => Results.Ok(new
{
    service = "health-api",
    instance.pod,
    @namespace = instance.ns,
    instance.node,
    environment = app.Environment.EnvironmentName,
    utc = DateTimeOffset.UtcNow,
}));

// Liveness: "is the process wedged?" Never fails on purpose -- if this fails,
// the kubelet restarts the container.
app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = _ => false, // run no checks, just prove the app can serve a request
    ResponseWriter = WriteHealthResponse,
});

// Readiness: "should this pod receive traffic right now?" Failing here removes
// the pod from the Service's endpoints; it does NOT restart the container.
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse,
});

// Learning aid, not a production pattern: no auth, mutates global state.
// curl -X POST .../admin/ready/false  then watch `kubectl get endpoints`.
app.MapPost("/admin/ready/{state:bool}", (bool state, ReadinessState s) =>
{
    s.IsReady = state;
    app.Logger.LogWarning("Readiness manually set to {State}", state);
    return Results.Ok(new { ready = s.IsReady });
});

app.Logger.LogInformation(
    "health-api starting: pod={Pod} namespace={Namespace} env={Environment}",
    instance.pod, instance.ns, app.Environment.EnvironmentName);

app.Run();

static Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsync(JsonSerializer.Serialize(new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), description = e.Value.Description }),
    }));
}

internal sealed class ReadinessState
{
    public volatile bool IsReady = true;
}
