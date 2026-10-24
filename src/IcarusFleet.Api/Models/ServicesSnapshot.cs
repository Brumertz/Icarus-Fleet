namespace IcarusFleet.Api.Models;

/// <summary>Read-only view of the three collections plus the next available tag.</summary>
public sealed record ServicesSnapshot(
    IReadOnlyList<ServiceJob> Regular,
    IReadOnlyList<ServiceJob> Express,
    IReadOnlyList<ServiceJob> Finished,
    int NextServiceTag);
