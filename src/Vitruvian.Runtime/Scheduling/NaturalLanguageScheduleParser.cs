using System.Text.RegularExpressions;
using VitruvianAbstractions.Interfaces;

namespace VitruvianRuntime.Scheduling;

/// <summary>
/// Parses plain-text schedule descriptions (e.g. "every 5 minutes", "hourly", "daily")
/// into a <see cref="TimeSpan"/> repeat interval.
/// Uses local regex heuristics first and falls back to the LLM when available.
/// </summary>
public sealed class NaturalLanguageScheduleParser
{
    private readonly IModelClient? _modelClient;

    public NaturalLanguageScheduleParser(IModelClient? modelClient = null)
    {
        _modelClient = modelClient;
    }

    /// <summary>
    /// Attempts to parse a plain-text schedule description into a <see cref="TimeSpan"/>.
    /// Returns <c>null</c> when the input cannot be understood.
    /// </summary>
    public async Task<TimeSpan?> ParseAsync(string description, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        // Try local heuristic first (fast, no API call)
        var local = TryParseLocal(description);
        if (local.HasValue)
            return local;

        // Fall back to LLM if available
        if (_modelClient is not null)
            return await TryParseLlmAsync(description, ct);

        return null;
    }

    /// <summary>
    /// Fast local regex-based parsing for common schedule patterns.
    /// </summary>
    internal static TimeSpan? TryParseLocal(string description)
    {
        var text = description.Trim().ToLowerInvariant();

        // "every N second(s)/minute(s)/hour(s)/day(s)"
        var everyMatch = Regex.Match(text, @"every\s+(\d+)\s+(second|minute|hour|day)s?");
        if (everyMatch.Success)
        {
            var amount = int.Parse(everyMatch.Groups[1].Value);
            return everyMatch.Groups[2].Value switch
            {
                "second" => TimeSpan.FromSeconds(amount),
                "minute" => TimeSpan.FromMinutes(amount),
                "hour" => TimeSpan.FromHours(amount),
                "day" => TimeSpan.FromDays(amount),
                _ => null
            };
        }

        // "every minute/hour/day"
        var everyUnit = Regex.Match(text, @"every\s+(second|minute|hour|day)$");
        if (everyUnit.Success)
        {
            return everyUnit.Groups[1].Value switch
            {
                "second" => TimeSpan.FromSeconds(1),
                "minute" => TimeSpan.FromMinutes(1),
                "hour" => TimeSpan.FromHours(1),
                "day" => TimeSpan.FromDays(1),
                _ => null
            };
        }

        // "daily" / "hourly"
        if (text.Contains("daily"))
            return TimeSpan.FromDays(1);
        if (text.Contains("hourly"))
            return TimeSpan.FromHours(1);

        // "every half hour" / "every 30 min"
        if (text.Contains("half hour") || text.Contains("half an hour"))
            return TimeSpan.FromMinutes(30);

        return null;
    }

    private async Task<TimeSpan?> TryParseLlmAsync(string description, CancellationToken ct)
    {
        var prompt =
            $"""
             Parse the following schedule description into a repeat interval in total seconds.
             Reply with ONLY a single integer representing the number of seconds.
             If you cannot determine the interval, reply with "0".
             
             Schedule description: "{description}"
             """;

        try
        {
            var response = await _modelClient!.GenerateAsync(prompt, ct);
            var cleaned = response.Trim();

            if (int.TryParse(cleaned, out var seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);
        }
        catch
        {
            // LLM call failed — return null so the caller can report the error.
        }

        return null;
    }
}
