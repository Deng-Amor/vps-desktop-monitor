using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace KomariDeskWidget;

public partial class SettingsWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly WidgetConfig _config;

    public ObservableCollection<NodeSelection> Nodes { get; } = [];

    public SettingsWindow(WidgetConfig config)
    {
        InitializeComponent();
        DataContext = this;
        _config = new WidgetConfig
        {
            Endpoint = config.Endpoint,
            RefreshSeconds = config.RefreshSeconds,
            Topmost = config.Topmost,
            NodeIds = [.. config.NodeIds]
        };

        EndpointBox.Text = _config.Endpoint;
        RefreshBox.Text = _config.RefreshSeconds.ToString();
        TopmostBox.IsChecked = _config.Topmost;
        Loaded += async (_, _) => await LoadNodesAsync();
    }

    private async void RefreshNodes(object sender, RoutedEventArgs e) => await LoadNodesAsync();

    private async Task LoadNodesAsync()
    {
        try
        {
            StatusText.Text = "正在读取节点…";
            var endpoint = EndpointBox.Text.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                StatusText.Text = "请先填写 Komari 面板地址。";
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
                    Name = $"{node.Name}  ({node.Uuid})",
                    IsSelected = allSelected || selected.Contains(node.Uuid)
                });
            }

            StatusText.Text = Nodes.Count == 0 ? "没有读取到节点。" : $"已读取 {Nodes.Count} 个节点。";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"读取失败：{ex.Message}";
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

    private void SelectAll(object sender, RoutedEventArgs e)
    {
        foreach (var node in Nodes) node.IsSelected = true;
    }

    private void SelectNone(object sender, RoutedEventArgs e)
    {
        foreach (var node in Nodes) node.IsSelected = false;
    }

    private void Cancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RefreshBox.Text.Trim(), out var refreshSeconds))
        {
            MessageBox.Show("刷新间隔必须是数字。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        refreshSeconds = Math.Max(3, refreshSeconds);
        var selectedIds = Nodes.Where(node => node.IsSelected).Select(node => node.Uuid).ToArray();
        if (Nodes.Count > 0 && selectedIds.Length == 0)
        {
            MessageBox.Show("请至少选择一个节点；如果想显示全部节点，请点击“全选”。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string[] nodeIds = selectedIds.Length == Nodes.Count ? [] : selectedIds;
        var nextConfig = new WidgetConfig
        {
            Endpoint = EndpointBox.Text.Trim().TrimEnd('/'),
            RefreshSeconds = refreshSeconds,
            Topmost = TopmostBox.IsChecked == true,
            NodeIds = nodeIds
        };

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "widget.json"), JsonSerializer.Serialize(nextConfig, JsonOptions));
        DialogResult = true;
        Close();
    }
}

public sealed class NodeSelection : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Uuid { get; init; } = "";
    public string Name { get; init; } = "";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
