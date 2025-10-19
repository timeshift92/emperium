namespace Imperium.Llm;

public class LlmOptions
{
    public string Provider { get; set; } = "OpenAI"; // or "Ollama"
    public string Model { get; set; } = "gpt-4o-mini";
    public double Temperature { get; set; } = 0.2;
}
