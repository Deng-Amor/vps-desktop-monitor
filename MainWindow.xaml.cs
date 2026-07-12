using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace KomariDeskWidget;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly DispatcherTimer _timer = new();
    private WidgetConfig _config = new();
    private string _footer = "正在连接…";
    private CodexQuotaSnapshot _codexQuota = CodexQuotaSnapshot.Loading();
    private bool _locked;
    private bool _showCodexPanel;
    private bool _codexLoaded;
    private bool _codexLoading;

    public ObservableCollection<ServerCard> Servers { get; } = [];
    public ObservableCollection<NodeSelection> Nodes { get; } = [];
    public string Footer { get => _footer; set { _footer = value; OnPropertyChanged(); } }
    public CodexQuotaSnapshot CodexQuota { get => _codexQuota; set { _codexQuota = value; OnPropertyChanged(); } }
    public string LockButtonText => _locked ? "🔒" : "🔓";
    public string LockButtonForeground => "#FFFFFF";
    public Visibility LockDotVisibility => _locked ? Visibility.Visible : Visibility.Collapsed;
    public string LockButtonTip => _locked ? "已锁定：不能拖动或缩放，点击解锁" : "未锁定：可以拖动和缩放，点击锁定";
    public string VersionText => $"v{UpdateService.CurrentVersion.ToString(3)}";
    public string PanelBackground => $"#{OpacityToAlpha(_config.PanelOpacity)}2B3446";
    public Visibility VpsPanelVisibility => !_showCodexPanel ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CodexPanelVisibility => _showCodexPanel ? Visibility.Visible : Visibility.Collapsed;
    public string VpsTabBackground => !_showCodexPanel ? "#F8FAFC" : "#22FFFFFF";
    public string VpsTabForeground => !_showCodexPanel ? "#111827" : "#DDE8F7";
    public string CodexTabBackground => _showCodexPanel ? "#F8FAFC" : "#22FFFFFF";
    public string CodexTabForeground => _showCodexPanel ? "#111827" : "#DDE8F7";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += async (_, _) =>
        {
            _config = LoadConfig();
            ApplyConfig();
            RestoreWindowPosition();
            _timer.Interval = TimeSpan.FromSeconds(Math.Max(3, _config.RefreshSeconds));
            _timer.Tick += async (_, _) =>
            {
                if (_showCodexPanel) await LoadCodexStatusAsync();
                else await LoadStatusAsync();
            };
            _timer.Start();
            await LoadStatusAsync();
            await UpdateService.CheckForUpdatesAsync(this, silentWhenLatest: true);
        };
    }

    private WidgetConfig LoadConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "widget.json");
        if (!File.Exists(path)) throw new FileNotFoundException("找不到 widget.json", path);
        return JsonSerializer.Deserialize<WidgetConfig>(File.ReadAllText(path), JsonOptions)
               ?? throw new InvalidDataException("widget.json 格式无效");
    }

    private void ApplyConfig()
    {
        Topmost = _config.Topmost;
        _locked = _config.Locked;
        RefreshLockState();
        OnPropertyChanged(nameof(PanelBackground));
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(3, _config.RefreshSeconds));
    }

    private void SaveConfig()
    {
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "widget.json"), JsonSerializer.Serialize(_config, JsonOptions));
    }

    private void RestoreWindowPosition()
    {
        if (_config.WindowLeft is not double left || _config.WindowTop is not double top) return;
        if (left + 40 < SystemParameters.VirtualScreenLeft || left > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40
            || top + 40 < SystemParameters.VirtualScreenTop || top > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40) return;
        Left = left;
        Top = top;
    }

    private void SaveWindowPosition()
    {
        _config.WindowLeft = Left;
        _config.WindowTop = Top;
        SaveConfig();
    }

    private async Task LoadStatusAsync()
    {
        try
        {
            var baseUrl = _config.Endpoint.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl) || baseUrl.Contains("your-komari", StringComparison.OrdinalIgnoreCase) || baseUrl.Contains("example.com", StringComparison.OrdinalIgnoreCase))
            {
                Servers.Clear();
                Footer = "请点击 ⚙ 设置 Komari 面板地址";
                return;
            }

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
        catch (HttpRequestException)
        {
            Footer = "连接失败：请检查 Komari 面板地址和网络";
        }
        catch (Exception ex)
        {
            Footer = $"刷新失败：{ex.Message}";
        }
    }

    private async Task LoadCodexStatusAsync(bool force = false)
    {
        if (_codexLoading) return;
        if (_codexLoaded && !force && CodexQuota.Status == "ok") return;

        _codexLoading = true;
        try
        {
            CodexQuota = CodexQuotaSnapshot.Loading();
            Footer = "正在读取 Codex…";
            CodexQuota = await CodexQuotaService.ReadAsync();
            _codexLoaded = true;
            Footer = CodexQuota.IsOk ? $"Codex {CodexQuota.UpdatedText}" : "Codex 读取失败";
        }
        finally
        {
            _codexLoading = false;
        }
    }

    private async Task<T> GetAsync<T>(string url) where T : class
    {
        using var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var envelope = await JsonSerializer.DeserializeAsync<ApiResponse<T>>(await response.Content.ReadAsStreamAsync(), JsonOptions);
        if (envelope?.Data is null) throw new InvalidDataException("Komari 返回了空数据");
        return envelope.Data;
    }

    private async void Refresh(object sender, RoutedEventArgs e)
    {
        if (_showCodexPanel) await LoadCodexStatusAsync(force: true);
        else await LoadStatusAsync();
    }
    private async void CheckUpdates(object sender, RoutedEventArgs e) => await UpdateService.CheckForUpdatesAsync(this, silentWhenLatest: false);
    private async void ShowVpsPanel(object sender, RoutedEventArgs e)
    {
        _showCodexPanel = false;
        RefreshPanelTabs();
        Footer = "VPS 状态";
        await LoadStatusAsync();
    }

    private async void ShowCodexPanel(object sender, RoutedEventArgs e)
    {
        _showCodexPanel = true;
        RefreshPanelTabs();
        await LoadCodexStatusAsync(force: !_codexLoaded);
    }

    private void RefreshPanelTabs()
    {
        OnPropertyChanged(nameof(VpsPanelVisibility));
        OnPropertyChanged(nameof(CodexPanelVisibility));
        OnPropertyChanged(nameof(VpsTabBackground));
        OnPropertyChanged(nameof(VpsTabForeground));
        OnPropertyChanged(nameof(CodexTabBackground));
        OnPropertyChanged(nameof(CodexTabForeground));
    }
    private void ToggleLock(object sender, RoutedEventArgs e)
    {
        _config.Locked = !_config.Locked;
        _locked = _config.Locked;
        SaveConfig();
        RefreshLockState();
    }

    private void RefreshLockState()
    {
        OnPropertyChanged(nameof(LockButtonText));
        OnPropertyChanged(nameof(LockButtonForeground));
        OnPropertyChanged(nameof(LockDotVisibility));
        OnPropertyChanged(nameof(LockButtonTip));
    }

    private async void OpenSettings(object sender, RoutedEventArgs e)
    {
        EndpointBox.Text = _config.Endpoint;
        RefreshBox.Text = _config.RefreshSeconds.ToString();
        TopmostBox.IsChecked = _config.Topmost;
        LockedBox.IsChecked = _config.Locked;
        PanelOpacitySlider.Value = Math.Clamp(_config.PanelOpacity, 30, 100);
        UpdatePanelOpacityText();
        DashboardPanelHost.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
        Footer = "设置";
        await LoadNodesForSettingsAsync();
    }

    private void PanelOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdatePanelOpacityText();
    }

    private void UpdatePanelOpacityText()
    {
        if (PanelOpacityText is not null)
        {
            PanelOpacityText.Text = $"{PanelOpacitySlider.Value:0}%";
        }
    }

    private async void RefreshNodes(object sender, RoutedEventArgs e) => await LoadNodesForSettingsAsync();

    private async Task LoadNodesForSettingsAsync()
    {
        try
        {
            SettingsStatusText.Text = "正在读取节点…";
            var endpoint = EndpointBox.Text.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                Nodes.Clear();
                SettingsStatusText.Text = "请先填写 Komari 面板地址。";
                return;
            }

            var nodes = await GetAsync<List<NodeInfo>>($"{endpoint}/api/nodes");
            var selected = _config.NodeIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allSelected = selected.Count == 0;
            Nodes.Clear();
            foreach (var node in nodes.OrderBy(node => node.Name))
            {
                Nodes.Add(new NodeSelection
                {
                    Uuid = node.Uuid,
                    Name = $"{node.Name} ({node.Uuid})",
                    IsSelected = allSelected || selected.Contains(node.Uuid)
                });
            }

            SettingsStatusText.Text = Nodes.Count == 0 ? "没有读取到节点。" : $"已读取 {Nodes.Count} 个节点。";
        }
        catch (Exception)
        {
            SettingsStatusText.Text = "读取失败：请检查面板地址和网络。";
        }
    }

    private void SelectAllNodes(object sender, RoutedEventArgs e)
    {
        foreach (var node in Nodes) node.IsSelected = true;
    }

    private void SelectNoNodes(object sender, RoutedEventArgs e)
    {
        foreach (var node in Nodes) node.IsSelected = false;
    }

    private async void SaveInlineSettings(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RefreshBox.Text.Trim(), out var refreshSeconds))
        {
            SettingsStatusText.Text = "刷新间隔必须是数字。";
            return;
        }

        var selectedIds = Nodes.Where(node => node.IsSelected).Select(node => node.Uuid).ToArray();
        if (Nodes.Count > 0 && selectedIds.Length == 0)
        {
            SettingsStatusText.Text = "请至少选择一个节点；想显示全部请点全选。";
            return;
        }

        _config = new WidgetConfig
        {
            Endpoint = EndpointBox.Text.Trim().TrimEnd('/'),
            RefreshSeconds = Math.Max(3, refreshSeconds),
            Topmost = TopmostBox.IsChecked == true,
            Locked = LockedBox.IsChecked == true,
            PanelOpacity = Math.Clamp((int)Math.Round(PanelOpacitySlider.Value), 30, 100),
            WindowLeft = _config.WindowLeft,
            WindowTop = _config.WindowTop,
            NodeIds = selectedIds.Length == Nodes.Count ? [] : selectedIds
        };
        SaveConfig();
        ApplyConfig();
        CloseSettingsView();
        await LoadStatusAsync();
    }

    private void CloseSettings(object sender, RoutedEventArgs e) => CloseSettingsView();

    private void CloseSettingsView()
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
        DashboardPanelHost.Visibility = Visibility.Visible;
        Footer = "返回主界面";
    }

    private void Minimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close(object sender, RoutedEventArgs e) => Close();
    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (_locked || e.ButtonState != MouseButtonState.Pressed) return;
        DragMove();
        SaveWindowPosition();
    }

    private void ResizeWindow(object sender, DragDeltaEventArgs e)
    {
        if (_locked) return;
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private static string OpacityToAlpha(int opacity)
    {
        var alpha = (byte)Math.Round(Math.Clamp(opacity, 30, 100) * 255d / 100d);
        return alpha.ToString("X2");
    }
}

