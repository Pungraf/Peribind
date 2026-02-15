using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace PeribindLauncher;

internal static class Program
{
    private const string ConfigFileName = "launcher.config.json";
    private const string LocalVersionFile = "version.txt";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static async Task<int> Main()
    {
        try
        {
            var appDir = AppContext.BaseDirectory;
            var configPath = Path.Combine(appDir, ConfigFileName);
            var config = await LoadConfigAsync(configPath);

            var installDir = ResolveInstallDir(appDir, config.InstallDirectory);
            Directory.CreateDirectory(installDir);

            Console.WriteLine($"[Launcher] Install dir: {installDir}");
            Console.WriteLine($"[Launcher] Registry: {config.RegistryBaseUrl}");

            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(Math.Clamp(config.HttpTimeoutSeconds, 10, 180))
            };

            var latest = await FetchLatestReleaseAsync(http, config);
            if (latest == null)
            {
                Console.WriteLine("[Launcher] No release found. Starting installed game.");
                return StartInstalledGame(installDir, config.GameExeRelativePath);
            }

            var localVersionPath = Path.Combine(installDir, LocalVersionFile);
            var localVersion = ReadLocalVersion(localVersionPath);
            Console.WriteLine($"[Launcher] Local version: {localVersion}");
            Console.WriteLine($"[Launcher] Remote version: {latest.Version} (min: {latest.MinSupportedVersion})");

            var mustUpdate = !string.Equals(localVersion, latest.Version, StringComparison.OrdinalIgnoreCase);
            if (mustUpdate)
            {
                EnsureSecureReleaseForUpdate(latest);
                Console.WriteLine("[Launcher] Update required. Downloading release...");
                await DownloadAndInstallAsync(http, latest, installDir);
                await File.WriteAllTextAsync(localVersionPath, latest.Version + Environment.NewLine);
                Console.WriteLine("[Launcher] Update installed.");
            }

            return StartInstalledGame(installDir, config.GameExeRelativePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Launcher] Fatal error: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveInstallDir(string appDir, string? configuredInstallDir)
    {
        if (string.IsNullOrWhiteSpace(configuredInstallDir))
        {
            return Path.GetFullPath(Path.Combine(appDir, "game"));
        }

        return Path.GetFullPath(
            Path.IsPathRooted(configuredInstallDir)
                ? configuredInstallDir
                : Path.Combine(appDir, configuredInstallDir));
    }

    private static async Task<LauncherConfig> LoadConfigAsync(string configPath)
    {
        if (File.Exists(configPath))
        {
            var text = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<LauncherConfig>(text, JsonOptions);
            if (config == null)
            {
                throw new InvalidOperationException("launcher.config.json is invalid.");
            }

            config.Validate();
            return config;
        }

        var template = LauncherConfig.CreateDefault();
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(template, JsonOptions));
        throw new InvalidOperationException(
            $"Missing {ConfigFileName}. A template was created next to launcher. Fill values and run again.");
    }

    private static async Task<ReleaseInfo?> FetchLatestReleaseAsync(HttpClient http, LauncherConfig config)
    {
        var baseUrl = config.RegistryBaseUrl.TrimEnd('/');
        var url =
            $"{baseUrl}/release/latest?channel={Uri.EscapeDataString(config.Channel)}&platform={Uri.EscapeDataString(config.Platform)}";

        using var response = await http.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync<ReleaseInfo>(JsonOptions);
        if (release == null || string.IsNullOrWhiteSpace(release.DownloadUrl) || string.IsNullOrWhiteSpace(release.Version))
        {
            throw new InvalidOperationException("release/latest response is invalid.");
        }

        return release;
    }

    private static string ReadLocalVersion(string localVersionPath)
    {
        if (!File.Exists(localVersionPath))
        {
            return "none";
        }

        return (File.ReadAllText(localVersionPath) ?? string.Empty).Trim();
    }

    private static async Task DownloadAndInstallAsync(HttpClient http, ReleaseInfo release, string installDir)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PeribindLauncher", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var archivePath = Path.Combine(tempRoot, "package.zip");
        var extractPath = Path.Combine(tempRoot, "extract");
        Directory.CreateDirectory(extractPath);

