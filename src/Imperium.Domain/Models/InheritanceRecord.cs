using System;

namespace Imperium.Domain.Models
{
    public class InheritanceRecord
    {
        public Guid Id { get; set; }
        // Character who died / whose estate is being processed
        public Guid DeceasedId { get; set; }
        // JSON array of heir character Ids
        public string HeirsJson { get; set; } = "[]";
        // JSON object describing rule or will, e.g. { "type": "equal_split" }
        public string RulesJson { get; set; } = "{}";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        // Optional resolution result stored as JSON
        public string? ResolutionJson { get; set; }
    }
}
