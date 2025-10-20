using System.Text.Json;
using System.Text.Json.Serialization;
using Imperium.Domain.Models;
using Imperium.Domain.Services;
using Imperium.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Imperium.Api.Services;

/// <summary>
/// Applies inheritance settlements transactionally:
/// - Reassigns Ownership records from the deceased to heirs according to rules
/// - Distributes household wealth
/// - Persists and enqueues GameEvents
/// </summary>
public class InheritanceService
{
    private readonly ImperiumDbContext _db;
    private readonly IEventDispatcher _dispatcher;
    private readonly Imperium.Api.Utils.IRandomProvider _random;
    private readonly Microsoft.Extensions.Options.IOptions<Imperium.Api.CurrencyOptions> _currencyOptions;
    private readonly Microsoft.Extensions.Options.IOptions<Imperium.Api.InheritanceOptions> _inheritanceOptions;

    public InheritanceService(ImperiumDbContext db, IEventDispatcher dispatcher, Imperium.Api.Utils.IRandomProvider random,
        Microsoft.Extensions.Options.IOptions<Imperium.Api.CurrencyOptions> currencyOptions,
        Microsoft.Extensions.Options.IOptions<Imperium.Api.InheritanceOptions> inheritanceOptions)
    {
        _db = db;
        _dispatcher = dispatcher;
        _random = random;
        _currencyOptions = currencyOptions;
        _inheritanceOptions = inheritanceOptions;
    }

