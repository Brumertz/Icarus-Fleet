using System.Globalization;

namespace IcarusFleet.Api.Models;

/// <summary>Service priority. Express jobs pay a 15% surcharge.</summary>
public enum ServicePriority
{
    Regular,
    Express,
}

/// <summary>
/// A single drone service request. Ported from the original WPF desktop app
/// (Icarus_Drone_Service_App) preserving its formatting rules:
/// client name in Title Case, problem in Sentence Case, cost rounded to 2 decimals.
/// </summary>
public class ServiceJob
{
    private string? _clientName;
    private string? _droneModel;
    private string? _problem;
    private double _cost;

    /// <summary>Unique service tag, assigned sequentially from 100 in steps of 10.</summary>
    public int ServiceTag { get; init; }

    /// <summary>Client name, stored trimmed and returned in Title Case.</summary>
    public string ClientName
    {
        get => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(_clientName ?? string.Empty);
        set => _clientName = value?.Trim() ?? string.Empty;
    }

    /// <summary>Drone model identifier, stored trimmed.</summary>
    public string DroneModel
    {
        get => _droneModel ?? string.Empty;
        set => _droneModel = value?.Trim() ?? string.Empty;
    }

    /// <summary>Problem description, returned in Sentence Case.</summary>
    public string Problem
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_problem))
                return string.Empty;
            var s = _problem.Trim();
            return char.ToUpper(s[0]) + s.Substring(1).ToLower();
        }
        set => _problem = value?.Trim() ?? string.Empty;
    }

    /// <summary>Final cost in USD, rounded to 2 decimals (Express already includes the surcharge).</summary>
    public double Cost
    {
        get => _cost;
        set => _cost = Math.Round(value, 2);
    }

    /// <summary>Regular or Express. Determines which FIFO queue the job belongs to.</summary>
    public ServicePriority Priority { get; set; }

    /// <summary>UTC timestamp of enqueueing.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>UTC timestamp set when the job moves to the Finished list.</summary>
    public DateTime? ProcessedAt { get; set; }
}
