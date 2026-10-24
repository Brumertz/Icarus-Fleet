using IcarusFleet.Api.Models;

namespace IcarusFleet.Api.Services;

/// <summary>
/// In-memory queue domain ported from the original WPF desktop app:
/// Regular and Express FIFO queues, a Finished list, sequential service tags
/// starting at 100 (step 10) and a 15% surcharge applied to Express jobs on enqueue.
/// Thread-safe behind a single lock — registered as a singleton in the API.
/// </summary>
public class ServiceQueueManager
{
    public const int FirstServiceTag = 100;
    public const int ServiceTagStep = 10;
    public const double ExpressSurchargeRate = 0.15;

    private readonly object _gate = new();
    private readonly Queue<ServiceJob> _regular = new();
    private readonly Queue<ServiceJob> _express = new();
    private readonly List<ServiceJob> _finished = new();
    private int _nextServiceTag = FirstServiceTag;

    /// <summary>
    /// Validates and enqueues a new job, assigning the next sequential tag.
    /// Express jobs get the 15% surcharge applied before enqueueing.
    /// </summary>
    /// <exception cref="ArgumentException">When a required field is missing or the cost is negative.</exception>
    public ServiceJob Enqueue(CreateServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientName))
            throw new ArgumentException("Client name is required.");
        if (string.IsNullOrWhiteSpace(request.DroneModel))
            throw new ArgumentException("Drone model is required.");
        if (string.IsNullOrWhiteSpace(request.Problem))
            throw new ArgumentException("Service problem is required.");
        if (request.Cost < 0)
            throw new ArgumentException("Cost cannot be negative.");

        lock (_gate)
        {
            var job = new ServiceJob
            {
                ServiceTag = _nextServiceTag,
                ClientName = request.ClientName,
                DroneModel = request.DroneModel,
                Problem = request.Problem,
                Priority = request.Priority,
                Cost = request.Priority == ServicePriority.Express
                    ? Math.Round(request.Cost * (1 + ExpressSurchargeRate), 2)
                    : request.Cost,
            };

            if (job.Priority == ServicePriority.Express)
                _express.Enqueue(job);
            else
                _regular.Enqueue(job);

            _nextServiceTag += ServiceTagStep;
            return job;
        }
    }

    /// <summary>
    /// Moves the job with the given tag (from either queue) to the Finished list,
    /// stamping its <see cref="ServiceJob.ProcessedAt"/>. Returns null when not found.
    /// </summary>
    public ServiceJob? Process(int serviceTag)
    {
        lock (_gate)
        {
            var job = Extract(_regular, serviceTag) ?? Extract(_express, serviceTag);
            if (job is null)
                return null;

            job.ProcessedAt = DateTime.UtcNow;
            _finished.Add(job);
            return job;
        }
    }

    /// <summary>Removes a job from the Finished list. Returns false when not found.</summary>
    public bool RemoveFinished(int serviceTag)
    {
        lock (_gate)
        {
            var job = _finished.FirstOrDefault(j => j.ServiceTag == serviceTag);
            return job is not null && _finished.Remove(job);
        }
    }

    /// <summary>Returns a consistent copy of both queues, the finished list and the next tag.</summary>
    public ServicesSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new ServicesSnapshot(
                _regular.ToList(),
                _express.ToList(),
                _finished.ToList(),
                _nextServiceTag);
        }
    }

    /// <summary>
    /// Removes and returns the first job with the given tag, preserving the
    /// relative FIFO order of the remaining jobs.
    /// </summary>
    private static ServiceJob? Extract(Queue<ServiceJob> queue, int serviceTag)
    {
        ServiceJob? found = null;
        var count = queue.Count;
        for (var i = 0; i < count; i++)
        {
            var job = queue.Dequeue();
            if (found is null && job.ServiceTag == serviceTag)
                found = job;
            else
                queue.Enqueue(job);
        }
        return found;
    }
}
