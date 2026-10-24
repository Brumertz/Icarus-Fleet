using System.Text.Json.Serialization;
using IcarusFleet.Api.Models;
using IcarusFleet.Api.Services;

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

// Interactive API docs at http://localhost:5000/ (disabled by default in production).
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Icarus Fleet API v1");
    options.RoutePrefix = string.Empty;
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
