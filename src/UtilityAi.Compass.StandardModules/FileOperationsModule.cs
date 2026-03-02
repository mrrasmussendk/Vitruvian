using System.Text.Json;
using UtilityAi.Compass.Abstractions.Interfaces;

namespace UtilityAi.Compass.StandardModules;

/// <summary>
/// File operations module implementing ICompassModule.
/// Handles reading and writing files on the local filesystem.
/// </summary>
public sealed class FileOperationsModule : ICompassModule
{
    private readonly IModelClient? _modelClient;
    private readonly string _workingDirectory;
    private const int MaxContentSizeBytes = 10 * 1024 * 1024; // 10MB limit
    private const int MaxFilenameLength = 255;

    public string Domain => "file-operations";
    public string Description => "Read content from files or write/create text files with specific filenames (e.g., notes.txt, config.json)";

    public FileOperationsModule(IModelClient? modelClient = null, string? workingDirectory = null)
    {
        _modelClient = modelClient;
        _workingDirectory = workingDirectory ?? Directory.GetCurrentDirectory();
    }

    public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
    {
        // Extract file path from request
        var path = ExtractFilePath(request);
        if (string.IsNullOrWhiteSpace(path))
            return "Could not identify a file path in your request.";

        if (_modelClient is null)
        {
            // Fallback: assume read operation
            return await ExecuteFileReadAsync(path);
        }

        // Use LLM to determine operation type and parameters
        var operation = await DetermineFileOperationAsync(request, ct);

        if (operation.Type == FileOperationType.Read)
        {
            return await ExecuteFileReadAsync(operation.Path);
        }
        else if (operation.Type == FileOperationType.Write)
        {
            return await ExecuteFileWriteAsync(operation.Path, operation.Content ?? string.Empty);
        }

        return "Could not determine file operation type.";
    }

    private async Task<string> ExecuteFileReadAsync(string path)
    {
        try
        {
            var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(_workingDirectory, path);

            if (!File.Exists(fullPath))
                return $"File not found: {path}";

            var content = await File.ReadAllTextAsync(fullPath);
            return content;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Failed to read file: {ex.Message}";
        }
    }

    private async Task<string> ExecuteFileWriteAsync(string path, string content)
    {
        try
        {
            var validation = ValidateFileParameters(path, content);
            if (!validation.IsValid)
                return $"Invalid file parameters: {validation.Error}";

            var sanitizedPath = SanitizePath(path);
            var fullPath = Path.IsPathRooted(sanitizedPath) ? sanitizedPath : Path.Combine(_workingDirectory, sanitizedPath);

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(fullPath, content);
            return $"File created: {Path.GetRelativePath(_workingDirectory, fullPath)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return $"Failed to create file: {ex.Message}";
        }
    }

    private async Task<FileOperation> DetermineFileOperationAsync(string requestText, CancellationToken ct)
    {
        if (_modelClient is null)
            return new FileOperation(FileOperationType.Read, ExtractFilePath(requestText), null);

        try
        {
            var systemMessage = @"Determine the file operation type and extract parameters.
Return ONLY valid JSON in this format: {""type"":""read""|""write"",""path"":""filename.ext"",""content"":""content if write, null if read""}
- type: 'read' for reading/showing/outputting files, 'write' for creating/writing files
- path: the exact filename mentioned
- content: file content if writing, null if reading";

            var response = await _modelClient.CompleteAsync(
                systemMessage: systemMessage,
                userMessage: $"Analyze this file operation request: {requestText}",
                cancellationToken: ct);

            return ParseFileOperation(response);
        }
        catch (Exception)
        {
            // Fallback to simple extraction
            return new FileOperation(FileOperationType.Read, ExtractFilePath(requestText), null);
        }
    }

    private static FileOperation ParseFileOperation(string jsonResponse)
    {
        try
        {
            var json = jsonResponse.Trim();
            if (json.StartsWith("```"))
            {
                var lines = json.Split('\n');
                json = string.Join('\n', lines.Skip(1).Take(lines.Length - 2));
            }
            if (json.StartsWith("```json"))
            {
                json = json.Substring(7);
                var endIndex = json.IndexOf("```");
                if (endIndex > 0)
                    json = json.Substring(0, endIndex);
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var typeStr = root.GetProperty("type").GetString()?.ToLowerInvariant();
            var type = typeStr == "write" ? FileOperationType.Write : FileOperationType.Read;
            var path = root.GetProperty("path").GetString() ?? string.Empty;
            var content = root.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String
                ? contentEl.GetString()
                : null;

            return new FileOperation(type, path, content);
        }
        catch (JsonException)
        {
            return new FileOperation(FileOperationType.Read, string.Empty, null);
        }
    }

    private static string ExtractFilePath(string text)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Return the last token that looks like a file path
        for (var i = tokens.Length - 1; i >= 0; i--)
        {
            if (tokens[i].Contains('.') || tokens[i].Contains(Path.DirectorySeparatorChar) ||
                tokens[i].Contains(Path.AltDirectorySeparatorChar))
                return tokens[i];
        }

        return tokens.Length > 0 ? tokens[^1] : string.Empty;
    }

    private static (bool IsValid, string? Error) ValidateFileParameters(string filename, string content)
    {
        if (filename.Length > MaxFilenameLength)
            return (false, $"Filename too long (max {MaxFilenameLength} characters)");

        if (filename.Contains("..") || filename.Contains("~"))
            return (false, "Path traversal not allowed");

        if (Path.IsPathRooted(filename))
            return (false, "Absolute paths not allowed");

        var invalidChars = Path.GetInvalidFileNameChars();
        if (filename.Any(c => invalidChars.Contains(c) && c != Path.DirectorySeparatorChar && c != Path.AltDirectorySeparatorChar))
            return (false, "Filename contains invalid characters");

        var contentBytes = System.Text.Encoding.UTF8.GetByteCount(content);
        if (contentBytes > MaxContentSizeBytes)
            return (false, $"Content too large (max {MaxContentSizeBytes / (1024 * 1024)}MB)");

        return (true, null);
    }

    private static string SanitizePath(string filename)
    {
        var normalized = filename.Replace('\\', Path.DirectorySeparatorChar)
                                 .Replace('/', Path.DirectorySeparatorChar);
        normalized = normalized.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalized;
    }

    private enum FileOperationType
    {
        Read,
        Write
    }

    private sealed record FileOperation(FileOperationType Type, string Path, string? Content);
}
