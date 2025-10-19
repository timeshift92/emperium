namespace Imperium.Llm;

public interface ILlmClient
{
    /// <summary>
    /// Отправляет промпт LLM и получает текстовый ответ (ожидается JSON).
    /// </summary>
    Task<string> SendPromptAsync(string prompt, CancellationToken ct = default);
}
