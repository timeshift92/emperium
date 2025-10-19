using System.Text.Json;

namespace Imperium.Llm;

public class MockLlmClient : ILlmClient
{
    public Task<string> SendPromptAsync(string prompt, CancellationToken ct = default)
    {
        // Очень простой мок: если в промпте есть слово weather — вернуть JSON по контракту
        if (prompt?.ToLowerInvariant().Contains("weather") ?? false)
        {
            var obj = new
            {
                condition = "sunny",
                temperatureC = 25,
                windKph = 10,
                precipitationMm = 0.0
            };
            return Task.FromResult(JsonSerializer.Serialize(obj));
        }
        // If prompt asks for short npc reply, simulate a small JSON reply
        if (prompt?.ToLowerInvariant().Contains("npc") == true || prompt?.ToLowerInvariant().Contains("персонаж") == true)
        {
            var simple = new { reply = "Пойду за хлебом.", moodDelta = 0 };
            return Task.FromResult(JsonSerializer.Serialize(simple));
        }

        return Task.FromResult("{}");
    }
}
