using Imperium.Domain.Agents;
using Imperium.Infrastructure;
using Imperium.Domain.Models;
using Imperium.Domain.Utils;
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
    private readonly Imperium.Api.NpcReactionOptions _reactionOptions;

    public NpcAgent(ILogger<NpcAgent>? logger = null, Microsoft.Extensions.Options.IOptions<Imperium.Api.NpcReactionOptions>? reactionOptions = null)
    {
        _logger = logger;
        _reactionOptions = reactionOptions?.Value ?? new Imperium.Api.NpcReactionOptions();
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

        var dispatcher = scope.GetRequiredService<Imperium.Domain.Services.IEventDispatcher>();

        // 🧮 выбрать до 5 случайных NPC напрямую из БД
        var chars = await db.Characters
            .OrderBy(c => EF.Functions.Random())
            .Take(5)
            .ToListAsync();

        if (chars.Count == 0) return;

        var events = new List<GameEvent>();

        var forbidden = new[]
        {
            "2025","2024","2023","интернет","сервер","компьютер","смартфон","телефон","электрон","email",
            "почта","разработчик","программист","API","код","GitHub","github","stackoverflow","stack overflow"
        };

        // For proximity-based communication: NPC talks only if co-located with a chosen peer; otherwise, moves towards peer
        foreach (var ch in chars)
        {
            if (ct.IsCancellationRequested) break;
            // pick a random peer
            var peer = await db.Characters.OrderBy(c => EF.Functions.Random()).FirstOrDefaultAsync(ct);
            if (peer != null && peer.Id != ch.Id)
            {
                var sameLoc = (!string.IsNullOrWhiteSpace(ch.LocationName) && ch.LocationName == peer.LocationName);
                if (!sameLoc)
                {
                    // Move character to peer's location (instant for now) and emit npc_move; skip conversation this tick
                    ch.LocationId = peer.LocationId;
                    ch.LocationName = peer.LocationName;
                    await db.SaveChangesAsync(ct);
                    var moveEv = new GameEvent
                    {
                        Id = Guid.NewGuid(),
                        Timestamp = DateTime.UtcNow,
                        Type = "npc_move",
                        Location = ch.LocationName ?? "unknown",
                        PayloadJson = JsonSerializer.Serialize(new { characterId = ch.Id, to = ch.LocationName, peerId = peer.Id })
                    };
                    events.Add(moveEv);
                    continue;
                }
            }

            string archetype = InferArchetype(ch.SkillsJson);

            try
            {
                var replyQueue = scope.GetRequiredService<Imperium.Api.Services.INpcReplyQueue>();
                var metrics = scope.GetService<Imperium.Api.MetricsService>();
                await replyQueue.EnqueueAsync(new Imperium.Api.Services.NpcReplyRequest(ch.Id, archetype, ct));
                metrics?.Add("npc.enqueued", 1);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "NpcAgent: failed to enqueue npc reply for {Id}", ch.Id);
            }
        }

        // Enqueue events for background persistence and publishing
        if (events.Count > 0)
        {
            foreach (var ev in events)
            {
                _ = dispatcher.EnqueueAsync(ev);
            }
        }

        // --- New: реагируем на попытки возврата владений ---
        try
        {
            var recentThreshold = DateTime.UtcNow.AddMinutes(-5);
                var reclaimAttempts = await db.GameEvents
                .Where(e => e.Type == "ownership_reclaim_attempt" && e.Timestamp >= recentThreshold)
                .OrderByDescending(e => e.Timestamp)
                .Take(20)
                .ToListAsync();

            if (reclaimAttempts.Count > 0)
            {
                var npcMemories = await db.NpcMemories.ToListAsync();
                var rnd = Random.Shared;
                var reactions = new List<GameEvent>();

                foreach (var a in reclaimAttempts)
                {
                    // try to extract assetId and characterId from payload
                    Guid? assetId = null;
                    Guid? claimant = null;
                    try
                    {
                        using var doc = JsonDocument.Parse(a.PayloadJson);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("assetId", out var aid) && aid.ValueKind == JsonValueKind.String)
                        {
                            Guid.TryParse(aid.GetString(), out var g);
                            assetId = g == Guid.Empty ? null : g;
                        }
                        if (root.TryGetProperty("characterId", out var cid) && cid.ValueKind == JsonValueKind.String)
                        {
                            Guid.TryParse(cid.GetString(), out var cg);
                            claimant = cg == Guid.Empty ? null : cg;
                        }
                    }
                    catch { }

                    // choose candidates: NPCs who remember the asset, or random nearby NPCs
                    var candidates = new List<Guid>();
                    if (assetId.HasValue)
                    {
                        candidates.AddRange(npcMemories.Where(m => m.KnownAssets.Contains(assetId.Value)).Select(m => m.CharacterId));
                        candidates.AddRange(npcMemories.Where(m => m.LostAssets.Contains(assetId.Value)).Select(m => m.CharacterId));
                    }

                    if (!candidates.Any())
                    {
                        // pick 1-3 random chars from DB (prefer same location)
                        var nearby = await db.Characters.Where(c => c.LocationName == a.Location).OrderBy(c => EF.Functions.Random()).Take(3).Select(c => c.Id).ToListAsync();
                        if (nearby.Count == 0)
                            nearby = await db.Characters.OrderBy(c => EF.Functions.Random()).Take(3).Select(c => c.Id).ToListAsync();
                        candidates.AddRange(nearby);
                    }

                    candidates = candidates.Distinct().ToList();

                    // resolve current owner of the asset if present
                    Guid? currentOwner = null;
                    if (assetId.HasValue)
                    {
                        try
                        {
                            var own = await db.Ownerships.AsNoTracking().FirstOrDefaultAsync(o => o.AssetId == assetId.Value);
                            if (own != null && own.OwnerId != Guid.Empty) currentOwner = own.OwnerId;
                        }
                        catch { }
                    }

                    // Prefetch relationships between candidates and claimant/owner to weight reactions
                    var counterpartIds = new List<Guid>();
                    if (claimant.HasValue) counterpartIds.Add(claimant.Value);
                    if (currentOwner.HasValue) counterpartIds.Add(currentOwner.Value);

                    List<Imperium.Domain.Models.Relationship> rels = new();
                    if (counterpartIds.Count > 0 && candidates.Count > 0)
                    {
                        try
                        {
                            rels = await db.Relationships.AsNoTracking()
                                .Where(r => (counterpartIds.Contains(r.TargetId) && candidates.Contains(r.SourceId))
                                         || (counterpartIds.Contains(r.SourceId) && candidates.Contains(r.TargetId)))
                                .ToListAsync();
                        }
                        catch { }
                    }

                    double GetRelScore(Guid candidateId, Guid other)
                    {
                        var r = rels.FirstOrDefault(x => (x.SourceId == candidateId && x.TargetId == other) || (x.TargetId == candidateId && x.SourceId == other));
                        if (r == null) return 0d;
                        // normalize combined sentiment into [-1..1]
                        var raw = (r.Trust + r.Love - r.Hostility) / 300.0;
                        return Math.Max(-1d, Math.Min(1d, raw));
                    }

                    foreach (var cid in candidates)
                    {
                        // small chance that NPC will react (based on memory/attachment)
                        var mem = npcMemories.FirstOrDefault(m => m.CharacterId == cid);
                        // baseline probability + weights from options
                        double baseChance = _reactionOptions.BaseProbability;
                        if (mem != null)
                        {
                            // attachment and greed influence: more attachment -> more likely to engage; greed -> self-interested actions
                            baseChance += Math.Clamp(mem.Attachment, 0, 1) * _reactionOptions.AttachmentWeight;
                            baseChance += Math.Clamp(mem.Greed, 0, 1) * _reactionOptions.GreedWeight;
                        }

                        // Relationship modifiers
                        double relClaim = 0, relOwner = 0;
                        if (claimant.HasValue) relClaim = GetRelScore(cid, claimant.Value);
                        if (currentOwner.HasValue) relOwner = GetRelScore(cid, currentOwner.Value);

                        // positive affinity to claimant encourages reaction; affinity to owner discourages
                        baseChance += Math.Max(0, relClaim) * _reactionOptions.RelClaimantWeight;
                        baseChance -= Math.Max(0, relOwner) * _reactionOptions.RelOwnerWeight;

                        var reactRoll = rnd.NextDouble();
                        if (reactRoll < Math.Min(_reactionOptions.MaxProbability, Math.Max(0.0, baseChance)))
                        {
                            string action;
                            bool hadPersonalLoss = mem != null && assetId.HasValue && mem.LostAssets.Contains(assetId.Value);
                            if (hadPersonalLoss)
                            {
                                action = "attempt_reclaim"; // strong self-interest
                            }
                            else
                            {
                                var tilt = relClaim - relOwner;
                                var p = rnd.NextDouble();
                                if (tilt > 0.2)
                                {
                                    action = (p < 0.6) ? "support_claimant" : "observe";
                                }
                                else if (tilt < -0.2)
                                {
                                    // negative tilt -> oppose claimant (may be ignored by downstream, but useful for telemetry)
                                    action = (p < 0.5) ? "oppose_claimant" : "observe";
                                }
                                else
                                {
                                    // neutral -> mostly observe with a chance to support
                                    action = (p < 0.3) ? "support_claimant" : "observe";
                                }
                            }

                            var payloadObj = new Dictionary<string, object?>
                            {
                                ["characterId"] = cid,
                                ["targetAssetId"] = assetId,
                                ["sourceEvent"] = a.Id,
                                ["action"] = action,
                                ["timestamp"] = DateTime.UtcNow,
                                ["relClaimant"] = claimant,
                                ["relOwner"] = currentOwner
                            };

                            var ev = new GameEvent
                            {
                                Id = Guid.NewGuid(),
                                Timestamp = DateTime.UtcNow,
                                Type = "npc_reaction",
                                Location = a.Location,
                                PayloadJson = JsonSerializer.Serialize(payloadObj)
                            };
                            reactions.Add(ev);
                        }
                    }
                }

                if (reactions.Count > 0)
                {
                    foreach (var r in reactions)
                    {
                        _ = dispatcher.EnqueueAsync(r);

                        // If this reaction is an attempt_reclaim, also enqueue an ownership_reclaim_attempt
                        try
                        {
                            using var doc = JsonDocument.Parse(r.PayloadJson);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("action", out var act) && act.ValueKind == JsonValueKind.String && act.GetString() == "attempt_reclaim")
                            {
                                Guid? targetAsset = null;
                                Guid? characterId = null;
                                if (root.TryGetProperty("targetAssetId", out var ta) && ta.ValueKind == JsonValueKind.String)
                                {
                                    if (Guid.TryParse(ta.GetString(), out var g)) targetAsset = g;
                                }
                                if (root.TryGetProperty("characterId", out var cid) && cid.ValueKind == JsonValueKind.String)
                                {
                                    if (Guid.TryParse(cid.GetString(), out var cg)) characterId = cg;
                                }

                                var claimEv = new GameEvent
                                {
                                    Id = Guid.NewGuid(),
                                    Timestamp = DateTime.UtcNow,
                                    Type = "ownership_reclaim_attempt",
                                    Location = r.Location,
                                    PayloadJson = JsonSerializer.Serialize(new { assetId = targetAsset, characterId = characterId, sourceNpcReaction = r.Id })
                                };
                                _ = dispatcher.EnqueueAsync(claimEv);
                            }
                        }
                        catch { }
                    }
                    var metricsSvc = scope.GetService<Imperium.Api.MetricsService>();
                    metricsSvc?.Add("npc.reactions", reactions.Count);
                    try
                    {
                        // breakdown by action
                        var support = reactions.Count(e => e.PayloadJson.Contains("support_claimant"));
                        var oppose = reactions.Count(e => e.PayloadJson.Contains("oppose_claimant"));
                        var attempt = reactions.Count(e => e.PayloadJson.Contains("attempt_reclaim"));
                        var observe = reactions.Count(e => e.PayloadJson.Contains("\"observe\""));
                        if (support > 0) metricsSvc?.Add("npc.reactions.support", support);
                        if (oppose > 0) metricsSvc?.Add("npc.reactions.oppose", oppose);
                        if (attempt > 0) metricsSvc?.Add("npc.reactions.attempt", attempt);
                        if (observe > 0) metricsSvc?.Add("npc.reactions.observe", observe);
                    }
                    catch { }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // graceful cancellation (application stopping or per-agent timeout)
            var log = scope.GetService<Microsoft.Extensions.Logging.ILogger<NpcAgent>>();
            log?.LogInformation("NpcAgent: reclaim processing canceled (shutdown or per-agent timeout)");
        }
        catch (Exception ex)
        {
            // don't break the tick loop on reaction errors
            var log = scope.GetService<Microsoft.Extensions.Logging.ILogger<NpcAgent>>();
            log?.LogWarning(ex, "NpcAgent: error while processing reclaim reactions");
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
    // Архетипный промпт для LLM (строго русская речь)
    private static string BuildPrompt(Character ch, string archetype)
    {
        var npcNameJson = JsonSerializer.Serialize(ch.Name);
        var skillsJson = ch.SkillsJson ?? "[]";
        var essence = ch.EssenceJson ?? "{}";
        var loc = string.IsNullOrWhiteSpace(ch.LocationName) ? "неизвестное место" : ch.LocationName;
        var history = string.IsNullOrWhiteSpace(ch.History) ? "" : ch.History;
        var normalizedGender = GenderHelper.Normalize(ch.Gender);
        var genderRu = normalizedGender switch
        {
            "female" => "женщина",
            "male" => "мужчина",
            _ => string.Empty
        };
        var toneHint = normalizedGender switch
        {
            "female" => "Персонаж женского пола: используй мягкие, заботливые обороты речи и женские окончания, если уместно.",
            "male" => "Персонаж мужского пола: допускаются более уверенные и решительные формулировки с мужскими окончаниями.",
            _ => "Пол не указан: придерживайся нейтрального тона без упора на гендер."
        };

        return $@"[role:Npc]
        Ты отвечаешь как житель античного Средиземноморья. Говори уверенно и только на русском языке, используя кириллицу. 
        Ты — {(string.IsNullOrWhiteSpace(genderRu) ? "" : genderRu + ", ")}{archetype} по характеру. 
        {toneHint}
        Не упоминай современные технологии, нейросети или английские слова. Верни JSON без лишних полей: {{""reply"": string, ""moodDelta"": int (опционально)}}. 
        Поле reply должно содержать 2–3 короткие фразы (до 40 слов) и оставаться в рамках эпохи. 
        Контекст: имя {npcNameJson}, возраст {ch.Age}, статус {ch.Status ?? "неизвестно"}, локация {loc}, навыки {skillsJson}, сущность {essence}, история {history}, текущая дата {DateTime.UtcNow:O}.";
    }

    private static string ReaskPrompt() =>
        "[role:Npc]\nОтвет верни в JSON {\"reply\": string, \"moodDelta\": int}. Пиши строго по-русски кириллицей и не вставляй английских слов.";
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
        s = Regex.Replace(s, "[A-Za-z]", "", RegexOptions.Compiled);
        s = Regex.Replace(s, "\\s+", " ", RegexOptions.Compiled).Trim();

        if (string.IsNullOrWhiteSpace(s))
        {
            s = "Я говорю на языке Империи и обсуждаю дела нашего времени.";
        }

        return s;
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
        return "[role:Npc]\nПерепиши ответ, сохранив смысл и атмосферу эпохи, используя только русский язык (кириллица). Верни JSON {\"reply\": string, \"moodDelta\": int}.\nИсходный ответ:\n" + reply;
    }





}








