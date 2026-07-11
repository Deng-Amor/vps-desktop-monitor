using System.Windows;

namespace KomariDeskWidget;

public partial class UpdateProgressWindow : Window
{
    public UpdateProgressWindow()
    {
        InitializeComponent();
    }

    public void SetPreparing()
    {
        StatusText.Text = "准备下载更新…";
        DetailText.Text = "请稍等，下载完成后会自动安装并重启。";
        PercentText.Text = "0%";
        DownloadProgress.IsIndeterminate = true;
    }

    public void ReportDownload(long downloadedBytes, long? totalBytes)
    {
        DownloadProgress.IsIndeterminate = !totalBytes.HasValue || totalBytes.Value <= 0;
        if (totalBytes.HasValue && totalBytes.Value > 0)
        {
            var percent = Math.Clamp(downloadedBytes * 100d / totalBytes.Value, 0, 100);
            DownloadProgress.Value = percent;
            PercentText.Text = $"{percent:0}%";
            DetailText.Text = $"{FormatSize(downloadedBytes)} / {FormatSize(totalBytes.Value)}";
        }
        else
        {
            PercentText.Text = "下载中";
            DetailText.Text = $"已下载 {FormatSize(downloadedBytes)}";
        }

        StatusText.Text = "正在下载新版安装器…";
    }

    public void SetInstalling()
    {
        DownloadProgress.IsIndeterminate = true;
        PercentText.Text = "安装中";
        StatusText.Text = "下载完成，正在安装并重启…";
        DetailText.Text = "应用会暂时关闭，安装完成后自动重新打开。";
    }

    private static string FormatSize(long value)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }
}