        try
        {
            using (var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var downloadStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = File.Create(archivePath);
                await downloadStream.CopyToAsync(fileStream);
            }

            var actualHash = ComputeSha256Hex(archivePath);
            if (!actualHash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Downloaded file hash mismatch.");
            }

            ZipFile.ExtractToDirectory(archivePath, extractPath, overwriteFiles: true);

            var normalizedExtractPath = NormalizeExtractRoot(extractPath);
            ReplaceInstallDirectory(normalizedExtractPath, installDir);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static string NormalizeExtractRoot(string extractPath)
    {
        var files = Directory.GetFiles(extractPath);
        var directories = Directory.GetDirectories(extractPath);

        if (files.Length == 0 && directories.Length == 1)
        {
            return directories[0];
        }

        return extractPath;
    }

    private static void ReplaceInstallDirectory(string sourceDir, string installDir)
    {
        var backupDir = installDir + "_backup";
        TryDeleteDirectory(backupDir);

        if (Directory.Exists(installDir))
        {
            Directory.Move(installDir, backupDir);
        }

        Directory.CreateDirectory(installDir);
        CopyDirectory(sourceDir, installDir);
        TryDeleteDirectory(backupDir);
    }

    private static int StartInstalledGame(string installDir, string gameExeRelativePath)
    {
        var gamePath = Path.Combine(installDir, gameExeRelativePath);
        if (!File.Exists(gamePath))
        {
            Console.Error.WriteLine($"[Launcher] Game executable not found: {gamePath}");
            return 2;
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = gamePath,
                WorkingDirectory = Path.GetDirectoryName(gamePath) ?? installDir,
                UseShellExecute = true
            }
        };
        process.Start();
        return 0;
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var filePath in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(filePath);
            var destinationPath = Path.Combine(targetDir, fileName);
            File.Copy(filePath, destinationPath, overwrite: true);
        }

        foreach (var directoryPath in Directory.GetDirectories(sourceDir))
        {
            var directoryName = Path.GetFileName(directoryPath);
            var destinationPath = Path.Combine(targetDir, directoryName);
            CopyDirectory(directoryPath, destinationPath);
        }
    }

    private static string ComputeSha256Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void EnsureSecureReleaseForUpdate(ReleaseInfo release)
    {
        if (!Uri.TryCreate(release.DownloadUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Release downloadUrl must use HTTPS.");
        }

        if (!IsValidSha256Hex(release.Sha256))
        {
            throw new InvalidOperationException("Release sha256 must be a non-empty 64-character hex string.");
        }
    }

    private static bool IsValidSha256Hex(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
        {
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var isHex =
                (c >= '0' && c <= '9') ||
                (c >= 'a' && c <= 'f') ||
                (c >= 'A' && c <= 'F');
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // no-op
        }
    }
}

internal sealed class LauncherConfig
{
    public string RegistryBaseUrl { get; set; } = "http://209.38.222.103:8080";
    public string Channel { get; set; } = "stable";
    public string Platform { get; set; } = "win64";
    public string InstallDirectory { get; set; } = "./game";
    public string GameExeRelativePath { get; set; } = "PeribindClient.exe";
    public int HttpTimeoutSeconds { get; set; } = 60;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RegistryBaseUrl))
        {
            throw new InvalidOperationException("RegistryBaseUrl is required.");
        }

        if (string.IsNullOrWhiteSpace(Channel))
        {
            throw new InvalidOperationException("Channel is required.");
        }

        if (string.IsNullOrWhiteSpace(Platform))
        {
            throw new InvalidOperationException("Platform is required.");
        }

        if (string.IsNullOrWhiteSpace(GameExeRelativePath))
        {
            throw new InvalidOperationException("GameExeRelativePath is required.");
        }
    }

    public static LauncherConfig CreateDefault() => new();
}

internal sealed class ReleaseInfo
{
    public string Version { get; set; } = string.Empty;
    public string MinSupportedVersion { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}
