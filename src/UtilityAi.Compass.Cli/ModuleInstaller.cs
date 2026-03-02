using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;

namespace UtilityAi.Compass.Cli;

public static class ModuleInstaller
{
    private static readonly HttpClient _httpClient = new();
    private const string ManifestFileName = "compass-manifest.json";

    public sealed record ModuleInstallResult(bool Success, string Message)
    {
        public static ModuleInstallResult FromSuccess(string message) => new(true, message);
        public static ModuleInstallResult FromFailure(string message) => new(false, message);
    }

    public static bool TryRunInstallScript()
    {
        var scriptPath = OperatingSystem.IsWindows()
            ? Path.Combine(AppContext.BaseDirectory, "scripts", "install.ps1")
            : Path.Combine(AppContext.BaseDirectory, "scripts", "install.sh");

        if (!File.Exists(scriptPath))
            return false;

        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("powershell", $"-ExecutionPolicy Bypass -File \"{scriptPath}\"")
            : new ProcessStartInfo("bash", $"\"{scriptPath}\"");

        startInfo.UseShellExecute = false;
        var process = Process.Start(startInfo);
        if (process is null)
            return false;

        process.WaitForExit();
        return process.ExitCode == 0;
    }

    public static async Task<string> InstallAsync(
        string moduleSpec,
        string pluginsPath,
        bool allowUnsigned = false,
        Func<string, string?>? secretPrompt = null,
        CancellationToken cancellationToken = default) =>
        (await InstallWithResultAsync(moduleSpec, pluginsPath, allowUnsigned, secretPrompt, cancellationToken)).Message;

    public static async Task<ModuleInstallResult> InstallWithResultAsync(
        string moduleSpec,
        string pluginsPath,
        bool allowUnsigned = false,
        Func<string, string?>? secretPrompt = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(pluginsPath);

        if (File.Exists(moduleSpec))
            return InstallFromFile(moduleSpec, pluginsPath, allowUnsigned, secretPrompt);

        if (!TryParsePackageReference(moduleSpec, out var packageId, out var packageVersion))
            return ModuleInstallResult.FromFailure("Module install failed: provide a .dll/.nupkg path or NuGet reference in the form PackageId@Version.");

        var downloadUrl = $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLowerInvariant()}/{packageVersion.ToLowerInvariant()}/{packageId.ToLowerInvariant()}.{packageVersion.ToLowerInvariant()}.nupkg";
        using var response = await _httpClient.GetAsync(downloadUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return ModuleInstallResult.FromFailure($"Module install failed: could not download '{packageId}@{packageVersion}' (HTTP {(int)response.StatusCode}).");

        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid().ToString("N")}.nupkg");
        await using (var tempFile = File.Create(tempPath))
            await response.Content.CopyToAsync(tempFile, cancellationToken);

