using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace KomariDeskWidget;

public sealed class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Deng-Amor/vps-desktop-monitor/releases/latest";
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static async Task CheckForUpdatesAsync(Window owner, bool silentWhenLatest)
    {
        try
        {
            var release = await GetLatestReleaseAsync();
            var latestVersion = NormalizeVersion(ParseVersion(release.TagName));
            var currentVersion = NormalizeVersion(CurrentVersion);
            if (latestVersion <= currentVersion)
            {
                if (!silentWhenLatest)
                {
                    MessageBox.Show(owner, $"当前已经是最新版本：v{currentVersion}", "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            var setupAsset = release.Assets.FirstOrDefault(asset => asset.Name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase));
            if (setupAsset is null)
            {
                MessageBox.Show(owner, $"发现新版本 {release.TagName}，但没有找到安装器附件。请前往 GitHub Releases 手动下载。", "检查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                owner,
                $"发现新版本 {release.TagName}。\n\n当前版本：v{CurrentVersion.ToString(3)}\n是否现在下载并启动安装器？\n\n应用会在启动安装器后自动退出，安装器会保留你的 widget.json 配置。",
                "发现新版本",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes) return;

            var installerPath = await DownloadInstallerAsync(setupAsset);
            var installPath = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = $"--silent --launch --installPath \"{installPath}\"",
                UseShellExecute = true
            });

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            if (!silentWhenLatest)
            {
                MessageBox.Show(owner, $"检查更新失败：{ex.Message}", "检查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private static async Task<GitHubRelease> GetLatestReleaseAsync()
    {
        using var response = await Http.GetAsync(LatestReleaseUrl);
        response.EnsureSuccessStatusCode();
        return await JsonSerializer.DeserializeAsync<GitHubRelease>(await response.Content.ReadAsStreamAsync(), JsonOptions)
               ?? throw new InvalidDataException("GitHub 返回了空数据。");
    }

    private static async Task<string> DownloadInstallerAsync(GitHubAsset asset)
    {
        var updateDir = Path.Combine(Path.GetTempPath(), "KomariDeskWidget", "updates");
        Directory.CreateDirectory(updateDir);
        var targetPath = Path.Combine(updateDir, asset.Name);

        using var response = await Http.GetAsync(asset.BrowserDownloadUrl);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.Create(targetPath);
        await input.CopyToAsync(output);

        return targetPath;
    }

    private static Version ParseVersion(string tagName)
    {
        var clean = tagName.Trim().TrimStart('v', 'V');
        return Version.TryParse(clean, out var version) ? version : new Version(0, 0, 0);
    }

    private static Version NormalizeVersion(Version version)
    {
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KomariDeskWidget-Updater");
        return client;
    }
}

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    public List<GitHubAsset> Assets { get; set; } = [];
}

public sealed class GitHubAsset
{
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";
}
