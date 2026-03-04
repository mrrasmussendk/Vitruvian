using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using VitruvianPluginSdk.Attributes;

namespace VitruvianCli;

public static class ModuleInstaller
{
    private static readonly HttpClient _httpClient = new();
    private const string ManifestFileName = "vitruvian-manifest.json";
    private const string LegacyManifestFileName = "Vitruvian-manifest.json";
    private const string SupportedModuleTargetFrameworks = "net8.0;net9.0;net10.0";

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
        var trimmedInput = input.TrimStart();
        if (!trimmedInput.StartsWith(command, StringComparison.OrdinalIgnoreCase))
            return false;
        if (trimmedInput.Length > command.Length && !char.IsWhiteSpace(trimmedInput[command.Length]))
            return false;

        var args = trimmedInput[command.Length..]
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
        var trimmedInput = input.TrimStart();
        if (!trimmedInput.StartsWith(command, StringComparison.OrdinalIgnoreCase))
            return false;
        if (trimmedInput.Length > command.Length && !char.IsWhiteSpace(trimmedInput[command.Length]))
            return false;

        var args = trimmedInput[command.Length..].Trim();
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
        var manifestPath = Path.Combine(moduleDirectory, ManifestFileName);
        var readmePath = Path.Combine(moduleDirectory, "README.md");
        var scaffoldTargetFramework = GetCurrentTargetFrameworkMoniker();

