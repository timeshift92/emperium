using System;
using System.Collections.Generic;

namespace Imperium.Domain.Models;

/// <summary>
/// Память NPC: известные и утраченные активы, мотивация
/// </summary>
public class NpcMemory
{
    public Guid Id { get; set; }
    public Guid CharacterId { get; set; }
    public List<Guid> KnownAssets { get; set; } = new();
    public List<Guid> LostAssets { get; set; } = new();
    public double Greed { get; set; } // Степень жадности (0..1)
    public double Attachment { get; set; } // Привязанность к активам (0..1)
    public DateTime LastUpdated { get; set; }
}