public sealed class WidgetConfig
{
    public string Endpoint { get; set; } = "";
    public int RefreshSeconds { get; set; } = 10;
    public bool Topmost { get; set; } = true;
    public bool Locked { get; set; }
    public int PanelOpacity { get; set; } = 91;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public string[] NodeIds { get; set; } = [];
}

public sealed class ApiResponse<T> where T : class { public T? Data { get; set; } }
public sealed class NodeInfo
{
    public string Uuid { get; set; } = "";
    public string Name { get; set; } = "未命名节点";

    [JsonPropertyName("cpu_cores")]
    public int CpuCores { get; set; }
}

public sealed class RecentStatus { public CpuInfo Cpu { get; set; } = new(); public MemoryInfo Ram { get; set; } = new(); public MemoryInfo Disk { get; set; } = new(); public NetworkInfo Network { get; set; } = new(); }
public sealed class CpuInfo { public double Usage { get; set; } public int Cores { get; set; } }
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
    public string CoreText { get; init; } = "⚙ CPU";
    public string RamUsedText { get; init; } = "▣ 内存";
    public string DiskUsedText { get; init; } = "▣ 硬盘";
    public string NetworkText { get; init; } = "↑ 0 B/s  ↓ 0 B/s";

    public static ServerCard From(NodeInfo node, RecentStatus? status)
    {
        if (status is null) return new() { Name = node.Name, State = "离线", StateColor = "#DC2626" };
        var cores = status.Cpu.Cores > 0 ? status.Cpu.Cores : node.CpuCores;
        return new()
        {
            Name = node.Name,
            StateColor = "#16A34A",
            Cpu = status.Cpu.Usage,
            Ram = Percent(status.Ram.Used, status.Ram.Total),
            Disk = Percent(status.Disk.Used, status.Disk.Total),
            CoreText = cores > 0 ? $"⚙ {cores} Cores" : "⚙ CPU",
            RamUsedText = $"▣ {FormatSize(status.Ram.Used)}",
            DiskUsedText = $"▣ {FormatSize(status.Disk.Used)}",
            NetworkText = $"↑ {FormatRate(status.Network.Up)}  ↓ {FormatRate(status.Network.Down)}"
        };
    }

    private static double Percent(long used, long total) => total == 0 ? 0 : Math.Clamp(used * 100d / total, 0, 100);
    private static string FormatSize(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }

    private static string FormatRate(long value)
    {
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }
}
