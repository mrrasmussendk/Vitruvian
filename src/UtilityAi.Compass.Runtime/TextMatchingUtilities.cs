namespace UtilityAi.Compass.Runtime;

/// <summary>
/// Utilities for keyword-based text matching and scoring.
/// </summary>
public static class TextMatchingUtilities
{
    private static readonly char[] WordSeparators = [' ', '.', ',', '!', '?'];
    private const int MinWordLength = 3;

    /// <summary>
    /// Calculates a match score between request words and a description.
    /// Returns a score based on how many request words appear in the description.
    /// </summary>
    /// <param name="request">The user request text.</param>
    /// <param name="description">The description to match against.</param>
    /// <returns>Match score (higher is better).</returns>
    public static int CalculateMatchScore(string request, string description)
    {
        var requestWords = SplitAndFilterWords(request.ToLowerInvariant());
        var descriptionWords = SplitAndFilterWords(description.ToLowerInvariant());

        var score = 0;
        foreach (var requestWord in requestWords)
        {
            foreach (var descWord in descriptionWords)
            {
                // Check for partial matches in either direction
                if (descWord.Contains(requestWord) || requestWord.Contains(descWord))
                {
                    score += 1;
                }
            }
        }

        return score;
    }

    /// <summary>
    /// Splits text into words and filters out short words that don't contribute to matching.
    /// </summary>
    private static string[] SplitAndFilterWords(string text)
    {
        return text
            .Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= MinWordLength)
            .ToArray();
    }
}
