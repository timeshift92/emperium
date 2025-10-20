namespace Imperium.Llm;

public interface IFallbackLlmProvider
{
    /// <summary>
    /// Return a fallback ILlmClient to use when primary providers fail.
    /// May return null if no fallback is available.
    /// </summary>
    ILlmClient? GetFallback();
}