        try
        {
            return InstallFromNupkg(tempPath, pluginsPath, packageId, allowUnsigned, secretPrompt);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public static bool TryParsePackageReference(string moduleSpec, out string packageId, out string packageVersion)
    {
        packageId = string.Empty;
        packageVersion = string.Empty;

        if (string.IsNullOrWhiteSpace(moduleSpec))
            return false;

        var parts = moduleSpec.Split('@', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return false;

        packageId = parts[0];
        packageVersion = parts[1];
        return packageId.Length > 0 && packageVersion.Length > 0;
    }

    public static bool TryParseInstallCommand(string input, out string moduleSpec)
        => TryParseInstallCommand(input, out moduleSpec, out _);

    public static bool TryParseInstallCommand(string input, out string moduleSpec, out bool allowUnsigned)
    {
        moduleSpec = string.Empty;
        allowUnsigned = false;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        const string command = "/install-module";
        if (!input.TrimStart().StartsWith(command, StringComparison.OrdinalIgnoreCase))
            return false;

        var args = input.TrimStart()[command.Length..]
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--allow-unsigned", StringComparison.OrdinalIgnoreCase))
            {
                allowUnsigned = true;
                continue;
            }

            if (moduleSpec.Length == 0)
                moduleSpec = arg;
        }

        return moduleSpec.Length > 0;
    }

    public static bool TryParseNewModuleCommand(string input, out string moduleName, out string outputPath)
    {
        moduleName = string.Empty;
        outputPath = Directory.GetCurrentDirectory();
        if (string.IsNullOrWhiteSpace(input))
            return false;

        const string command = "/new-module";
        if (!input.TrimStart().StartsWith(command, StringComparison.OrdinalIgnoreCase))
            return false;

        var args = input.TrimStart()[command.Length..].Trim();
        if (string.IsNullOrWhiteSpace(args))
            return false;

        var split = args.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        moduleName = split[0];
        if (split.Length > 1)
            outputPath = split[1];

        return moduleName.Length > 0;
    }

    public static IReadOnlyList<string> ListInstalledModules(string pluginsPath)
    {
        if (!Directory.Exists(pluginsPath))
            return [];

        return Directory
            .EnumerateFiles(pluginsPath, "*.dll", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string ScaffoldNewModule(string moduleName, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
            return "Module scaffold failed: provide a module name.";

        if (moduleName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || moduleName.Contains(Path.DirectorySeparatorChar)
            || moduleName.Contains(Path.AltDirectorySeparatorChar))
            return "Module scaffold failed: module name contains invalid path characters.";

        var targetRoot = string.IsNullOrWhiteSpace(outputPath) ? Directory.GetCurrentDirectory() : outputPath;
        var moduleDirectory = Path.GetFullPath(Path.Combine(targetRoot, moduleName.Trim()));
        if (Directory.Exists(moduleDirectory) && Directory.EnumerateFileSystemEntries(moduleDirectory).Any())
            return $"Module scaffold failed: target directory '{moduleDirectory}' already exists and is not empty.";

        Directory.CreateDirectory(moduleDirectory);

        var classBaseName = SanitizeIdentifier(moduleName.Trim());
        var className = $"{classBaseName}Module";
        var moduleNamespace = classBaseName;
        var domainId = SanitizeDomainId(moduleName.Trim());

        var projectPath = Path.Combine(moduleDirectory, $"{moduleName.Trim()}.csproj");
        var classPath = Path.Combine(moduleDirectory, $"{className}.cs");

        File.WriteAllText(projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="UtilityAi" Version="1.6.5" />
              </ItemGroup>

            </Project>
            """);

        File.WriteAllText(classPath,
            $$"""
            using UtilityAi.Compass.Abstractions.Interfaces;

            namespace {{moduleNamespace}};

            /// <summary>
            /// Example module created by compass --new-module.
            /// Implements ICompassModule for simplified LLM-based routing.
            /// </summary>
            public sealed class {{className}} : ICompassModule
            {
                private readonly IModelClient? _modelClient;

                public string Domain => "{{domainId}}";
                public string Description => "Example module - describe what this module does";

                public {{className}}(IModelClient? modelClient = null)
                {
                    _modelClient = modelClient;
                }

                public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
                {
                    // TODO: Implement your module logic here
                    if (_modelClient is null)
                        return "No model configured.";

                    return await _modelClient.GenerateAsync($"Handle request: {request}", ct);
                }
            }
            """);

        return $"Created module scaffold at '{moduleDirectory}'. Build it with 'dotnet build'.";
    }

    public static async Task<ModuleInspectionReport> InspectAsync(string moduleSpec, CancellationToken cancellationToken = default)
    {
        if (File.Exists(moduleSpec))
            return await InspectFileAsync(moduleSpec, cancellationToken);

        if (!TryParsePackageReference(moduleSpec, out var packageId, out var packageVersion))
            return ModuleInspectionReport.Error(moduleSpec, "Inspection failed: provide a .dll/.nupkg path or NuGet reference in the form PackageId@Version.");

        var downloadUrl = $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLowerInvariant()}/{packageVersion.ToLowerInvariant()}/{packageId.ToLowerInvariant()}.{packageVersion.ToLowerInvariant()}.nupkg";
        using var response = await _httpClient.GetAsync(downloadUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return ModuleInspectionReport.Error(moduleSpec, $"Inspection failed: could not download '{packageId}@{packageVersion}' (HTTP {(int)response.StatusCode}).");

        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.nupkg");
        await using (var tempFile = File.Create(tempPath))
            await response.Content.CopyToAsync(tempFile, cancellationToken);

        try
        {
            return await InspectFileAsync(tempPath, cancellationToken, moduleSpec);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static ModuleInstallResult InstallFromFile(string filePath, string pluginsPath, bool allowUnsigned, Func<string, string?>? secretPrompt = null)
    {
        var extension = Path.GetExtension(filePath);
        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryValidateModuleAssembly(filePath, out var validationError))
                return ModuleInstallResult.FromFailure(validationError);
            var fileDirectory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(fileDirectory))
                return ModuleInstallResult.FromFailure($"Module install failed: could not resolve directory for '{Path.GetFileName(filePath)}'.");
            if (!TryLoadManifest(fileDirectory, out var manifest, out var manifestError))
                return ModuleInstallResult.FromFailure(manifestError);
            if (manifest is not null && !TryResolveRequiredSecrets(manifest, secretPrompt, out var secretError))
                return ModuleInstallResult.FromFailure(secretError);
            if (!allowUnsigned && !IsSignedAssembly(filePath))
                return ModuleInstallResult.FromFailure($"Module install failed: '{Path.GetFileName(filePath)}' is unsigned. Re-run with --allow-unsigned to override.");

            var destination = Path.Combine(pluginsPath, Path.GetFileName(filePath));
            File.Copy(filePath, destination, overwrite: true);
            return ModuleInstallResult.FromSuccess($"Installed module DLL: {Path.GetFileName(filePath)}");
        }

        if (extension.Equals(".nupkg", StringComparison.OrdinalIgnoreCase))
            return InstallFromNupkg(filePath, pluginsPath, allowUnsigned: allowUnsigned, secretPrompt: secretPrompt);

        return ModuleInstallResult.FromFailure("Module install failed: only .dll and .nupkg files are supported.");
    }

    private static ModuleInstallResult InstallFromNupkg(string nupkgPath, string pluginsPath, string? packageId = null, bool allowUnsigned = false, Func<string, string?>? secretPrompt = null)
    {
        using var archive = ZipFile.OpenRead(nupkgPath);
        if (!TryLoadManifest(archive, out var manifest, out var manifestError))
            return ModuleInstallResult.FromFailure(manifestError);
        if (manifest is not null && !TryResolveRequiredSecrets(manifest, secretPrompt, out var secretError))
            return ModuleInstallResult.FromFailure(secretError);
        var copiedFiles = new List<string>();
        var hasUtilityAiModule = false;

        foreach (var entry in archive.Entries.Where(IsPluginAssemblyEntry))
        {
            if (!IsCompatibleTargetFramework(entry.FullName))
                continue;

            var fileName = Path.GetFileName(entry.Name);
            if (string.IsNullOrWhiteSpace(fileName))
                continue;

            var destination = Path.Combine(pluginsPath, fileName);
            using (var source = entry.Open())
            using (var target = File.Create(destination))
            {
                source.CopyTo(target);
            }
            copiedFiles.Add(destination);
            if (!hasUtilityAiModule && TryValidateModuleAssembly(destination, out _))
                hasUtilityAiModule = true;
            if (!allowUnsigned && !IsSignedAssembly(destination))
            {
                foreach (var file in copiedFiles.Where(File.Exists))
                    File.Delete(file);
                return ModuleInstallResult.FromFailure($"Module install failed: '{fileName}' is unsigned. Re-run with --allow-unsigned to override.");
            }
        }

        if (copiedFiles.Count == 0)
            return ModuleInstallResult.FromFailure($"Module install failed: package '{packageId ?? Path.GetFileName(nupkgPath)}' does not contain compatible .dll files in lib/ or runtimes/*/lib/.");

        if (!hasUtilityAiModule)
        {
            foreach (var file in copiedFiles.Where(File.Exists))
                File.Delete(file);

            return ModuleInstallResult.FromFailure($"Module install failed: package '{packageId ?? Path.GetFileName(nupkgPath)}' does not contain a compatible UtilityAI module assembly.");
        }

        return ModuleInstallResult.FromSuccess($"Installed {copiedFiles.Count} module assembly file(s) from '{packageId ?? Path.GetFileName(nupkgPath)}'.");
    }

    private static bool TryResolveRequiredSecrets(
        ModulePermissionManifest manifest,
        Func<string, string?>? secretPrompt,
        out string error)
    {
        foreach (var secretName in manifest.RequiredSecrets ?? [])
        {
            if (string.IsNullOrWhiteSpace(secretName))
                continue;

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(secretName)))
                continue;

            if (secretPrompt is null)
            {
                error = $"Module install failed: required secret '{secretName}' is missing. Set it as an environment variable before installation.";
                return false;
            }

            var provided = secretPrompt(secretName);
            if (string.IsNullOrWhiteSpace(provided))
            {
                error = $"Module install failed: required secret '{secretName}' was not provided.";
                return false;
            }

            Environment.SetEnvironmentVariable(secretName, provided);
        }

        error = string.Empty;
        return true;
    }

    private static bool IsPluginAssemblyEntry(ZipArchiveEntry entry)
    {
        if (!entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return false;

        var normalized = entry.FullName.Replace('\\', '/');
        return normalized.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
            || (normalized.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase)
                && normalized.Contains("/lib/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryValidateModuleAssembly(string assemblyPath, out string error)
    {
        var loadContext = new AssemblyLoadContext($"Compass.ModuleValidation.{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            // Load from a memory stream to avoid holding a file lock on Windows.
            var bytes = File.ReadAllBytes(assemblyPath);
            using var ms = new MemoryStream(bytes);
            var assembly = loadContext.LoadFromStream(ms);
            var hasModule = assembly.GetExportedTypes().Any(IsUtilityAiModuleType);
            if (hasModule)
            {
                error = string.Empty;
                return true;
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            if (ex.Types.OfType<Type>().Any(IsUtilityAiModuleType))
            {
                error = string.Empty;
                return true;
            }
        }
        catch (FileNotFoundException)
        {
            // handled below
        }
        catch (FileLoadException)
        {
            // handled below
        }
        catch (BadImageFormatException)
        {
            // handled below
        }
        catch (NotSupportedException)
        {
            // handled below
        }
        finally
        {
            loadContext.Unload();
        }

        error = $"Module install failed: '{Path.GetFileName(assemblyPath)}' is not a compatible UtilityAI module assembly.";
        return false;
    }

    private static bool IsCompatibleTargetFramework(string entryPath)
    {
        var normalized = entryPath.Replace('\\', '/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var tfm = parts.Length >= 3 && parts[0].Equals("lib", StringComparison.OrdinalIgnoreCase)
            ? parts[1]
            : parts.Length >= 5
                && parts[0].Equals("runtimes", StringComparison.OrdinalIgnoreCase)
                && parts[2].Equals("lib", StringComparison.OrdinalIgnoreCase)
                ? parts[3]
                : null;

        if (string.IsNullOrWhiteSpace(tfm))
            return false;

        if (tfm.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            return false;

        var tfmBody = tfm[3..];
        var versionText = new string(tfmBody.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        if (versionText.Length == 0 || !Version.TryParse(versionText, out var version))
            return false;

        if (tfmBody.StartsWith($"{versionText}-", StringComparison.OrdinalIgnoreCase)
            && !IsCompatiblePlatformTfm(tfmBody[(versionText.Length + 1)..]))
            return false;

        return version.Major <= Environment.Version.Major;
    }

    private static bool IsCompatiblePlatformTfm(string platformPart)
    {
        if (platformPart.StartsWith("windows", StringComparison.OrdinalIgnoreCase))
            return OperatingSystem.IsWindows();
        if (platformPart.StartsWith("linux", StringComparison.OrdinalIgnoreCase))
            return OperatingSystem.IsLinux();
        if (platformPart.StartsWith("osx", StringComparison.OrdinalIgnoreCase))
            return OperatingSystem.IsMacOS();

        return true;
    }

    private static bool IsUtilityAiModuleType(Type type)
    {
        if (!type.IsClass || type.IsAbstract)
            return false;

        // Check if it implements ICompassModule
        return type.GetInterfaces()
            .Any(i => i.Name == "ICompassModule");
    }

    private static bool IsSignedAssembly(string assemblyPath)
    {
        try
        {
            var name = AssemblyName.GetAssemblyName(assemblyPath);
            return name.GetPublicKeyToken() is { Length: > 0 };
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLoadManifest(string directoryPath, out ModulePermissionManifest? manifest, out string error)
    {
        var manifestPath = Path.Combine(directoryPath, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            manifest = null;
            error = $"Module install failed: missing required manifest '{ManifestFileName}'.";
            return false;
        }

        try
        {
            using var stream = File.OpenRead(manifestPath);
            var parsed = JsonSerializer.Deserialize<ModulePermissionManifest>(stream, JsonDefaults);
            if (parsed is null)
            {
                manifest = null;
                error = $"Module install failed: manifest '{ManifestFileName}' is empty or invalid.";
                return false;
            }
            if (parsed.Capabilities.Count == 0)
            {
                manifest = null;
                error = $"Module install failed: manifest '{ManifestFileName}' must declare at least one capability.";
                return false;
            }

            manifest = parsed;
            error = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            manifest = null;
            error = $"Module install failed: invalid JSON in '{ManifestFileName}'.";
            return false;
        }
    }

    private static bool TryLoadManifest(ZipArchive archive, out ModulePermissionManifest? manifest, out string error)
    {
        var entry = archive.Entries.FirstOrDefault(e =>
            e.FullName.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            manifest = null;
            error = $"Module install failed: package is missing required manifest '{ManifestFileName}'.";
            return false;
        }

        try
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, leaveOpen: false);
            var parsed = JsonSerializer.Deserialize<ModulePermissionManifest>(reader.ReadToEnd(), JsonDefaults);
            if (parsed is null)
            {
                manifest = null;
                error = $"Module install failed: package manifest '{ManifestFileName}' is empty or invalid.";
                return false;
            }
            if (parsed.Capabilities.Count == 0)
            {
                manifest = null;
                error = $"Module install failed: package manifest '{ManifestFileName}' must declare at least one capability.";
                return false;
            }

            manifest = parsed;
            error = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            manifest = null;
            error = $"Module install failed: package contains invalid JSON in '{ManifestFileName}'.";
            return false;
        }
    }

    private static readonly JsonSerializerOptions JsonDefaults = new(JsonSerializerDefaults.Web);

    private static async Task<ModuleInspectionReport> InspectFileAsync(string filePath, CancellationToken cancellationToken, string? displayName = null)
    {
        var extension = Path.GetExtension(filePath);
        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var findings = new List<string>();
            var hasModule = TryValidateModuleAssembly(filePath, out var error);
            if (!hasModule)
                findings.Add(error);
            var signed = IsSignedAssembly(filePath);
            if (!signed)
                findings.Add("Assembly is unsigned.");

            ModulePermissionManifest? manifest = null;
            var fileDirectory = Path.GetDirectoryName(filePath);
            var hasManifest = !string.IsNullOrWhiteSpace(fileDirectory)
                && TryLoadManifest(fileDirectory, out manifest, out _);
            if (!hasManifest)
                findings.Add($"Missing required manifest '{ManifestFileName}'.");

            return new ModuleInspectionReport(
                displayName ?? filePath,
                hasModule,
                signed,
                hasManifest,
                manifest?.Capabilities ?? [],
                manifest?.Permissions ?? [],
                findings,
                $"Module inspection complete for {Path.GetFileName(filePath)}.");
        }

        if (extension.Equals(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(filePath);
            cancellationToken.ThrowIfCancellationRequested();
            var findings = new List<string>();
            var compatibleEntries = archive.Entries.Where(IsPluginAssemblyEntry).Where(e => IsCompatibleTargetFramework(e.FullName)).ToList();
            var hasModule = false;
            var signed = true;

            foreach (var entry in compatibleEntries)
            {
                var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dll");
                try
                {
                    await using (var source = entry.Open())
                    await using (var target = File.Create(tempPath))
                        await source.CopyToAsync(target, cancellationToken);

                    hasModule |= TryValidateModuleAssembly(tempPath, out _);
                    signed &= IsSignedAssembly(tempPath);
                }
                finally
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
            }

            if (!hasModule)
                findings.Add("No compatible UtilityAI module assembly found.");
            if (!signed)
                findings.Add("One or more assemblies are unsigned.");

            var hasManifest = TryLoadManifest(archive, out var manifest, out _);
            if (!hasManifest)
                findings.Add($"Missing required manifest '{ManifestFileName}'.");

            return new ModuleInspectionReport(
                displayName ?? filePath,
                hasModule,
                signed,
                hasManifest,
                manifest?.Capabilities ?? [],
                manifest?.Permissions ?? [],
                findings,
                $"Module inspection complete for {Path.GetFileName(filePath)}.");
        }

        return ModuleInspectionReport.Error(displayName ?? filePath, "Inspection failed: only .dll and .nupkg files are supported.");
    }

    public sealed record ModuleInspectionReport(
        string Module,
        bool HasUtilityAiModule,
        bool IsSigned,
        bool HasManifest,
        IReadOnlyList<string> Capabilities,
        IReadOnlyList<string> Permissions,
        IReadOnlyList<string> Findings,
        string Summary)
    {
        public static ModuleInspectionReport Error(string module, string summary) =>
            new(module, false, false, false, [], [], [summary], summary);
    }

    private sealed record ModulePermissionManifest(
        string Publisher,
        string Version,
        IReadOnlyList<string> Capabilities,
        IReadOnlyList<string> Permissions,
        string SideEffectLevel,
        string? IntegrityHash = null,
        IReadOnlyList<string>? NetworkEgressDomains = null,
        IReadOnlyList<string>? FileAccessScopes = null,
        IReadOnlyList<string>? RequiredSecrets = null);


    private static string SanitizeIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(ch);
            else
                builder.Append('_');
        }

        var result = builder.ToString().Trim('_');
        if (result.Length == 0)
            result = "CompassModule";

        while (result.Contains("__", StringComparison.Ordinal))
            result = result.Replace("__", "_", StringComparison.Ordinal);

        if (char.IsDigit(result[0]))
            result = $"_{result}";

        return result;
    }

    private static string SanitizeDomainId(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
            else if (ch is '-' or '_' or '.' or ' ')
                builder.Append('-');
        }

        var result = builder.ToString().Trim('-');
        if (result.Length == 0)
            result = "module";

        while (result.Contains("--", StringComparison.Ordinal))
            result = result.Replace("--", "-", StringComparison.Ordinal);

        if (char.IsDigit(result[0]))
            result = $"module-{result}";

        return result;
    }
}
