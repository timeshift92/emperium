using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Imperium.Domain.Models;

public class WorldTime
{
    public Guid Id { get; set; }
    // absolute tick counter (1 tick = 30s)
    public long Tick { get; set; }
    public int Hour { get; set; }
    public int Day { get; set; }
    /// <summary>Месяц (1..12) — вычисляемое поле, не сохраняется в БД.</summary>
    [NotMapped]
    public int Month { get; set; }
    /// <summary>День месяца (1..N) — вычисляемое поле, не сохраняется в БД.</summary>
    [NotMapped]
    public int DayOfMonth { get; set; }
    public int Year { get; set; }
    public bool IsDaytime { get; set; }
    public DateTime LastUpdated { get; set; }
}
