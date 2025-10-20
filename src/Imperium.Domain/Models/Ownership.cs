using System;

namespace Imperium.Domain.Models;

/// <summary>
/// Модель собственности: актив, владелец, уверенность, типы, социальное признание
/// </summary>
public class Ownership
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; } // Character, Faction, Household и др.
    public Guid AssetId { get; set; } // Актив: земля, дом, предмет, бизнес и т.д.
    public string OwnerType { get; set; } = string.Empty; // "Character", "Faction", "Household" ...
    public string AssetType { get; set; } = string.Empty; // "Land", "Building", "Item", "Business" ...
    public double Confidence { get; set; } // Уверенность во владении (0..1)
    public bool IsRecognized { get; set; } // Социальное признание
    public DateTime AcquiredAt { get; set; }
    public string AcquisitionType { get; set; } = string.Empty; // "purchase", "inheritance", "gift", "conquest", "creation", "confiscation"
}
