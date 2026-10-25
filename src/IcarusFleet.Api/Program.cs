using System.Text.Json.Serialization;
using IcarusFleet.Api.Models;
using IcarusFleet.Api.Services;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Required by SwaggerGen to discover the minimal-API endpoints.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<ServiceQueueManager>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

// The dashboard is the landing view: "/" serves it directly and the static
// assets (styles.css, main.js) resolve from the same wwwroot.
// Both UI routes are excluded from the OpenAPI description: they return HTML,
// so listing them in Swagger would make "Execute" return markup instead of JSON.
app.UseStaticFiles();

// `dotnet run` and Visual Studio use the project folder as content root, but a
// double-clicked .exe keeps the caller's working directory. When that happens
// the project wwwroot is unreachable, so fall back to the copy shipped next to
// the binaries (see the csproj CopyToOutputDirectory entry).
var projectWwwroot = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
var binaryWwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (Directory.Exists(binaryWwwroot) &&
    !string.Equals(Path.GetFullPath(binaryWwwroot), Path.GetFullPath(projectWwwroot), StringComparison.OrdinalIgnoreCase))
{
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(binaryWwwroot) });
}

var dashboardPath = new[] { projectWwwroot, binaryWwwroot }
    .Select(root => Path.Combine(root, "index.html"))
    .FirstOrDefault(File.Exists);

IResult ServeDashboard() => dashboardPath is not null && File.Exists(dashboardPath)
    ? Results.File(dashboardPath, "text/html; charset=utf-8")
    : Results.NotFound(new { error = "Dashboard not found." });

app.MapGet("/", ServeDashboard).ExcludeFromDescription();

// Keeps the previous dashboard URL working instead of dead-ending on a 404.
app.MapGet("/dashboard", () => Results.Redirect("/")).ExcludeFromDescription();

// Interactive API docs moved to /swagger so the dashboard owns the root URL.
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Icarus Fleet API v1");
    options.RoutePrefix = "swagger";
});

app.MapGet("/health", () =>
    Results.Ok(new { status = "ok", service = "icarus-fleet-api" }));

app.MapGet("/api/services", (ServiceQueueManager manager) =>
    Results.Ok(manager.Snapshot()));

app.MapPost("/api/services", (CreateServiceRequest request, ServiceQueueManager manager) =>
{
    try
    {
        var job = manager.Enqueue(request);
        return Results.Created($"/api/services/{job.ServiceTag}", job);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/services/{serviceTag:int}/process", (int serviceTag, ServiceQueueManager manager) =>
{
    var job = manager.Process(serviceTag);
    return job is null
        ? Results.NotFound(new { error = $"Service {serviceTag} is not in any queue." })
        : Results.Ok(job);
});

app.MapDelete("/api/services/finished/{serviceTag:int}", (int serviceTag, ServiceQueueManager manager) =>
    manager.RemoveFinished(serviceTag)
        ? Results.NoContent()
        : Results.NotFound(new { error = $"Finished service {serviceTag} not found." }));

app.Run();

// Exposes the implicit Program class to future integration tests (WebApplicationFactory).
public partial class Program { }
