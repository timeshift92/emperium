using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace Imperium.Api;

public class MetricsService
{
    private static readonly Meter Meter = new("Imperium.Api.Metrics");
    private static readonly Histogram<double> TickDurationHistogram = Meter.CreateHistogram<double>("imperium_tick_duration_ms", unit: "ms");
    private static readonly Histogram<double> AgentDurationHistogram = Meter.CreateHistogram<double>("imperium_agent_duration_ms", unit: "ms");
    private static readonly Histogram<double> LlmDurationHistogram = Meter.CreateHistogram<double>("imperium_llm_duration_ms", unit: "ms");

    private const int TickSampleCapacity = 120;
    private const int LlmSampleCapacity = 200;

    private readonly double[] _tickSamples = new double[TickSampleCapacity];
    private readonly double[] _llmSamples = new double[LlmSampleCapacity];
    private int _tickSampleCount;
    private int _tickWriteIndex;
    private int _llmSampleCount;
    private int _llmWriteIndex;

    private readonly object _tickLock = new();
    private readonly object _llmLock = new();

    private readonly Dictionary<string, long> _counters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Counter<long>> _counterInstruments = new(StringComparer.OrdinalIgnoreCase);

    public void Increment(string name)
    {
        var counter = GetCounter(name);
        lock (_counters)
        {
            _counters.TryGetValue(name, out var current);
            _counters[name] = current + 1;
        }
        counter.Add(1, new KeyValuePair<string, object?>("metric", name));
    }

    public void Add(string name, long value)
    {
        var counter = GetCounter(name);
        lock (_counters)
        {
            _counters.TryGetValue(name, out var current);
            _counters[name] = current + value;
        }
        counter.Add(value, new KeyValuePair<string, object?>("metric", name));
    }

    public void RecordTickDuration(double milliseconds)
    {
        TickDurationHistogram.Record(milliseconds);
        lock (_tickLock)
        {
            _tickSamples[_tickWriteIndex] = milliseconds;
            _tickWriteIndex = (_tickWriteIndex + 1) % TickSampleCapacity;
            if (_tickSampleCount < TickSampleCapacity)
            {
                _tickSampleCount++;
            }
        }
    }

    public void RecordAgentDuration(string agent, double milliseconds)
    {
        AgentDurationHistogram.Record(milliseconds, new KeyValuePair<string, object?>("agent", agent));
    }

    public void RecordLlmDuration(double milliseconds)
    {
        LlmDurationHistogram.Record(milliseconds);
        lock (_llmLock)
        {
            _llmSamples[_llmWriteIndex] = milliseconds;
            _llmWriteIndex = (_llmWriteIndex + 1) % LlmSampleCapacity;
            if (_llmSampleCount < LlmSampleCapacity)
            {
                _llmSampleCount++;
            }
        }
    }

    public long Get(string name)
    {
        lock (_counters)
        {
            return _counters.TryGetValue(name, out var v) ? v : 0L;
        }
    }

    public IReadOnlyDictionary<string, long> Snapshot()
    {
        lock (_counters)
        {
            return new Dictionary<string, long>(_counters);
        }
    }

    public double[] GetRecentTickDurations()
    {
        lock (_tickLock)
        {
            var result = new double[_tickSampleCount];
            for (var i = 0; i < _tickSampleCount; i++)
            {
                var index = (_tickWriteIndex - _tickSampleCount + i) % TickSampleCapacity;
                if (index < 0) index += TickSampleCapacity;
                result[i] = _tickSamples[index];
            }
            return result;
        }
    }

    public double[] GetRecentLlmDurations()
    {
        lock (_llmLock)
        {
            var result = new double[_llmSampleCount];
            for (var i = 0; i < _llmSampleCount; i++)
            {
                var index = (_llmWriteIndex - _llmSampleCount + i) % LlmSampleCapacity;
                if (index < 0) index += LlmSampleCapacity;
                result[i] = _llmSamples[index];
            }
            return result;
        }
    }

    private static string Sanitize(string metricName)
    {
        return metricName
            .Replace('.', '_')
            .Replace('-', '_')
            .Replace(' ', '_')
            .ToLowerInvariant();
    }

    private Counter<long> GetCounter(string name)
    {
        lock (_counterInstruments)
        {
            if (!_counterInstruments.TryGetValue(name, out var counter))
            {
                counter = Meter.CreateCounter<long>($"imperium_{Sanitize(name)}_total");
                _counterInstruments[name] = counter;
            }
            return counter;
        }
    }
}