    public async Task<InheritanceResult> ApplyInheritanceAsync(Guid inheritanceRecordId, CancellationToken ct = default)
    {
        var rec = await _db.InheritanceRecords.FirstOrDefaultAsync(r => r.Id == inheritanceRecordId, ct);
        if (rec == null) return InheritanceResult.NotFound(inheritanceRecordId);

        var heirs = ParseHeirs(rec.HeirsJson);
        var rule = ParseRule(rec.RulesJson, out var shares);

        if (heirs.Length == 0)
        {
            rec.ResolutionJson = JsonSerializer.Serialize(new { rule, heirs, applied = false, reason = "no_heirs" });
            await _db.SaveChangesAsync(ct);
            return InheritanceResult.NoHeirs(inheritanceRecordId);
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // Load assets owned by deceased (tracked)
            var assets = await _db.Ownerships
                .Where(o => o.OwnerId == rec.DeceasedId)
                .OrderBy(o => o.AssetId) // FIX: stable ordering for deterministic assignments
                .ToListAsync(ct);

            var transfers = new List<object>();
            var createdEvents = new List<GameEvent>();

            // --- Household wealth distribution ---
            // NB: если MemberIdsJson — обычная строка, Contains может не перевестись в SQL.
            // Делаем предикат в памяти (или замените на JSON-поиск вашего провайдера).
            var headHouseholds = await _db.Households
                .Where(h => h.HeadId == rec.DeceasedId)
                .ToListAsync(ct);

            var candidateMemberHouseholds = await _db.Households
                .Where(h => h.MemberIdsJson != null)
                .ToListAsync(ct);

            var memberHouseholds = candidateMemberHouseholds
                .Where(h => ContainsGuid(h.MemberIdsJson!, rec.DeceasedId))
                .ToList();

            var households = headHouseholds.Concat(memberHouseholds).DistinctBy(h => h.Id).ToList();

            // Currency precision from options
            var currencyDecimalPlaces = Math.Max(0, _currencyOptions?.Value?.DecimalPlaces ?? 2);
            var minimalUnitFactor = (long)Math.Pow(10, currencyDecimalPlaces);
            foreach (var hh in households)
            {
                if (hh.Wealth <= 0) continue;

                // convert to minimal integer currency units (e.g., cents) to avoid fractional rounding errors
                var totalDecimalUnits = decimal.Truncate(hh.Wealth * minimalUnitFactor);
                long totalUnits;
                try { totalUnits = (long)totalDecimalUnits; }
                catch (OverflowException) { totalUnits = (long)Math.Max(Math.Min((double)totalDecimalUnits, long.MaxValue), long.MinValue); }

                var perFloorUnits = totalUnits / heirs.Length;
                var remainderUnits = (int)(totalUnits - perFloorUnits * heirs.Length);

                var distributions = new List<object>();

                if (rule == InheritanceRule.Shares)
                {
                    // Use parsed shares from the outer ParseRule call (normalized for heirs)
                    var sharesForHeirs = NormalizeSharesForHeirs(heirs, shares);
                    if (sharesForHeirs.Count > 0)
                    {
                        var desired = new Dictionary<Guid, decimal>();
                        foreach (var hId in heirs) desired[hId] = sharesForHeirs.GetValueOrDefault(hId, 0m) * totalUnits;

                        var alloc = new Dictionary<Guid, long>();
                        var remaindersFrac = new List<(Guid id, decimal frac)>();
                        long allocated = 0;
                        foreach (var kv in desired)
                        {
                            var flo = (long)Math.Floor(kv.Value);
                            alloc[kv.Key] = flo;
                            allocated += flo;
                            remaindersFrac.Add((kv.Key, kv.Value - flo));
                        }

                        var remaining = (int)(totalUnits - allocated);
                        remaindersFrac = TieBreakOrder(remaindersFrac).ToList();
                        int ri2 = 0;
                        while (remaining > 0 && remaindersFrac.Count > 0)
                        {
                            var id = remaindersFrac[ri2 % remaindersFrac.Count].id;
                            alloc[id] = alloc.GetValueOrDefault(id) + 1;
                            remaining--;
                            ri2++;
                        }

                        for (int i = 0; i < heirs.Length; i++)
                        {
                            var hid = heirs[i];
                            var units = alloc.GetValueOrDefault(hid);
                            var amount = (decimal)units / minimalUnitFactor;
                            distributions.Add(new { heir = hid, amount });
                            var ev = new GameEvent
                            {
                                Id = Guid.NewGuid(),
                                Timestamp = DateTime.UtcNow,
                                Type = "inheritance_wealth_transfer",
                                Location = "household",
                                PayloadJson = JsonSerializer.Serialize(new
                                {
                                    inheritance = rec.Id,
                                    householdId = hh.Id,
                                    heirId = hid,
                                        amount = amount.ToString($"F{currencyDecimalPlaces}", System.Globalization.CultureInfo.InvariantCulture)
                                })
                            };
                            // Persist event as part of the transaction, then enqueue after commit
                            _db.GameEvents.Add(ev);
                            createdEvents.Add(ev);
                        }
                    }
                }

                // Equal split (default or fallback)
                if (distributions.Count == 0)
                {
                    var allocations = new long[heirs.Length];
                    for (int i = 0; i < heirs.Length; i++) allocations[i] = perFloorUnits;

                    if (remainderUnits > 0)
                    {
                        for (int r = 0; r < remainderUnits; r++)
                        {
                            var idx = _random.NextInt(heirs.Length);
                            allocations[idx]++;
                        }
                    }

                    for (int i = 0; i < heirs.Length; i++)
                    {
                        var units = allocations[i];
                        var amount = (decimal)units / minimalUnitFactor;
                        var heir = heirs[i];
                        var ev = new GameEvent
                        {
                            Id = Guid.NewGuid(),
                            Timestamp = DateTime.UtcNow,
                            Type = "inheritance_wealth_transfer",
                            Location = "household",
                            PayloadJson = JsonSerializer.Serialize(new
                            {
                                inheritance = rec.Id,
                                householdId = hh.Id,
                                heirId = heir,
                                    amount = amount.ToString($"F{currencyDecimalPlaces}", System.Globalization.CultureInfo.InvariantCulture)
                            })
                        };
                        _db.GameEvents.Add(ev);
                        createdEvents.Add(ev);
                        distributions.Add(new { heir, amount });
                    }
                }

                // clear household wealth atomically in DB
                hh.Wealth = 0m;
                transfers.Add(new { householdId = hh.Id, distributed = distributions });
            }
            // Prepare assetAssignments mapping
            var assetAssignments = new Dictionary<Guid, List<Ownership>>();
            foreach (var h in heirs) assetAssignments[h] = new List<Ownership>();

            // Assign assets to heirs according to the inheritance rule
            if (assets.Count > 0)
            {
                if (rule == InheritanceRule.Primogeniture)
                {
                    // All assets to the first (eldest) heir
                    var first = heirs[0];
                    assetAssignments[first].AddRange(assets);
                }
                else if (rule == InheritanceRule.Shares)
                {
                    var sharesForHeirs = NormalizeSharesForHeirs(heirs, shares);
                    if (sharesForHeirs.Count > 0)
                    {
                        // Largest remainder method to allocate integer counts of assets
                        var desired = new Dictionary<Guid, decimal>();
                        foreach (var h in heirs) desired[h] = sharesForHeirs.GetValueOrDefault(h, 0m) * assets.Count;

                        var alloc = new Dictionary<Guid, int>();
                        var remainders = new List<(Guid id, decimal frac)>();
                        int allocated = 0;
                        foreach (var kv in desired)
                        {
                            var flo = (int)Math.Floor(kv.Value);
                            alloc[kv.Key] = flo;
                            allocated += flo;
                            remainders.Add((kv.Key, kv.Value - flo));
                        }

                        var remaining = assets.Count - allocated;
                        remainders = TieBreakOrder(remainders).ToList();
                        int ri = 0;
                        while (remaining > 0 && remainders.Count > 0)
                        {
                            var id = remainders[ri % remainders.Count].id;
                            alloc[id] = alloc.GetValueOrDefault(id) + 1;
                            remaining--;
                            ri++;
                        }

                        // Now assign assets in stable order to heirs according to counts
                        int assetIndex = 0;
                        foreach (var h in heirs)
                        {
                            var count = alloc.GetValueOrDefault(h);
                            for (int k = 0; k < count && assetIndex < assets.Count; k++)
                            {
                                assetAssignments[h].Add(assets[assetIndex++]);
                            }
                        }
                        // If any assets left (due to rounding), assign round-robin
                        while (assetIndex < assets.Count)
                        {
                            foreach (var h in heirs)
                            {
                                if (assetIndex >= assets.Count) break;
                                assetAssignments[h].Add(assets[assetIndex++]);
                            }
                        }
                    }
                    else
                    {
                        // fallback to equal split
                        for (int i = 0; i < assets.Count; i++)
                        {
                            var h = heirs[i % heirs.Length];
                            assetAssignments[h].Add(assets[i]);
                        }
                    }
                }
                else
                {
                    // Equal split: deterministic round-robin assignment
                    for (int i = 0; i < assets.Count; i++)
                    {
                        var h = heirs[i % heirs.Length];
                        assetAssignments[h].Add(assets[i]);
                    }
                }
            }

            foreach (var kvp in assetAssignments)
            {
                var heirId = kvp.Key;
                var list = kvp.Value;
                foreach (var asset in list)
                {
                    var prevOwner = asset.OwnerId;
                    asset.OwnerId = heirId;
                    asset.AcquisitionType = "inheritance";
                    asset.AcquiredAt = DateTime.UtcNow;
                    asset.Confidence = 1.0;

                    transfers.Add(new
                    {
                        assetId = asset.AssetId,
                        prevOwner,
                        newOwner = heirId,
                        acquisitionType = asset.AcquisitionType
                    });

                    var ev = NewEvent("inheritance_transfer", asset.AssetType, new
                    {
                        inheritance = rec.Id,
                        assetId = asset.AssetId,
                        prevOwner,
                        newOwner = heirId
                    });
                    _db.GameEvents.Add(ev);
                    createdEvents.Add(ev);
                }
            }

            // resolution
            var resolutionObj = new
            {
                rule = rule.ToString().ToLowerInvariant(),
                heirs,
                applied = true,
                transferred = transfers.Count,
                distributedAt = DateTime.UtcNow
            };
            rec.ResolutionJson = JsonSerializer.Serialize(resolutionObj);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // publish after commit
            foreach (var ev in createdEvents)
            {
                try { await _dispatcher.EnqueueAsync(ev); } catch { /* логируйте если нужно */ }
            }

            return InheritanceResult.Ok(inheritanceRecordId, resolutionObj);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            rec.ResolutionJson = JsonSerializer.Serialize(new { rule = rule.ToString().ToLowerInvariant(), heirs, applied = false, error = ex.Message });
            await _db.SaveChangesAsync(ct);
            return InheritanceResult.Error(inheritanceRecordId, ex.Message);
        }
    }

