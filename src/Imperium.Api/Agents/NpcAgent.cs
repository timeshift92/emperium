using Imperium.Domain.Agents;
using Imperium.Infrastructure;
using Imperium.Domain.Models;
using Imperium.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Imperium.Api.Agents;

/// <summary>
/// Агент NpcAI: генерирует короткие реплики для случайных NPC.
/// Ответы строго в JSON: { "reply": string, "moodDelta"?: int }.
/// Реплики — бытовые, эпохальные, без современных терминов.
/// </summary>
public class NpcAgent : IWorldAgent
{
    public string Name => "NpcAI";
    private readonly ILogger<NpcAgent>? _logger;

    public NpcAgent(ILogger<NpcAgent>? logger = null)
    {
        _logger = logger;
    }

    // Попытаться распарсить строку JSON и вернуть JsonElement (через object), иначе вернуть исходную строку
    private static object? TryParseJsonOrRaw(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText())!;
        }
        catch
        {
            return json;
        }
    }

    // Вспомогательный метод: вызов LLM с таймаутом и безопасной обработкой отмены
    private static async Task<string?> CallLlmWithTimeoutAsync(ILlmClient llm, string prompt, CancellationToken outerCt)
    {
    // Таймаут на каждый LLM-вызов — 8 секунд (потому что мы хотим, чтобы тик не висел долго)
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
    cts.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            return await llm.SendPromptAsync(prompt, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // не пробрасываем дальше — логируется в вызывающем коде
            return null;
        }
        catch (Exception)
        {
            // swallow to avoid breaking tick loop; caller logs details
            return null;
        }
    }

    public async Task TickAsync(IServiceProvider scope, CancellationToken ct)
    {
        var db = scope.GetRequiredService<ImperiumDbContext>();
        var llm = scope.GetRequiredService<ILlmClient>();
        var stream = scope.GetRequiredService<Imperium.Api.EventStreamService>();

        // 🧮 выбрать до 5 случайных NPC напрямую из БД
        var chars = await db.Characters
            .OrderBy(c => EF.Functions.Random())
            .Take(5)
            .ToListAsync(ct);

        if (chars.Count == 0) return;

        var events = new List<GameEvent>();

        var forbidden = new[]
        {
            "2025","2024","2023","интернет","сервер","компьютер","смартфон","телефон","электрон","email",
            "почта","разработчик","программист","API","код","GitHub","github","stackoverflow","stack overflow"
        };

        foreach (var ch in chars)
        {
            if (ct.IsCancellationRequested) break;

            string archetype = InferArchetype(ch.SkillsJson);
            string prompt = BuildPrompt(ch, archetype);

            try
            {
                var metrics = scope.GetService<Imperium.Api.MetricsService>();
                string? raw = null;
                // Меньше попыток — быстрее тики в dev-режиме
                const int maxAttempts = 2;
                int reasksPerformed = 0;
                int sanitizations = 0;

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    // Первая попытка — основной промпт, остальные — уточнения
                    if (attempt == 1)
                        raw = await CallLlmWithTimeoutAsync(llm, prompt, ct);
                    else
                    {
                        reasksPerformed++;
                        raw = await CallLlmWithTimeoutAsync(llm, ReaskPrompt(), ct);
                    }

                    if (string.IsNullOrWhiteSpace(raw))
                        continue;

                    // Если в ответе латиница (англ. буквы) — чаще всего модель утекла в технический режим -> reask
                    if (IsSignificantLatinOrTechnical(raw) && attempt < maxAttempts)
                    {
                        _logger?.LogInformation("NpcAgent: latin detected in raw reply for {Character} — reasking", ch.Name);
                        continue;
                    }

                    // Проверка на современные слова
                    if (HasForbiddenTokens(raw, forbidden) && attempt < maxAttempts)
                    {
                        _logger?.LogInformation("NpcAgent: forbidden tokens detected in raw reply for {Character} — reasking", ch.Name);
                        continue;
                    }

                    // Попытка распарсить JSON
                    if (TryParseNpcReply(raw, out var reply, out var mood))
                    {
                        // Если в reply есть латиница или запрещённые токены на последней попытке — попробуем rewrite
                        if ((IsSignificantLatinOrTechnical(reply) || HasForbiddenTokens(reply, forbidden)) && attempt < maxAttempts)
                        {
                            _logger?.LogInformation("NpcAgent: reply contains latin/forbidden on attempt {Attempt} for {Character}", attempt, ch.Name);
                            continue;
                        }

                        if ((IsSignificantLatinOrTechnical(reply) || HasForbiddenTokens(reply, forbidden)) && attempt == maxAttempts)
                        {
                            // финальная попытка: просим LLM переформулировать reply как архаичный русский
                            try
                            {
                                var rewritePrompt = BuildRewritePrompt(reply);
                                var rewritten = await CallLlmWithTimeoutAsync(llm, rewritePrompt, ct);
                                if (!string.IsNullOrWhiteSpace(rewritten) && TryParseNpcReply(rewritten, out var newReply, out var newMood))
                                {
                                    reply = newReply;
                                    mood = newMood ?? mood; // prefer rewritten mood if provided
                                    sanitizations++;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogWarning(ex, "NpcAgent: rewrite failed for {Character}", ch.Name);
                            }
                        }

                        if (HasForbiddenTokens(reply, forbidden))
                        {
                            reply = SanitizeReply(reply, forbidden);
                            sanitizations++;
                        }

                        if (reply.Length > 350)
                            reply = reply[..350];

                        var ev = new GameEvent
                        {
                            Id = Guid.NewGuid(),
                            Timestamp = DateTime.UtcNow,
                            Type = "npc_reply",
                            Location = ch.LocationName ?? "unknown",
                            PayloadJson = JsonSerializer.Serialize(new
                            {
                                characterId = ch.Id,
                                name = ch.Name,
                                archetype,
                                location = ch.LocationName,
                                // Try to include structured JSON for essence/skills if possible
                                essence = TryParseJsonOrRaw(ch.EssenceJson),
                                skills = TryParseJsonOrRaw(ch.SkillsJson),
                                history = ch.History,
                                reply,
                                moodDelta = mood,
                                meta = new { reasksPerformed, sanitizations }
                            })
                        };
                        events.Add(ev);
                        break;
                    }
                }
                if (reasksPerformed > 0)
                {
                    _logger?.LogInformation("NpcAgent: reasks={Reasks} sanitizations={Sanitizations} for {Character}", reasksPerformed, sanitizations, ch.Name);
                    metrics?.Add("npc.reasks", reasksPerformed);
                }
                if (sanitizations > 0)
                {
                    metrics?.Add("npc.sanitizations", sanitizations);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "NpcAgent: ошибка при обработке NPC {Id}", ch.Id);
            }
        }

        // Enqueue events for background persistence and publishing
        if (events.Count > 0)
        {
            var dispatcher = scope.GetRequiredService<Imperium.Domain.Services.IEventDispatcher>();
            foreach (var ev in events)
            {
                _ = dispatcher.EnqueueAsync(ev);
            }
        }
    }

    // 🧠 Определение архетипа NPC
    private static string InferArchetype(string? skillsJson)
    {
        if (string.IsNullOrWhiteSpace(skillsJson)) return "крестьянин";
        var s = skillsJson.ToLowerInvariant();
        return s switch
        {
            var x when x.Contains("ремес") || x.Contains("кузн") => "ремесленник",
            var x when x.Contains("купец") || x.Contains("торг") => "торговец",
            var x when x.Contains("солдат") || x.Contains("вояк") => "солдат",
            var x when x.Contains("жрец") || x.Contains("монах") || x.Contains("свящ") => "жрец",
            _ => "крестьянин"
        };
    }

    // 🧾 Промпт
    private static string BuildPrompt(Character ch, string archetype)
    {
        var npcNameJson = JsonSerializer.Serialize(ch.Name);
        var skillsJson = ch.SkillsJson ?? "[]";
        var essence = ch.EssenceJson ?? "{}";
        var loc = string.IsNullOrWhiteSpace(ch.LocationName) ? "неизвестно" : ch.LocationName;
        var history = string.IsNullOrWhiteSpace(ch.History) ? "" : ch.History;
        return
            "[role:Npc]\n отвечать надо строго на русском языке" +
            $"Вы — персонаж античного мира ({archetype}). " +
            "Говорите как житель эпохи: бытовой, архаичный стиль, упоминания богов, урожая, дорог, налогов. " +
            "НИ В КОЕМ СЛУЧАЕ не используйте современные термины (интернет, компьютер, сервер, API, GitHub и т.п.). " +
            "Ответьте ТОЛЬКО компактным JSON: {\"reply\": string, \"moodDelta\": int (опционально)}. " +
            "reply — максимум ~35 слов; moodDelta — опционально.\n" +
            $"Контекст: персонаж {npcNameJson}, возраст: {ch.Age}, статус: {ch.Status ?? "unknown"}, место: {loc}, навыки: {skillsJson}, характеристики: {essence}, краткая история: {history}, время: {DateTime.UtcNow:O}.";
    }

    // 🔁 Повторный промпт при ошибке
    private static string ReaskPrompt() =>
        "[role:Npc]\nПовторите ответ строго в формате JSON. Без современных слов, дат и технических терминов.";

    // 🚫 Проверка на запрещённые слова
    private static bool HasForbiddenTokens(string text, string[] forbidden)
    {
        var lower = text.ToLowerInvariant();
        return Regex.IsMatch(lower, @"\b(" + string.Join("|", forbidden.Select(Regex.Escape)) + @")\b");
    }

    // 📦 Парсинг JSON
    private static bool TryParseNpcReply(string input, out string reply, out int? mood)
    {
        reply = string.Empty;
        mood = null;
        try
        {
            using var doc = JsonDocument.Parse(input);
            var root = doc.RootElement;
            if (root.TryGetProperty("reply", out var r))
                reply = r.GetString() ?? string.Empty;
            if (root.TryGetProperty("moodDelta", out var m) && m.ValueKind == JsonValueKind.Number)
                mood = m.GetInt32();
            return !string.IsNullOrWhiteSpace(reply);
        }
        catch
        {
            return false;
        }
    }

    // 🧹 Очистка реплики
    private static string SanitizeReply(string input, string[] forbidden)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var s = Regex.Replace(input, "\\b(19|20)\\d{2}\\b", "", RegexOptions.Compiled);
        foreach (var f in forbidden.OrderByDescending(x => x.Length))
            s = Regex.Replace(s, Regex.Escape(f), "", RegexOptions.IgnoreCase);
        return Regex.Replace(s, "\\s+", " ", RegexOptions.Compiled).Trim();
    }

    // Проверка: содержит ли ответ заметное количество латиницы или явные технические/метаданные
    // Ранее простая проверка по наличию любых латинских букв давала много ложных срабатываний
    // (например, короткие имена, даты или единицы). Здесь используем долю латинских букв от всех
    // букв и дополнительные шаблоны (url, email, блоки кода, служебные слова) чтобы принимать
    // решение о необходимости reask только в явных случаях.
    private static bool IsSignificantLatinOrTechnical(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        // посчитаем латинские и все буквы (латиница + кириллица)
        var latinMatches = Regex.Matches(input, "[A-Za-z]", RegexOptions.Compiled);
        var letterMatches = Regex.Matches(input, "[A-Za-zА-Яа-я]", RegexOptions.Compiled);
        int latin = latinMatches.Count;
        int letters = Math.Max(1, letterMatches.Count); // защититься от деления на 0

        double ratio = (double)latin / letters;

        // Если более 25% букв — подозрительно
        if (ratio > 0.25) return true;

        var lower = input.ToLowerInvariant();

        // Явные технические сигнатуры, URL/email, блоки кода, служебные метки
        if (lower.Contains("http") || lower.Contains("www.") || lower.Contains("@") || lower.Contains("mailto:")
            || lower.Contains("```") || lower.Contains("<code>") || lower.Contains("model:") || lower.Contains("temperature")
            || lower.Contains("tokens") || lower.Contains("usage") || lower.Contains("stop")
            || Regex.IsMatch(input, @"\b(api|github|stack overflow|stackoverflow|computer|server|internet)\b", RegexOptions.IgnoreCase))
            return true;

        return false;
    }

    // Пост-промпт: перепиши reply в архаичный русский, верни JSON
    private static string BuildRewritePrompt(string reply)
    {
        // Мы передаём текущий reply в поле original, просим вернуть JSON с полем reply исправленным
        return "[role:Npc]\nПерепишите это поле reply как устойчивое, архаичное русское высказывание, уберите любые современные слова, латиницу и годы.\nВХОД (только текст):\n" + reply + "\n\nВЕРНИТЕ ТОЛЬКО JSON: {\"reply\": string, \"moodDelta\": int (опционально)}";
    }
}
