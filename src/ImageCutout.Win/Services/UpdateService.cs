using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ImageCutout.Win.Services;

/// <summary>
/// Reads a configured HTTPS mirror or public GitHub Release and verifies its detached, RSA-SHA256-signed manifest.
/// The publisher's public key is distributed with the installed application.
/// An unsigned or malformed release can never be installed.
/// </summary>
public sealed class UpdateService : IDisposable
{
    internal const string ArchiveName = "ImageCutout-win-x64-Bundled.zip";
    internal const string ManifestName = "update-manifest.json";
    internal const string SignatureName = "update-manifest.sig";
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = true });
    private readonly UpdateConfiguration _configuration;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private UpdateService(UpdateConfiguration config)
    {
        _configuration = config;
        _http.Timeout = TimeSpan.FromMinutes(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ImageCutout-Updater/0.1.0");

    }

    public static UpdateService? CreateIfConfigured()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "update-config.json");
        if (!File.Exists(path)) return null;
        var config = JsonSerializer.Deserialize<UpdateConfiguration>(File.ReadAllText(path), JsonOptions);
        if (config is null || (string.IsNullOrWhiteSpace(config.Repository) && config.Mirrors.Length == 0) ||
            string.IsNullOrWhiteSpace(config.PublicKeyPem)) return null;
        if (!string.IsNullOrWhiteSpace(config.Repository) && !Regex.IsMatch(config.Repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))
            throw new InvalidDataException("更新源的仓库格式必须为 OWNER/REPO。");
        using (var rsa = RSA.Create()) { rsa.ImportFromPem(config.PublicKeyPem); if (rsa.KeySize < 2048) throw new InvalidDataException("RSA 公钥至少 2048 位"); }
        return new UpdateService(config);
    }

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken token = default)
    {
        var sources = _configuration.Mirrors.Select(x => (Url: HttpsBase(x) + "latest/", GitHub: false)).ToList();
        if (!string.IsNullOrWhiteSpace(_configuration.Repository)) sources.Add(("", true));
        Exception? failure = null;
        bool reached = false;
        foreach (var source in sources)
        {
            token.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                string url = source.GitHub ? $"https://github.com/{_configuration.Repository}/releases/latest/download/" : source.Url;
                var update = await CheckSourceAsync(url, source.GitHub, timeout.Token);
                reached = true;
                if (update is not null) return update;
            }
            catch (Exception ex) when (!token.IsCancellationRequested) { failure = ex; }
        }
        token.ThrowIfCancellationRequested();
        if (reached) return null;
        throw new InvalidOperationException("所有更新源均不可用或校验失败。", failure);
    }
    private static string HttpsBase(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException("镜像地址必须为 HTTPS 目录。");
        return uri.AbsoluteUri.TrimEnd('/') + "/";
    }
    private async Task<AvailableUpdate?> CheckSourceAsync(string baseUrl, bool github, CancellationToken token)
    {
        byte[] manifestBytes = await DownloadSmallAsync(baseUrl + ManifestName, 64 * 1024, token);
        byte[] signatureBytes = await DownloadSmallAsync(baseUrl + SignatureName, 8 * 1024, token);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(_configuration.PublicKeyPem);
        if (!rsa.VerifyData(manifestBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new CryptographicException("发布清单数字签名验证失败，更新已阻止。");

        var manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestBytes, JsonOptions)
            ?? throw new InvalidDataException("更新清单内容为空。");
        if (manifest.Repository != _configuration.Repository || manifest.Product != "ImageCutout" ||
            !Regex.IsMatch(manifest.Version, @"^[0-9]+\.[0-9]+\.[0-9]+$") ||
            !Version.TryParse(manifest.Version, out var latest) || manifest.Tag != "v" + manifest.Version || manifest.Archive != ArchiveName ||
            manifest.Size <= 0 || manifest.Size > 2_000_000_000L ||
            !Regex.IsMatch(manifest.Sha256 ?? "", "^[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("签名清单与仓库、版本或发布包不匹配。");

        var current = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 1, 0);
        if (latest <= current) return null;
        string downloadBase = github ? $"https://github.com/{_configuration.Repository}/releases/download/{manifest.Tag}/" :
            baseUrl.Substring(0, baseUrl.Length - 7) + manifest.Tag + "/";
        return new AvailableUpdate(latest, manifest.Tag, downloadBase + ArchiveName,
            manifest.Sha256.ToLowerInvariant(), manifest.Size, downloadBase);
    }

    public async Task<string> DownloadAsync(AvailableUpdate update, IProgress<int> progress, CancellationToken token)
    {
        var sources = new List<string> { update.DownloadUrl };
        sources.AddRange(_configuration.Mirrors.Select(x => HttpsBase(x) + update.Tag + "/" + ArchiveName));
        if (!string.IsNullOrWhiteSpace(_configuration.Repository)) sources.Add($"https://github.com/{_configuration.Repository}/releases/download/{update.Tag}/{ArchiveName}");
        Exception? failure = null;
        foreach (string url in sources.Distinct())
        {
            token.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(30));
            try { return await DownloadFromAsync(update with { DownloadUrl = url }, progress, timeout.Token); }
            catch (Exception ex) when (!token.IsCancellationRequested) { failure = ex; progress.Report(0); }
        }
        token.ThrowIfCancellationRequested();
        throw new IOException("所有更新包下载源均失败。", failure);
    }
    private async Task<string> DownloadFromAsync(AvailableUpdate update, IProgress<int> progress, CancellationToken token)
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCutout", "updates", update.Tag);
        Directory.CreateDirectory(folder);
        string staged = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".part");
        string final = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using var headers = CancellationTokenSource.CreateLinkedTokenSource(token);
            headers.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await _http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, headers.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long announced && announced != update.Size)
                throw new InvalidDataException("下载文件大小与签名清单不一致。");
            await using var input = await response.Content.ReadAsStreamAsync(token);
            await using (var output = new FileStream(staged, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long received = 0;
                var block = new byte[128 * 1024];
                int count;
                while ((count = await input.ReadAsync(block, token)) != 0)
                {
                    received += count;
                    if (received > update.Size) throw new InvalidDataException("更新包超过清单声明大小。");
                    hash.AppendData(block, 0, count);
                    await output.WriteAsync(block.AsMemory(0, count), token);
                    progress.Report((int)Math.Min(100, received * 100 / update.Size));
                }
                await output.FlushAsync(token);
                if (received != update.Size ||
                    !Convert.ToHexString(hash.GetHashAndReset()).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new CryptographicException("更新包 SHA-256 或文件大小校验失败。");
            }
            File.Move(staged, final, true);
            return final;
        }
        catch
        {
            try { File.Delete(staged); } catch (IOException) { }
            throw;
        }
    }

    public static void StartUpdater(string verifiedZip, AvailableUpdate update)
    {
        string installedUpdater = Path.Combine(AppContext.BaseDirectory, "ImageCutout.Updater.exe");
        if (!File.Exists(installedUpdater)) throw new FileNotFoundException("更新器不存在。", installedUpdater);
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCutout", "updater", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string detachedUpdater = Path.Combine(folder, "ImageCutout.Updater.exe");
        File.Copy(installedUpdater, detachedUpdater);
        string token = Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo(detachedUpdater)
        {
            UseShellExecute = false,
            WorkingDirectory = folder
        };
        foreach (string arg in new[] {
            "--install-dir", Path.GetFullPath(AppContext.BaseDirectory),
            "--archive", Path.GetFullPath(verifiedZip), "--sha256", update.Sha256,
            "--parent-pid", Environment.ProcessId.ToString(), "--health-token", token,
            "--version", update.Version.ToString(3)
        }) start.ArgumentList.Add(arg);
        _ = Process.Start(start) ?? throw new InvalidOperationException("无法启动独立更新器。");
    }

    public static void ConfirmHealthyStartup(string[] args)
    {
        int index = Array.IndexOf(args, "--update-health-token");
        if (index < 0 || index + 1 >= args.Length) return;
        string token = args[index + 1];
        if (!Regex.IsMatch(token, "^[a-f0-9]{32}$")) return;
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCutout", "updates");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "health-" + token + ".ok"), "ready", Encoding.UTF8);
    }

    private async Task<byte[]> DownloadSmallAsync(string url, int maxBytes, CancellationToken token)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        return await ReadLimitedAsync(response, maxBytes, token);
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int maxBytes, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new InvalidDataException("远程发布信息超过允许的大小。");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            if (memory.Length + read > maxBytes) throw new InvalidDataException("远程发布信息过大。");
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }

    public void Dispose() => _http.Dispose();
}

public sealed record AvailableUpdate(Version Version, string Tag, string DownloadUrl, string Sha256, long Size, string Repository);
public sealed class UpdateConfiguration
{
    public string Repository { get; set; } = "";
    public string PublicKeyPem { get; set; } = "";
    public string[] Mirrors { get; set; } = [];
}
public sealed class UpdateManifest
{
    public string Product { get; set; } = "";
    public string Repository { get; set; } = "";
    public string Version { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Archive { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
}
