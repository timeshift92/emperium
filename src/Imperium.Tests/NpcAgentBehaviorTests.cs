using System;
using System.Reflection;
using Xunit;

namespace Imperium.Tests
{
    public class NpcAgentBehaviorTests
    {
        private static MethodInfo GetPrivateStatic(string name)
        {
            var t = typeof(Imperium.Api.Agents.NpcAgent);
            var m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null) throw new InvalidOperationException($"Method {name} not found");
            return m;
        }

        [Fact]
        public void TryParseNpcReply_ParsesValidJson()
        {
            var m = GetPrivateStatic("TryParseNpcReply");
            var parameters = new object?[] { "{\"reply\":\"Привет\", \"moodDelta\": 2}", null, null };
            var result = (bool)m.Invoke(null, parameters)!;
            Assert.True(result);
            Assert.Equal("Привет", parameters[1]);
            Assert.Equal(2, parameters[2]);
        }

        [Fact]
        public void SanitizeReply_RemovesForbiddenAndLatin()
        {
            var m = GetPrivateStatic("SanitizeReply");
            var forbidden = new[] { "internet", "github" };
            var input = "Это ответ с github и letters ABC and 2025";
            var res = (string)m.Invoke(null, new object?[] { input, forbidden })!;
            Assert.DoesNotContain("github", res, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch("[A-Za-z]", res);
        }

        [Fact]
        public void IsSignificantLatinOrTechnical_DetectsLatin()
        {
            var m = GetPrivateStatic("IsSignificantLatinOrTechnical");
            var input = "hello мир this is a test";
            var res = (bool)m.Invoke(null, new object?[] { input })!;
            Assert.True(res);
        }
    }
}
