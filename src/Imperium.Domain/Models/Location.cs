namespace Imperium.Domain.Models;

public class Location
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Population { get; set; }
    // Simple city treasury for taxes/fees
    public decimal Treasury { get; set; }
    // Coordinates (WGS84); optional for legacy data
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    // Simple adjacency list for road network
    public string? NeighborsJson { get; set; }
    // 🏛 Cultural context (e.g. hellenic, tribal, nomadic, etc.)
    public string Culture { get; set; } = "unknown";
    // Biome classification for the location (forest|plain|mountain|coast)
    public string Biome { get; set; } = "unknown";
}
