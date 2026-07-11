using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace KomariDeskWidget;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _timer = new();
    private WidgetConfig _config = new();
    private string _footer = "正在连接…";

    public ObservableCollection<ServerCard> Servers { get; } = [];
    public string Footer { get => _footer; set { _footer = value; OnPropertyChanged(); } }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += async (_, _) =>
        {
            _config = LoadConfig();
            _timer.Interval = TimeSpan.FromSeconds(Math.Max(3, _config.RefreshSeconds));
            _timer.Tick += async (_, _) => await LoadStatusAsync();
            _timer.Start();
            await LoadStatusAsync();
        };
    }

    private WidgetConfig LoadConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "widget.json");
        if (!File.Exists(path)) throw new FileNotFoundException("找不到 widget.json", path);
        return JsonSerializer.Deserialize<WidgetConfig>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidDataException("widget.json 格式无效");
    }

    private async Task LoadStatusAsync()
    {
        try
        {
            var baseUrl = _config.Endpoint.TrimEnd('/');
            var nodes = await GetAsync<List<NodeInfo>>($"{baseUrl}/api/nodes");
            var wanted = _config.NodeIds.Length == 0 ? nodes : nodes.Where(node => _config.NodeIds.Contains(node.Uuid, StringComparer.OrdinalIgnoreCase)).ToList();
            var cards = await Task.WhenAll(wanted.Select(async node =>
            {
                var history = await GetAsync<List<RecentStatus>>($"{baseUrl}/api/recent/{node.Uuid}");
                return ServerCard.From(node, history.LastOrDefault());
            }));
            Servers.Clear();
            foreach (var card in cards) Servers.Add(card);
            Footer = $"更新于 {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            Footer = $"刷新失败：{ex.Message}";
        }
    }

    private async Task<T> GetAsync<T>(string url) where T : class
    {
        using var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var envelope = await JsonSerializer.DeserializeAsync<ApiResponse<T>>(await response.Content.ReadAsStreamAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (envelope?.Data is null) throw new InvalidDataException("Komari 返回了空数据");
        return envelope.Data;
    }

    private async void Refresh(object sender, RoutedEventArgs e) => await LoadStatusAsync();
    private void Minimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close(object sender, RoutedEventArgs e) => Close();
    private void DragWindow(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class WidgetConfig
{
    public string Endpoint { get; set; } = "https://example.com";
    public int RefreshSeconds { get; set; } = 10;
    public string[] NodeIds { get; set; } = [];
}

public sealed class ApiResponse<T> where T : class { public T? Data { get; set; } }
public sealed class NodeInfo { public string Uuid { get; set; } = ""; public string Name { get; set; } = "未命名节点"; }
public sealed class RecentStatus { public CpuInfo Cpu { get; set; } = new(); public MemoryInfo Ram { get; set; } = new(); public MemoryInfo Disk { get; set; } = new(); public NetworkInfo Network { get; set; } = new(); }
public sealed class CpuInfo { public double Usage { get; set; } }
public sealed class MemoryInfo { public long Total { get; set; } public long Used { get; set; } }
public sealed class NetworkInfo { public long Up { get; set; } public long Down { get; set; } }

public sealed class ServerCard
{
    public string Name { get; init; } = "";
    public string State { get; init; } = "在线";
    public string StateColor { get; init; } = "#86EFAC";
    public double Cpu { get; init; }
    public double Ram { get; init; }
    public double Disk { get; init; }
    public string CpuText => $"{Cpu:F0}%";
    public string RamText => $"{Ram:F0}%";
    public string DiskText => $"{Disk:F0}%";
    public string UploadText { get; init; } = "↑ 0 B/s";
    public string DownloadText { get; init; } = "↓ 0 B/s";

    public static ServerCard From(NodeInfo node, RecentStatus? status)
    {
        if (status is null) return new() { Name = node.Name, State = "离线", StateColor = "#FCA5A5" };
        return new()
        {
            Name = node.Name,
            Cpu = status.Cpu.Usage,
            Ram = Percent(status.Ram.Used, status.Ram.Total),
            Disk = Percent(status.Disk.Used, status.Disk.Total),
            UploadText = $"↑ {FormatRate(status.Network.Up)}",
            DownloadText = $"↓ {FormatRate(status.Network.Down)}"
        };
    }

    private static double Percent(long used, long total) => total == 0 ? 0 : Math.Clamp(used * 100d / total, 0, 100);
    private static string FormatRate(long value)
    {
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }
}
