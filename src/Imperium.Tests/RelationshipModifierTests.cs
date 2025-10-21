using System.Collections.Generic;
using System.Reflection;
using Imperium.Api;
using Imperium.Api.Agents;
using Imperium.Domain.Models;
using Xunit;

namespace Imperium.Tests;

public class RelationshipModifierTests
{
    private static MethodInfo ResolveMethod => typeof(RelationshipAgent)
        .GetMethod("ResolveGenderBias", BindingFlags.NonPublic | BindingFlags.Static)!
        ?? throw new InvalidOperationException("ResolveGenderBias not found");

    private static MethodInfo ConsumeMethod => typeof(RelationshipAgent)
        .GetMethod("ConsumeGenderBias", BindingFlags.NonPublic | BindingFlags.Static)!
        ?? throw new InvalidOperationException("ConsumeGenderBias not found");

    private static FieldInfo ResidualsField => typeof(RelationshipAgent)
        .GetField("GenderBiasResiduals", BindingFlags.NonPublic | BindingFlags.Static)!
        ?? throw new InvalidOperationException("Residuals dictionary not found");

    private static void ResetResiduals()
    {
        var store = ResidualsField.GetValue(null) as IDictionary<(Guid, Guid), double>;
        store?.Clear();
    }

    [Fact]
    public void ResolveGenderBias_UsesConfiguration()
    {
        ResetResiduals();
        var options = new RelationshipModifierOptions
        {
            GenderBias = new Dictionary<string, double>
            {
                ["male->female"] = 0.25,
                ["female->male"] = -0.1,
                ["same"] = 0.05
            }
        };

        var male = new Character { Gender = "male" };
        var female = new Character { Gender = "female" };

        var biasMf = (double)ResolveMethod.Invoke(null, new object?[] { male, female, options })!;
        var biasFm = (double)ResolveMethod.Invoke(null, new object?[] { female, male, options })!;
        var biasSame = (double)ResolveMethod.Invoke(null, new object?[] { male, male, options })!;

        Assert.Equal(0.25, biasMf, 3);
        Assert.Equal(-0.1, biasFm, 3);
        Assert.Equal(0.05, biasSame, 3);
    }

    [Fact]
    public void ConsumeGenderBias_AccumulatesResiduals()
    {
        ResetResiduals();
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();

        var delta1 = (int)ConsumeMethod.Invoke(null, new object?[] { source, target, 0.4 })!;
        var delta2 = (int)ConsumeMethod.Invoke(null, new object?[] { source, target, 0.4 })!;
        var delta3 = (int)ConsumeMethod.Invoke(null, new object?[] { source, target, 0.4 })!;

        Assert.Equal(0, delta1);
        Assert.Equal(0, delta2);
        Assert.Equal(1, delta3);
    }
}
