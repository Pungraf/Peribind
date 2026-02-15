using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace PeribindLauncher;

internal sealed class LauncherEngine : IDisposable
{
    private const string ConfigFileName = "launcher.config.json";
    private const string LocalVersionFile = "version.txt";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpClient _httpClient;
    private readonly string _installDir;

    public LauncherConfig Config { get; }

    private LauncherEngine(LauncherConfig config, string installDir, HttpClient httpClient)
    {
        Config = config;
        _installDir = installDir;
        _httpClient = httpClient;
    }

    public static async Task<LauncherEngine> CreateAsync(string appDir)
    {
        var configPath = Path.Combine(appDir, ConfigFileName);
        var config = await LoadConfigAsync(configPath);
        var installDir = ResolveInstallDir(appDir, config.InstallDirectory);
        Directory.CreateDirectory(installDir);

        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(config.HttpTimeoutSeconds, 10, 180))
        };

        return new LauncherEngine(config, installDir, httpClient);
    }

    public async Task<LauncherRunResult> CheckAndUpdateAsync(IProgress<LauncherProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new LauncherProgress("Checking latest release...", null, null, null, null));

        var latest = await FetchLatestReleaseAsync(ct);
        var localVersionPath = Path.Combine(_installDir, LocalVersionFile);
        var localVersion = ReadLocalVersion(localVersionPath);

        if (latest == null)
        {
            if (!HasInstalledGame())
            {
                throw new InvalidOperationException("No release manifest found and no local game install present.");
            }

            return new LauncherRunResult(
                LocalVersion: localVersion,
                RemoteVersion: localVersion,
                Updated: false,
                NotesUrl: string.Empty,
                StatusMessage: "No remote release found. Using installed build.");
        }

        progress?.Report(new LauncherProgress(
            $"Found release {latest.Version}. Local version: {localVersion}.",
            null,
            null,
            null,
            null));

        var mustUpdate = !string.Equals(localVersion, latest.Version, StringComparison.OrdinalIgnoreCase);
        if (mustUpdate)
        {
            EnsureSecureReleaseForUpdate(latest);
            await DownloadAndInstallAsync(latest, _installDir, progress, ct);
            await File.WriteAllTextAsync(localVersionPath, latest.Version + Environment.NewLine, ct);
            localVersion = latest.Version;
        }

        if (!HasInstalledGame())
        {
            throw new InvalidOperationException("Game executable not found after update.");
        }

        return new LauncherRunResult(
            LocalVersion: localVersion,
            RemoteVersion: latest.Version,
            Updated: mustUpdate,
            NotesUrl: latest.NotesUrl ?? string.Empty,
            StatusMessage: mustUpdate ? "Update installed successfully." : "Game is up to date.");
    }

    public bool HasInstalledGame()
    {
        return File.Exists(GetInstalledGamePath());
    }

    public void StartInstalledGame()
    {
        var gamePath = GetInstalledGamePath();
        if (!File.Exists(gamePath))
        {
            throw new InvalidOperationException($"Game executable not found: {gamePath}");
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = gamePath,
                WorkingDirectory = Path.GetDirectoryName(gamePath) ?? _installDir,
                UseShellExecute = true
            }
        };
        process.Start();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private string GetInstalledGamePath()
    {
        return Path.Combine(_installDir, Config.GameExeRelativePath);
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

    private async Task<ReleaseInfo?> FetchLatestReleaseAsync(CancellationToken ct)
    {
        var baseUrl = Config.RegistryBaseUrl.TrimEnd('/');
        var url =
            $"{baseUrl}/release/latest?channel={Uri.EscapeDataString(Config.Channel)}&platform={Uri.EscapeDataString(Config.Platform)}";

        using var response = await _httpClient.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync<ReleaseInfo>(JsonOptions, ct);
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

    private async Task DownloadAndInstallAsync(
        ReleaseInfo release,
        string installDir,
        IProgress<LauncherProgress>? progress,
        CancellationToken ct)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PeribindLauncher", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var archivePath = Path.Combine(tempRoot, "package.zip");
        var extractPath = Path.Combine(tempRoot, "extract");
        Directory.CreateDirectory(extractPath);

        try
        {
            progress?.Report(new LauncherProgress("Downloading update...", 0, 0, release.SizeBytes > 0 ? release.SizeBytes : null, null));
            await DownloadFileWithProgressAsync(release.DownloadUrl, archivePath, progress, ct);

            progress?.Report(new LauncherProgress("Verifying file hash...", null, null, null, null));
            var actualHash = ComputeSha256Hex(archivePath);
            if (!actualHash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Downloaded file hash mismatch.");
            }

            progress?.Report(new LauncherProgress("Extracting package...", null, null, null, null));
            ZipFile.ExtractToDirectory(archivePath, extractPath, overwriteFiles: true);

            progress?.Report(new LauncherProgress("Installing update...", null, null, null, null));
            var normalizedExtractPath = NormalizeExtractRoot(extractPath);
            ReplaceInstallDirectory(normalizedExtractPath, installDir);

            progress?.Report(new LauncherProgress("Update finished.", 100, null, null, null));
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private async Task DownloadFileWithProgressAsync(
        string url,
        string destinationPath,
        IProgress<LauncherProgress>? progress,
        CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;

        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(destinationPath);

        var buffer = new byte[1024 * 64];
        long downloaded = 0;
        var stopwatch = Stopwatch.StartNew();
        var lastUiTickMs = 0L;

        while (true)
        {
            var read = await input.ReadAsync(buffer, ct);
            if (read <= 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;

            var nowMs = stopwatch.ElapsedMilliseconds;
            if (nowMs - lastUiTickMs < 120 && contentLength.HasValue && downloaded < contentLength.Value)
            {
                continue;
            }
            lastUiTickMs = nowMs;

            if (contentLength.HasValue && contentLength.Value > 0L)
            {
                var percent = (int)Math.Clamp((downloaded * 100L) / contentLength.Value, 0L, 100L);
                progress?.Report(new LauncherProgress(
                    $"Downloading update... {percent}%",
                    percent,
                    downloaded,
                    contentLength.Value,
                    CalculateEta(stopwatch.Elapsed, downloaded, contentLength.Value)));
            }
            else
            {
                progress?.Report(new LauncherProgress("Downloading update...", null, downloaded, null, null));
            }
        }
    }

    private static TimeSpan? CalculateEta(TimeSpan elapsed, long downloadedBytes, long totalBytes)
    {
        if (downloadedBytes <= 0 || totalBytes <= 0 || downloadedBytes >= totalBytes)
        {
            return TimeSpan.Zero;
        }

        var elapsedSeconds = elapsed.TotalSeconds;
        if (elapsedSeconds < 0.3)
        {
            return null;
        }

        var bytesPerSecond = downloadedBytes / elapsedSeconds;
        if (bytesPerSecond <= 1.0)
        {
            return null;
        }

        var remainingBytes = totalBytes - downloadedBytes;
        var etaSeconds = remainingBytes / bytesPerSecond;
        if (double.IsNaN(etaSeconds) || double.IsInfinity(etaSeconds) || etaSeconds < 0)
        {
            return null;
        }

        return TimeSpan.FromSeconds(etaSeconds);
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
    public string NotesUrl { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

internal sealed record LauncherProgress(
    string Status,
    int? Percent,
    long? DownloadedBytes,
    long? TotalBytes,
    TimeSpan? Eta);

internal sealed record LauncherRunResult(
    string LocalVersion,
    string RemoteVersion,
    bool Updated,
    string NotesUrl,
    string StatusMessage);
