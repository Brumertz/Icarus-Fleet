namespace IcarusFleet.Api.Models;

/// <summary>
/// Payload for creating a service job.
/// <see cref="ServicePriority.Regular"/> is used when the field is omitted from JSON.
/// </summary>
public sealed record CreateServiceRequest(
    string ClientName,
    string DroneModel,
    string Problem,
    double Cost,
    ServicePriority Priority = ServicePriority.Regular);
