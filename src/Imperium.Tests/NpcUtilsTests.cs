using System;
using Xunit;
using Imperium.Api.Services;

namespace Imperium.Tests
{
    public class NpcUtilsTests
    {
        [Fact]
        public void TryParseNpcReply_ParsesJsonReply()
        {
            string json = "{\"reply\": \"Привет\", \"moodDelta\": 1}";
            bool ok = NpcUtils.TryParseNpcReply(json, out var reply, out var mood);
            Assert.True(ok);
            Assert.Equal("Привет", reply);
            Assert.Equal(1, mood);
        }

        [Fact]
        public void SanitizeReply_RemovesForbiddenAndLatin()
        {
            var forbidden = new[] { "интернет", "github" };
            var raw = "Visit github or the интернет 2025 example ABC";
            var cleaned = NpcUtils.SanitizeReply(raw, forbidden);
            Assert.DoesNotContain("github", cleaned, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch("[A-Za-z]", cleaned);
        }

        [Fact]
        public void HasForbiddenTokens_FindsTokens()
        {
            var forbidden = new[] { "интернет", "github" };
            var text = "Мы говорим об интернете и торговле";
            Assert.True(NpcUtils.HasForbiddenTokens(text, forbidden));
        }
    }
}
