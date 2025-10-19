using System;

namespace Imperium.Domain.Models;

public class WorldTime
{
    public Guid Id { get; set; }
    // absolute tick counter (1 tick = 30s)
    public long Tick { get; set; }
    public int Hour { get; set; }
    public int Day { get; set; }
    public int Year { get; set; }
    public bool IsDaytime { get; set; }
    public DateTime LastUpdated { get; set; }
}
