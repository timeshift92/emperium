using System.Collections.Generic;
using Imperium.Api.Extensions;
using Imperium.Domain.Models;
using Xunit;

namespace Imperium.Tests;

public class CharactersFilterTests
{
    [Fact]
    public void FilterByGender_Female_ReturnsOnlyFemale()
    {
        var characters = new List<Character>
        {
            new Character { Name = "A", Gender = "female" },
            new Character { Name = "B", Gender = "male" },
            new Character { Name = "C", Gender = "female" },
            new Character { Name = "D", Gender = null }
        };

        var filtered = characters.AsQueryable().FilterByGender("female").ToList();

        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, c => Assert.Equal("female", c.Gender));
    }

    [Fact]
    public void FilterByGender_Any_KeepsAll()
    {
        var characters = new List<Character>
        {
            new Character { Name = "A", Gender = "female" },
            new Character { Name = "B", Gender = "male" },
        };

        var filtered = characters.AsQueryable().FilterByGender("any").ToList();

        Assert.Equal(characters.Count, filtered.Count);
    }

    [Fact]
    public void FilterByGender_InvalidValue_DoesNothing()
    {
        var characters = new List<Character>
        {
            new Character { Name = "A", Gender = "female" },
            new Character { Name = "B", Gender = "male" },
        };

        var filtered = characters.AsQueryable().FilterByGender("unknown").ToList();

        Assert.Equal(characters.Count, filtered.Count);
    }
}
