using UtilityAi.Capabilities;
using UtilityAi.Consideration;
using UtilityAi.Consideration.General;
using UtilityAi.Compass.Abstractions;
using UtilityAi.Compass.Abstractions.Facts;
using UtilityAi.Compass.PluginSdk.Attributes;
using UtilityAi.Utils;

namespace UtilityAi.Compass.StandardModules;

/// <summary>
/// Standard module that creates a file on disk.
/// Responds to execute-intent requests containing a file path and content.
/// </summary>
[CompassCapability("file-creation", priority: 3)]
[CompassGoals(GoalTag.Execute)]
[CompassLane(Lane.Execute)]
[CompassCost(0.2)]
[CompassRisk(0.3)]
[CompassSideEffects(SideEffectLevel.Write)]
[CompassCooldown("file-creation.write", secondsTtl: 5)]
public sealed class FileCreationModule : ICapabilityModule
{
    /// <inheritdoc />
    public IEnumerable<Proposal> Propose(Runtime rt)
    {
        var request = rt.Bus.GetOrDefault<UserRequest>();
        if (request is null) yield break;

        yield return new Proposal(
            id: "file-creation.write",
            cons: [new ConstantValue(0.75)],
            act: _ =>
            {
                var (path, content) = ParseFileRequest(request.Text);
                try
                {
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);
                    File.WriteAllText(path, content);
                    rt.Bus.Publish(new AiResponse($"File created: {path}"));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
                {
                    rt.Bus.Publish(new AiResponse($"Failed to create file: {path} ({ex.Message})"));
                }
                return Task.CompletedTask;
            }
        ) { Description = "Create a file with the specified content" };
    }

    public static (string Path, string Content) ParseFileRequest(string text)
    {
        // Expected patterns:
        //   "create file <path> with content <content>"
        //   "write the text '<content>' to file <path>"
        //   "write '<content>' to file <path>"
        //   "insert text <content> into file <path>"
        var lower = text.ToLowerInvariant();

        // Pattern: "with content"
        var withContent = lower.IndexOf(" with content ", StringComparison.Ordinal);
        if (withContent >= 0)
        {
            var before = text[..withContent];
            var content = text[(withContent + " with content ".Length)..];

            var pathToken = ExtractPathToken(before);
            return (pathToken, content);
        }

        // Pattern: "write [the text/word] '<content>' [to|into] [the] file <path>"
        var patterns = new[] { " into the file ", " into file ", " to the file ", " to file " };
        int toFileIdx = -1;
        string matchedPattern = "";

        foreach (var pattern in patterns)
        {
            toFileIdx = lower.IndexOf(pattern, StringComparison.Ordinal);
            if (toFileIdx >= 0)
            {
                matchedPattern = pattern;
                break;
            }
        }

        if (toFileIdx >= 0)
        {
            var beforeTo = text[..toFileIdx];
            var afterTo = text[(toFileIdx + matchedPattern.Length)..];

            // Extract content from the "before" part - look for quoted content
            var content = ExtractQuotedContent(beforeTo);
            if (string.IsNullOrEmpty(content))
            {
                // No quotes, extract after "write [the text/word]"
                var writeIdx = lower.IndexOf("write", StringComparison.Ordinal);
                if (writeIdx >= 0)
                {
                    var afterWrite = beforeTo[(writeIdx + 5)..].TrimStart();
                    // Remove common prefixes like "the text", "the word", etc.
                    var prefixes = new[] { "the text ", "the word ", "text ", "word " };
                    foreach (var prefix in prefixes)
                    {
                        if (afterWrite.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            afterWrite = afterWrite[prefix.Length..];
                            break;
                        }
                    }
                    content = afterWrite.Trim();
                }
            }

            // Extract path from "after" part
            var path = afterTo.Trim();
            return (path, content);
        }

        // Fallback: treat last token as path, rest as content
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 3)
        {
            var path = tokens[^1];
            return (path, string.Join(' ', tokens[2..^1]));
        }

        return ("output.txt", text);
    }

    private static string ExtractQuotedContent(string text)
    {
        // Look for content in single quotes
        var singleStart = text.IndexOf('\'');
        if (singleStart >= 0)
        {
            var singleEnd = text.IndexOf('\'', singleStart + 1);
            if (singleEnd > singleStart)
                return text.Substring(singleStart + 1, singleEnd - singleStart - 1);
        }

        // Look for content in double quotes
        var doubleStart = text.IndexOf('"');
        if (doubleStart >= 0)
        {
            var doubleEnd = text.IndexOf('"', doubleStart + 1);
            if (doubleEnd > doubleStart)
                return text.Substring(doubleStart + 1, doubleEnd - doubleStart - 1);
        }

        return string.Empty;
    }

    private static string ExtractPathToken(string segment)
    {
        var tokens = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 0 ? tokens[^1] : "output.txt";
    }
}
