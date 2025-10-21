using System;
using System.Linq;
using Imperium.Domain.Models;
using Imperium.Domain.Utils;

namespace Imperium.Api.Extensions;

public static class CharacterQueryExtensions
{
    public static IQueryable<Character> FilterByGender(this IQueryable<Character> query, string? gender)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));

        if (string.IsNullOrWhiteSpace(gender)) return query;

        var trimmed = gender.Trim();
        if (trimmed.Equals("any", StringComparison.OrdinalIgnoreCase)) return query;

        var normalized = GenderHelper.Normalize(trimmed);
        if (normalized == null) return query;

        return query.Where(c => c.Gender != null && c.Gender != "" && c.Gender!.ToLower() == normalized);
    }
}
