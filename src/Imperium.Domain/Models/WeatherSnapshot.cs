namespace Imperium.Domain.Models;

public class WeatherSnapshot
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Condition { get; set; } = string.Empty; // sunny, rain, storm, drought, fog
    public int TemperatureC { get; set; }
    public int WindKph { get; set; }
    public double PrecipitationMm { get; set; }
    public double DayLengthHours { get; set; }
}
