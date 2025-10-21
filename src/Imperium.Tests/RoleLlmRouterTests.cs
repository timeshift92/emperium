using Xunit;
using Imperium.Llm;

namespace Imperium.Tests
{
    public class RoleLlmRouterTests
    {
        [Fact]
        public void Router_Implements_ILlmClient()
        {
            Assert.True(typeof(ILlmClient).IsAssignableFrom(typeof(RoleLlmRouter)));
        }
    }
}