    // -------------- helpers --------------

    private static Guid[] ParseHeirs(string? heirsJson)
    {
        if (string.IsNullOrWhiteSpace(heirsJson)) return Array.Empty<Guid>();
        try
        {
            var heirs = JsonSerializer.Deserialize<Guid[]>(heirsJson);
            return heirs?.Where(g => g != Guid.Empty).Distinct().ToArray() ?? Array.Empty<Guid>();
        }
        catch
        {
            return Array.Empty<Guid>();
        }
    }

    private enum InheritanceRule { EqualSplit, Primogeniture, Shares }

    private static InheritanceRule ParseRule(string? rulesJson, out Dictionary<Guid, decimal> shares)
    {
        shares = new Dictionary<Guid, decimal>();
        if (string.IsNullOrWhiteSpace(rulesJson)) return InheritanceRule.EqualSplit;

        try
        {
            using var doc = JsonDocument.Parse(rulesJson);
            var root = doc.RootElement;

            var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()!.Trim().ToLowerInvariant()
                : "equal_split";

            InheritanceRule rule = type switch
            {
                "primogeniture" => InheritanceRule.Primogeniture,
                "shares" => InheritanceRule.Shares,
                _ => InheritanceRule.EqualSplit
            };

            if (rule == InheritanceRule.Shares &&
                root.TryGetProperty("shares", out var sh) &&
                sh.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in sh.EnumerateArray())
                {
                    if (!el.TryGetProperty("heir", out var heirEl) || heirEl.ValueKind != JsonValueKind.String) continue;
                    if (!el.TryGetProperty("pct", out var pctEl)) continue;

                    if (!Guid.TryParse(heirEl.GetString(), out var hid) || hid == Guid.Empty) continue;

                    decimal pct = 0m;
                    switch (pctEl.ValueKind)
                    {
                        case JsonValueKind.Number:
                            pct = pctEl.GetDecimal();
                            break;
                        case JsonValueKind.String:
                            if (!decimal.TryParse(pctEl.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out pct))
                                pct = 0m;
                            break;
                    }

                    if (pct > 0m) shares[hid] = pct;
                }
            }

            return rule;
        }
        catch
        {
            return InheritanceRule.EqualSplit;
        }
    }

    private static Dictionary<Guid, decimal> NormalizeShares(Dictionary<Guid, decimal> raw)
    {
        var positive = raw.Where(kv => kv.Value > 0m).ToDictionary(kv => kv.Key, kv => kv.Value);
        var sum = positive.Values.Sum();
        if (sum <= 0m) return new();
        return positive.ToDictionary(kv => kv.Key, kv => kv.Value / sum);
    }

    private static Dictionary<Guid, decimal> NormalizeSharesForHeirs(Guid[] heirs, Dictionary<Guid, decimal> raw)
    {
        var norm = NormalizeShares(raw);
        // Вернём только тех наследников, что реально указаны в массиве heirs
        var map = new Dictionary<Guid, decimal>();
        foreach (var h in heirs)
            map[h] = norm.GetValueOrDefault(h, 0m);
        // Если все нули (в правилах наследники не совпали) — вернём пустой словарь
        return map.Any(kv => kv.Value > 0m) ? map : new();
    }

    private static long ToCurrencyUnitsSafe(decimal amount, long minimalUnitFactor)
    {
        // Точно не больше исходного значения в деньгах (truncate), избегаем переполнения long
        try
        {
            var unitsDec = decimal.Truncate(amount * minimalUnitFactor);
            if (unitsDec > long.MaxValue) return long.MaxValue;
            if (unitsDec < long.MinValue) return long.MinValue;
            return (long)unitsDec;
        }
        catch
        {
            // крайний случай
            return amount >= 0 ? long.MaxValue : long.MinValue;
        }
    }

    private static bool ContainsGuid(string jsonList, Guid id)
    {
        // Простейшая безопасная проверка для строкового массива GUID'ов в JSON.
        // Лучше заменить на нативный JSON-поиск БД при наличии.
        try
        {
            var arr = JsonSerializer.Deserialize<Guid[]>(jsonList);
            return arr?.Contains(id) == true;
        }
        catch { return false; }
    }

    private static GameEvent NewEvent(string type, string location, object payload)
    {
        // serialize payload to JsonNode so we can inject meta.traceId without changing caller payload shape
        try
        {
            var node = System.Text.Json.JsonSerializer.SerializeToNode(payload) as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
            if (!node.ContainsKey("meta"))
            {
                node["meta"] = new System.Text.Json.Nodes.JsonObject { ["traceId"] = Guid.NewGuid().ToString() };
            }
            return new GameEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Type = type,
                Location = location,
                PayloadJson = node.ToJsonString()
            };
        }
        catch
        {
            // fallback: embed traceId in a wrapper object
            var wrapper = new { meta = new { traceId = Guid.NewGuid().ToString() }, payload };
            return new GameEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Type = type,
                Location = location,
                PayloadJson = JsonSerializer.Serialize(wrapper)
            };
        }
    }

    // Tie-break ordering: given a list of (id, frac) we order by frac desc, and when fracs equal
    // apply policy from InheritanceOptions: DeterministicHash, Random (via _random), or Stable.
    private List<(Guid id, decimal frac)> TieBreakOrder(List<(Guid id, decimal frac)> list)
    {
        // Primary sort: fractional part desc
        var ordered = list.OrderByDescending(x => x.frac).ToList();
        // Group by frac to detect ties
        var groups = ordered.GroupBy(x => x.frac).ToList();
        var result = new List<(Guid id, decimal frac)>();
        foreach (var g in groups)
        {
            var items = g.ToList();
            if (items.Count == 1)
            {
                result.Add(items[0]);
                continue;
            }

            var opt = _inheritanceOptions?.Value?.TieBreaker ?? Imperium.Api.TieBreakerOption.DeterministicHash;
            if (opt == Imperium.Api.TieBreakerOption.Stable)
            {
                // Keep original stable order by id in the heirs list (caller preserved ordering)
                items.Sort((a, b) => a.id.CompareTo(b.id));
                result.AddRange(items);
            }
            else if (opt == Imperium.Api.TieBreakerOption.Random)
            {
                // Shuffle deterministically using IRandomProvider
                var rnd = _random ?? new Imperium.Api.Utils.SeedableRandom();
                var shuffled = items.OrderBy(_ => rnd.NextDouble()).ToList();
                result.AddRange(shuffled);
            }
            else // DeterministicHash
            {
                // Compute SHA256 hash of id + salt and order by hash descending
                var salt = _inheritanceOptions?.Value?.Salt ?? string.Empty;
                var hashed = items.Select(it => new { it.id, it.frac, hash = ComputeHash(it.id, salt) })
                                  .OrderByDescending(x => x.hash, StringComparer.Ordinal)
                                  .Select(x => (x.id, x.frac)).ToList();
                result.AddRange(hashed);
            }
        }
        return result;
    }

    private static string ComputeHash(Guid id, string salt)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var input = System.Text.Encoding.UTF8.GetBytes(id.ToString("D") + "|" + (salt ?? string.Empty));
        var hash = sha.ComputeHash(input);
        return Convert.ToHexString(hash);
    }
}

public record InheritanceResult(Guid RecordId, bool IsSuccess, string Message, object? Data)
{
    public static InheritanceResult NotFound(Guid id) => new(id, false, "not_found", null);
    public static InheritanceResult NoHeirs(Guid id) => new(id, false, "no_heirs", null);
    public static InheritanceResult Ok(Guid id, object data) => new(id, true, "ok", data);
    public static InheritanceResult Error(Guid id, string message) => new(id, false, message, null);
}