        File.WriteAllText(projectPath,
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <TargetFrameworks>{{SupportedModuleTargetFrameworks}}</TargetFrameworks>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <Version>1.0.0</Version>
                <Authors>YourName</Authors>
                <Description>Vitruvian module created with /new-module</Description>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="UtilityAi" Version="1.6.5" />
                <PackageReference Include="Vitruvian.Abstractions" Version="0.*" />
                <PackageReference Include="Vitruvian.PluginSdk" Version="0.*" />
              </ItemGroup>

              <ItemGroup>
                <None Include="{{ManifestFileName}}" CopyToOutputDirectory="PreserveNewest" />
              </ItemGroup>

            </Project>
            """);

        File.WriteAllText(classPath,
            $$"""
            using VitruvianAbstractions.Interfaces;
            using VitruvianAbstractions;
            using VitruvianPluginSdk.Attributes;

            namespace {{moduleNamespace}};

            /// <summary>
            /// Example module created by vitruvian --new-module.
            /// Implements IVitruvianModule for simplified LLM-based routing.
            /// </summary>
            [RequiresPermission(ModuleAccess.Read)]
            public sealed class {{className}} : IVitruvianModule
            {
                private readonly IModelClient? _modelClient;

                /// <summary>
                /// Unique identifier for this module. The GOAP planner uses this for routing.
                /// </summary>
                public string Domain => "{{domainId}}";

                /// <summary>
                /// Natural language description used by the planner and router to decide
                /// when to invoke this module. Be specific about what this module does.
                /// </summary>
                public string Description => "Example module - TODO: describe what this module does";

                public {{className}}(IModelClient? modelClient = null)
                {
                    _modelClient = modelClient;
                }

                /// <summary>
                /// Executes the module's capability based on the user request.
                /// </summary>
                /// <param name="request">The natural language request from the user or planner.</param>
                /// <param name="userId">Optional user identifier for permission checks.</param>
                /// <param name="ct">Cancellation token for async operations.</param>
                /// <returns>A user-friendly response string describing the result.</returns>
                public async Task<string> ExecuteAsync(string request, string? userId, CancellationToken ct)
                {
                    // TODO: Implement your module logic here

                    // Example: Return an error if no model is configured
                    if (_modelClient is null)
                        return "Error: No model client configured for this module.";

                    try
                    {
                        // Example: Use the model client to process the request
                        var response = await _modelClient.GenerateAsync(
                            $"Handle the following request: {request}",
                            ct);

                        return response;
                    }
                    catch (Exception ex)
                    {
                        // Return user-friendly error messages
                        return $"Error processing request: {ex.Message}";
                    }
                }
            }
            """);

        File.WriteAllText(manifestPath,
            $$"""
            {
              "Publisher": "YourName",
              "Version": "1.0.0",
              "Capabilities": [
                "{{domainId}}"
              ],
              "Permissions": [
                "read"
              ],
              "SideEffectLevel": "readonly",
              "NetworkEgressDomains": [],
              "FileAccessScopes": [],
              "RequiredSecrets": []
            }
            """);

        File.WriteAllText(readmePath,
            $$"""
            # {{moduleName}}

            A Vitruvian module created with `/new-module`.

            ## What This Module Does

            TODO: Describe your module's functionality here.

            ## Building

            Build the module:

            ```bash
            dotnet build
            ```

            ## Testing Locally

            ### Option 1: Install the DLL

            ```bash
            # Build in Release mode
            dotnet build -c Release

            # Install the module (adjust path to your Vitruvian installation)
            vitruvian --install-module ./bin/Release/{{scaffoldTargetFramework}}/{{moduleName}}.dll --allow-unsigned
            ```

            ### Option 2: Copy to plugins folder

            ```bash
            # Build the module
            dotnet build

            # Copy DLL and manifest to Vitruvian plugins directory
            cp bin/Debug/{{scaffoldTargetFramework}}/{{moduleName}}.dll /path/to/vitruvian/plugins/
            cp vitruvian-manifest.json /path/to/vitruvian/plugins/
            ```

            ## Deployment

            ### Package for Distribution

            Create a NuGet package:

            ```bash
            dotnet pack -c Release
            ```

            The `.nupkg` file will be in `bin/Release/`.

            ### Sign the Assembly (Recommended)

            For production use, sign your assembly to avoid the `--allow-unsigned` flag:

            1. Generate a strong name key:
               ```bash
               sn -k {{moduleName}}.snk
               ```

            2. Add to your `.csproj`:
               ```xml
               <PropertyGroup>
                 <SignAssembly>true</SignAssembly>
                 <AssemblyOriginatorKeyFile>{{moduleName}}.snk</AssemblyOriginatorKeyFile>
               </PropertyGroup>
               ```

            3. Rebuild and the assembly will be signed.

            ## Customization

            ### Required Changes

            1. **Update `Description`** in `{{className}}.cs` to accurately describe what your module does
            2. **Implement `ExecuteAsync`** with your actual module logic
            3. **Update `vitruvian-manifest.json`**:
               - Set your publisher name
               - Update permissions to match what your module needs
               - Set `SideEffectLevel` (`readonly`, `low`, `medium`, `high`)

            ### Permission Levels

            If your module needs more than read access, update the `[RequiresPermission]` attribute:

            ```csharp
            [RequiresPermission(ModuleAccess.Read | ModuleAccess.Write)]
            ```

            And update `vitruvian-manifest.json`:

            ```json
            "Permissions": ["read", "write"],
            "SideEffectLevel": "medium"
            ```

             ### Adding Secrets

             If your module needs API keys or credentials:

             1. Add one or more attributes to your module class:
                ```csharp
                [RequiresApiKey("MY_API_KEY")]
                [RequiresApiKey("MY_SECOND_API_KEY")]
                ```

             2. Optionally document the same keys in `vitruvian-manifest.json`:
                ```json
                "RequiredSecrets": ["MY_API_KEY"]
                ```

             3. Users will be prompted during installation to provide any missing keys.

            ## Testing

            Create unit tests in a separate test project:

            ```bash
            dotnet new xunit -n {{moduleName}}.Tests
            cd {{moduleName}}.Tests
            dotnet add reference ../{{moduleName}}.csproj
            dotnet add package Moq
            ```

            ## Documentation

            For more details on module development, see:
            - [Vitruvian README](https://github.com/mrrasmussendk/Vitruvian)
            - [EXTENDING.md](https://github.com/mrrasmussendk/Vitruvian/blob/main/docs/EXTENDING.md)
            - [SECURITY.md](https://github.com/mrrasmussendk/Vitruvian/blob/main/docs/SECURITY.md)
            """);

        return $"Created module scaffold at '{moduleDirectory}' with manifest and README. Build with 'dotnet build', then install with 'vitruvian --install-module ./bin/Debug/{scaffoldTargetFramework}/{moduleName}.dll --allow-unsigned'.";
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
            var requiredSecrets = GetRequiredSecrets(filePath, manifest);
            if (!TryResolveRequiredSecrets(requiredSecrets, secretPrompt, out var secretError))
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

        var requiredSecrets = GetRequiredSecrets(copiedFiles, manifest);
        if (!TryResolveRequiredSecrets(requiredSecrets, secretPrompt, out var secretError))
        {
            foreach (var file in copiedFiles.Where(File.Exists))
                File.Delete(file);
            return ModuleInstallResult.FromFailure(secretError);
        }

        return ModuleInstallResult.FromSuccess($"Installed {copiedFiles.Count} module assembly file(s) from '{packageId ?? Path.GetFileName(nupkgPath)}'.");
    }

    private static bool TryResolveRequiredSecrets(
        IReadOnlyList<string> requiredSecrets,
        Func<string, string?>? secretPrompt,
        out string error)
    {
        foreach (var secretName in requiredSecrets)
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

            // Persist the secret to .env.Vitruvian so it survives process restarts.
            try { EnvFileLoader.PersistSecret(secretName, provided); }
            catch { /* best-effort — the process env var is already set */ }
        }

        error = string.Empty;
        return true;
    }

    private static IReadOnlyList<string> GetRequiredSecrets(string assemblyPath, ModulePermissionManifest? manifest)
    {
        var requiredSecrets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddManifestSecrets(requiredSecrets, manifest);
        AddAssemblySecrets(requiredSecrets, assemblyPath);
        return requiredSecrets.ToArray();
    }

    private static IReadOnlyList<string> GetRequiredSecrets(IEnumerable<string> assemblyPaths, ModulePermissionManifest? manifest)
    {
        var requiredSecrets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddManifestSecrets(requiredSecrets, manifest);
        foreach (var assemblyPath in assemblyPaths)
            AddAssemblySecrets(requiredSecrets, assemblyPath);
        return requiredSecrets.ToArray();
    }

    private static void AddManifestSecrets(HashSet<string> requiredSecrets, ModulePermissionManifest? manifest)
    {
        foreach (var secretName in manifest?.RequiredSecrets ?? [])
        {
            if (!string.IsNullOrWhiteSpace(secretName))
                requiredSecrets.Add(secretName.Trim());
        }
    }

    private static void AddAssemblySecrets(HashSet<string> requiredSecrets, string assemblyPath)
    {
        foreach (var secretName in GetRequiredApiKeysFromAssembly(assemblyPath))
            requiredSecrets.Add(secretName);
    }

    private static IReadOnlyList<string> GetRequiredApiKeysFromAssembly(string assemblyPath)
    {
        var loadContext = new AssemblyLoadContext($"VitruvianModuleSecrets.{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var bytes = File.ReadAllBytes(assemblyPath);
            using var ms = new MemoryStream(bytes);
            var assembly = loadContext.LoadFromStream(ms);
            return GetRequiredApiKeysFromTypes(assembly.GetExportedTypes());
        }
        catch (ReflectionTypeLoadException ex)
        {
            return GetRequiredApiKeysFromTypes(ex.Types.OfType<Type>());
        }
        catch
        {
            return [];
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static IReadOnlyList<string> GetRequiredApiKeysFromTypes(IEnumerable<Type> types)
        => types
            .Where(IsUtilityAiModuleType)
            .SelectMany(static type => type.GetCustomAttributes<RequiresApiKeyAttribute>(inherit: true))
            .Select(static attr => attr.EnvironmentVariable.Trim())
            .Where(static envVar => envVar.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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
        string? loadFailureReason = null;
        var loadContext = new AssemblyLoadContext($"VitruvianModuleValidation.{Guid.NewGuid():N}", isCollectible: true);
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

            loadFailureReason = string.Join("; ", ex.LoaderExceptions
                .OfType<Exception>()
                .Select(static loaderException => loaderException.Message.Trim())
                .Where(static message => message.Length > 0));
        }
        catch (FileNotFoundException ex)
        {
            loadFailureReason = ex.Message;
        }
        catch (FileLoadException ex)
        {
            loadFailureReason = ex.Message;
        }
        catch (BadImageFormatException ex)
        {
            loadFailureReason = ex.Message;
        }
        catch (NotSupportedException ex)
        {
            loadFailureReason = ex.Message;
        }
        finally
        {
            loadContext.Unload();
        }

        var guidance = "Ensure it targets a framework compatible with this Vitruvian CLI and includes a public non-abstract class implementing IVitruvianModule.";
        if (string.IsNullOrWhiteSpace(loadFailureReason))
        {
            error = $"Module install failed: '{Path.GetFileName(assemblyPath)}' is not a compatible Vitruvian module assembly. {guidance}";
            return false;
        }

        error = $"Module install failed: '{Path.GetFileName(assemblyPath)}' is not a compatible Vitruvian module assembly. {guidance} Load error: {loadFailureReason}";
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

    private static string GetCurrentTargetFrameworkMoniker()
    {
        const string frameworkNamePrefix = ".NETCoreApp,Version=v";
        var frameworkName = AppContext.TargetFrameworkName;
        if (frameworkName?.StartsWith(frameworkNamePrefix, StringComparison.OrdinalIgnoreCase) is true)
            return $"net{frameworkName[frameworkNamePrefix.Length..]}";

        return $"net{Environment.Version.Major}.0";
    }

    private static bool IsUtilityAiModuleType(Type type)
    {
        if (!type.IsClass || type.IsAbstract)
            return false;

        // Check if it implements IVitruvianModule
        return type.GetInterfaces()
            .Any(i => i.Name == "IVitruvianModule");
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
        var manifestPath = GetManifestPath(directoryPath);
        if (!File.Exists(manifestPath))
        {
            manifest = null;
            error = $"Module install failed: missing required manifest (expected '{ManifestFileName}' or '{LegacyManifestFileName}').";
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
            e.FullName.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase)
            || e.FullName.Equals(LegacyManifestFileName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            manifest = null;
            error = $"Module install failed: package is missing required manifest (expected '{ManifestFileName}' or '{LegacyManifestFileName}').";
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

    private static string GetManifestPath(string directoryPath)
    {
        var vitruvianManifestPath = Path.Combine(directoryPath, ManifestFileName);
        if (File.Exists(vitruvianManifestPath))
            return vitruvianManifestPath;

        return Path.Combine(directoryPath, LegacyManifestFileName);
    }

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
            result = "VitruvianModule";

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
