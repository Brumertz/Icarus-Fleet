using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using IcarusFleet.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace IcarusFleet.Tests;

/// <summary>
/// End-to-end integration tests for the ASP.NET Core Minimal API endpoints:
/// Health, static HTML dashboard, Swagger docs, queue inspection, enqueueing,
/// processing, and deleting jobs.
/// </summary>
public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IClassFixture<TestReporterFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    private static async Task RunAsync(Func<Task> action, [CallerMemberName] string testName = "") =>
        await TestReporter.RecordAsync(action, testName);

    [Fact]
    public Task GetHealth_ReturnsOkAndStatusHealthy() => RunAsync(async () =>
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
        Assert.Equal("icarus-fleet-api", body.Service);
    });

    [Fact]
    public Task GetRoot_ServesDashboardHtml() => RunAsync(async () =>
    {
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Icarus Fleet", content);
    });

    [Fact]
    public Task GetServices_ReturnsSnapshot() => RunAsync(async () =>
    {
        var response = await _client.GetAsync("/api/services");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var snapshot = await response.Content.ReadFromJsonAsync<ServicesSnapshot>(JsonOptions);
        Assert.NotNull(snapshot);
        Assert.True(snapshot.NextServiceTag >= 100);
    });

    [Fact]
    public Task PostService_EnqueuesRegularJob() => RunAsync(async () =>
    {
        var request = new CreateServiceRequest(
            ClientName: "Alice Integration",
            DroneModel: "DJI Mini 4 Pro",
            Problem: "Gimbal calibration error",
            Cost: 150.0,
            Priority: ServicePriority.Regular);

        var response = await _client.PostAsJsonAsync("/api/services", request, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var job = await response.Content.ReadFromJsonAsync<ServiceJob>(JsonOptions);
        Assert.NotNull(job);
        Assert.Equal("Alice Integration", job.ClientName);
        Assert.Equal(150.0, job.Cost);
        Assert.Equal(ServicePriority.Regular, job.Priority);
        Assert.True(job.ServiceTag >= 100);
    });

    [Fact]
    public Task PostService_EnqueuesExpressJobWithSurcharge() => RunAsync(async () =>
    {
        var request = new CreateServiceRequest(
            ClientName: "Bob Express",
            DroneModel: "Autel Evo Lite+",
            Problem: "Battery pin replacement",
            Cost: 200.0,
            Priority: ServicePriority.Express);

        var response = await _client.PostAsJsonAsync("/api/services", request, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var job = await response.Content.ReadFromJsonAsync<ServiceJob>(JsonOptions);
        Assert.NotNull(job);
        Assert.Equal(230.0, job.Cost); // +15% of 200 is 230
        Assert.Equal(ServicePriority.Express, job.Priority);
    });

    [Fact]
    public Task ProcessJob_MovesJobToFinished() => RunAsync(async () =>
    {
        var createRequest = new CreateServiceRequest(
            ClientName: "Charlie Queue",
            DroneModel: "Skydio 2+",
            Problem: "Motor ESC desoldered",
            Cost: 80.0,
            Priority: ServicePriority.Regular);

        var postResponse = await _client.PostAsJsonAsync("/api/services", createRequest, JsonOptions);
        var created = await postResponse.Content.ReadFromJsonAsync<ServiceJob>(JsonOptions);
        Assert.NotNull(created);

        var processResponse = await _client.PostAsync($"/api/services/{created.ServiceTag}/process", null);
        Assert.Equal(HttpStatusCode.OK, processResponse.StatusCode);

        var processed = await processResponse.Content.ReadFromJsonAsync<ServiceJob>(JsonOptions);
        Assert.NotNull(processed);
        Assert.NotNull(processed.ProcessedAt);
        Assert.Equal(created.ServiceTag, processed.ServiceTag);
    });

    [Fact]
    public Task DeleteFinishedJob_ReturnsNoContent() => RunAsync(async () =>
    {
        var createRequest = new CreateServiceRequest(
            ClientName: "Dana Delete",
            DroneModel: "DJI Inspire 3",
            Problem: "Propeller chip",
            Cost: 50.0,
            Priority: ServicePriority.Regular);

        var postResponse = await _client.PostAsJsonAsync("/api/services", createRequest, JsonOptions);
        var created = await postResponse.Content.ReadFromJsonAsync<ServiceJob>(JsonOptions);
        Assert.NotNull(created);

        await _client.PostAsync($"/api/services/{created.ServiceTag}/process", null);

        var deleteResponse = await _client.DeleteAsync($"/api/services/finished/{created.ServiceTag}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var secondDelete = await _client.DeleteAsync($"/api/services/finished/{created.ServiceTag}");
        Assert.Equal(HttpStatusCode.NotFound, secondDelete.StatusCode);
    });

    [Fact]
    public Task GetSwagger_ServesInteractiveDocumentation() => RunAsync(async () =>
    {
        var response = await _client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Swagger UI", content);
    });

    private record HealthResponse(string Status, string Service);
}
