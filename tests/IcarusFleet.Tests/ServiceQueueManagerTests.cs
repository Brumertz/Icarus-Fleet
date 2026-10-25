using System.Runtime.CompilerServices;
using IcarusFleet.Api.Models;
using IcarusFleet.Api.Services;
using Xunit;

namespace IcarusFleet.Tests;

/// <summary>
/// Unit tests for the queue domain ported from the original Icarus WPF app:
/// tags, surcharge, formatting rules, FIFO order and the finished list.
/// </summary>
public class ServiceQueueManagerTests : IClassFixture<TestReporterFixture>
{
    private static void Run(Action action, [CallerMemberName] string testName = "") =>
        TestReporter.Record(action, testName);

    private static CreateServiceRequest Job(
        string client = "brandon brumertz",
        string model = "DJI Mavic 3",
        string problem = "left front arm broken",
        double cost = 100,
        ServicePriority priority = ServicePriority.Regular)
        => new(client, model, problem, cost, priority);

    [Fact]
    public void Enqueue_AssignsTagsFrom100WithStep10() => Run(() =>
    {
        var manager = new ServiceQueueManager();

        var first = manager.Enqueue(Job());
        var second = manager.Enqueue(Job());
        var snapshot = manager.Snapshot();

        Assert.Equal(100, first.ServiceTag);
        Assert.Equal(110, second.ServiceTag);
        Assert.Equal(120, snapshot.NextServiceTag);
    });

    [Fact]
    public void Enqueue_RegularJob_KeepsBaseCost() => Run(() =>
    {
        var manager = new ServiceQueueManager();

        var job = manager.Enqueue(Job(cost: 50, priority: ServicePriority.Regular));

        Assert.Equal(50, job.Cost);
    });

    [Fact]
    public void Enqueue_ExpressJob_Applies15PercentSurcharge() => Run(() =>
    {
        var manager = new ServiceQueueManager();

        var round = manager.Enqueue(Job(cost: 100, priority: ServicePriority.Express));
        var odd = manager.Enqueue(Job(cost: 99.99, priority: ServicePriority.Express));

        Assert.Equal(115, round.Cost);
        Assert.Equal(114.99, odd.Cost);
    });

    [Fact]
    public void Enqueue_SortsJobsIntoTheirPriorityQueue() => Run(() =>
    {
        var manager = new ServiceQueueManager();

        manager.Enqueue(Job(priority: ServicePriority.Regular));
        manager.Enqueue(Job(priority: ServicePriority.Express));
        var snapshot = manager.Snapshot();

        Assert.Single(snapshot.Regular);
        Assert.Single(snapshot.Express);
    });

    [Fact]
    public void Enqueue_MissingRequiredFields_Throws() => Run(() =>
    {
        var manager = new ServiceQueueManager();

        Assert.Throws<ArgumentException>(() => manager.Enqueue(Job(client: "  ")));
        Assert.Throws<ArgumentException>(() => manager.Enqueue(Job(model: "")));
        Assert.Throws<ArgumentException>(() => manager.Enqueue(Job(problem: " ")));
    });

    [Fact]
    public void Enqueue_NegativeCost_Throws() => Run(() =>
    {
        var manager = new ServiceQueueManager();

        Assert.Throws<ArgumentException>(() => manager.Enqueue(Job(cost: -1)));
    });

    [Fact]
    public void ClientName_IsStoredTitleCased() => Run(() =>
    {
        var manager = new ServiceQueueManager();

        var job = manager.Enqueue(Job(client: "  brandon brumertz "));

        Assert.Equal("Brandon Brumertz", job.ClientName);
    });

    [Fact]
    public void Problem_IsStoredSentenceCased() => Run(() =>
    {
        var manager = new ServiceQueueManager();

        var job = manager.Enqueue(Job(problem: "LEFT front arm broken"));

        Assert.Equal("Left front arm broken", job.Problem);
    });

    [Fact]
    public void Cost_IsRoundedToTwoDecimals() => Run(() =>
    {
        var manager = new ServiceQueueManager();

        var job = manager.Enqueue(Job(cost: 10.456));

        Assert.Equal(10.46, job.Cost);
    });

    [Fact]
    public void Process_MovesJobToFinishedAndStampsIt() => Run(() =>
    {
        var manager = new ServiceQueueManager();
        manager.Enqueue(Job());

        var processed = manager.Process(100);
        var snapshot = manager.Snapshot();

        Assert.NotNull(processed);
        Assert.NotNull(processed.ProcessedAt);
        Assert.Empty(snapshot.Regular);
        Assert.Empty(snapshot.Express);
        Assert.Single(snapshot.Finished);
    });

    [Fact]
    public void Process_PreservesFifoOrder() => Run(() =>
    {
        var manager = new ServiceQueueManager();
        manager.Enqueue(Job());
        manager.Enqueue(Job());
        manager.Enqueue(Job());

        var first = manager.Process(100);
        var second = manager.Process(110);

        Assert.Equal(100, first?.ServiceTag);
        Assert.Equal(110, second?.ServiceTag);
        Assert.Single(manager.Snapshot().Regular);
    });

    [Fact]
    public void Process_UnknownTag_ReturnsNull() => Run(() =>
    {
        var manager = new ServiceQueueManager();
        manager.Enqueue(Job());

        Assert.Null(manager.Process(999));
        Assert.Single(manager.Snapshot().Regular);
    });

    [Fact]
    public void RemoveFinished_DeletesOnlyOnce() => Run(() =>
    {
        var manager = new ServiceQueueManager();
        manager.Enqueue(Job());
        manager.Process(100);

        Assert.True(manager.RemoveFinished(100));
        Assert.False(manager.RemoveFinished(100));
        Assert.Empty(manager.Snapshot().Finished);
    });
}
