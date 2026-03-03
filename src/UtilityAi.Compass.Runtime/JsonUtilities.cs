namespace UtilityAi.Compass.Runtime;

/// <summary>
/// Utilities for cleaning and parsing JSON responses from LLMs.
/// </summary>
public static class JsonUtilities
{
    /// <summary>
    /// Removes markdown code fences (```json and ```) from JSON text.
    /// </summary>
    public static string CleanMarkdownCodeFences(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[7..];
            var end = trimmed.IndexOf("```", StringComparison.Ordinal);
            if (end > 0)
                trimmed = trimmed[..end];
        }
        else if (trimmed.StartsWith("```"))
        {
            var lines = trimmed.Split('\n');
            if (lines.Length >= 3)
            {
                // Skip first line (```), take all middle lines, skip last line (```)
                trimmed = string.Join('\n', lines.Skip(1).Take(lines.Length - 2));
            }
            else
            {
                // Malformed fence, just skip first line
                trimmed = string.Join('\n', lines.Skip(1));
            }
        }

        return trimmed.Trim();
    }
}
