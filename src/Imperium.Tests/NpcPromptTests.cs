using Imperium.Api.Services;
using Imperium.Domain.Models;
using Xunit;

namespace Imperium.Tests;

public class NpcPromptTests
{
    [Fact]
    public void BuildPrompt_Female_ToneHintMentioned()
    {
        var character = new Character
        {
            Id = Guid.NewGuid(),
            Name = "Aurelia",
            Age = 28,
            Status = "idle",
            Gender = "female"
        };

        var prompt = NpcUtils.BuildPrompt(character, "ремесленник");

        Assert.Contains("женского пола", prompt);
        Assert.Contains("женские окончания", prompt);
    }

    [Fact]
    public void BuildPrompt_Male_ToneHintMentioned()
    {
        var character = new Character
        {
            Id = Guid.NewGuid(),
            Name = "Cassius",
            Age = 32,
            Status = "idle",
            Gender = "male"
        };

        var prompt = NpcUtils.BuildPrompt(character, "солдат");

        Assert.Contains("мужского пола", prompt);
        Assert.Contains("решительные формулировки", prompt);
    }

    [Fact]
    public void BuildPrompt_UnknownGender_NeutralTone()
    {
        var character = new Character
        {
            Id = Guid.NewGuid(),
            Name = "Patricius",
            Age = 40,
            Status = "idle",
            Gender = null
        };

        var prompt = NpcUtils.BuildPrompt(character, "торговец");

        Assert.Contains("Пол персонажа: не указан", prompt);
        Assert.Contains("нейтрального тона", prompt);
    }
}
