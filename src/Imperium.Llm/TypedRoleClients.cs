namespace Imperium.Llm;

public class WorldLlmClient : RoleLlmClient
{
    public WorldLlmClient(HttpClient http) : base(http, "mistral") { }
}

public class EconomyLlmClient : RoleLlmClient
{
    public EconomyLlmClient(HttpClient http) : base(http, "llama3:8b") { }
}

public class NpcLlmClient : RoleLlmClient
{
    public NpcLlmClient(HttpClient http) : base(http, "phi3:medium") { }
}
