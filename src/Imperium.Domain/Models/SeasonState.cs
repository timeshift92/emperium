namespace Imperium.Domain.Models;

public class SeasonState
{
    public Guid Id { get; set; }
    public string CurrentSeason { get; set; } = string.Empty; // Winter, Spring, Summer, Autumn
    public double AverageTemperatureC { get; set; }
    public double AveragePrecipitationMm { get; set; }
    public DateTime StartedAt { get; set; }
    public int DurationTicks { get; set; }
}
