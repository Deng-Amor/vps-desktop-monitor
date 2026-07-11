using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

namespace KomariDeskWidgetInstaller;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm(Environment.GetCommandLineArgs()));
    }
}

internal sealed class InstallerForm : Form
{
    private readonly TextBox _installPathBox = new();
    private readonly CheckBox _desktopShortcutBox = new();
    private readonly CheckBox _launchAfterInstallBox = new();
    private readonly Button _installButton = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _statusLabel = new();

    public InstallerForm(string[] args)
    {
        Text = "Komari Desk Widget 安装器";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 300);
        Font = new Font("Microsoft YaHei UI", 9F);

        var title = new Label
        {
            Text = "安装 Komari Desk Widget",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = false,
            Location = new Point(24, 22),
            Size = new Size(510, 24)
        };

        var description = new Label
        {
            Text = "请选择安装位置。安装器会复制程序文件，并可创建桌面快捷方式。",
            AutoSize = false,
            Location = new Point(24, 54),
            Size = new Size(510, 24)
        };

        var pathLabel = new Label
        {
            Text = "安装目录",
            AutoSize = true,
            Location = new Point(24, 96)
        };

        _installPathBox.Location = new Point(24, 120);
        _installPathBox.Size = new Size(405, 27);
        _installPathBox.Text = GetInitialInstallPath(args) ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Komari Desk Widget");

        var browseButton = new Button
        {
            Text = "浏览…",
            Location = new Point(440, 119),
            Size = new Size(90, 30)
        };
        browseButton.Click += Browse;

        _desktopShortcutBox.Text = "创建桌面快捷方式";
        _desktopShortcutBox.Checked = true;
        _desktopShortcutBox.AutoSize = true;
        _desktopShortcutBox.Location = new Point(24, 165);

        _launchAfterInstallBox.Text = "安装完成后启动程序";
        _launchAfterInstallBox.Checked = true;
        _launchAfterInstallBox.AutoSize = true;
        _launchAfterInstallBox.Location = new Point(180, 165);

        _progress.Location = new Point(24, 205);
        _progress.Size = new Size(506, 18);

        _statusLabel.Text = "准备安装。";
        _statusLabel.AutoSize = false;
        _statusLabel.Location = new Point(24, 232);
        _statusLabel.Size = new Size(330, 24);

        _installButton.Text = "安装";
        _installButton.Location = new Point(440, 236);
        _installButton.Size = new Size(90, 34);
        _installButton.Click += Install;

        Controls.AddRange([
            title,
            description,
            pathLabel,
            _installPathBox,
            browseButton,
            _desktopShortcutBox,
            _launchAfterInstallBox,
            _progress,
            _statusLabel,
            _installButton
        ]);
    }

    private void Browse(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 Komari Desk Widget 的安装目录",
            SelectedPath = _installPathBox.Text,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _installPathBox.Text = dialog.SelectedPath;
        }
    }

    private void Install(object? sender, EventArgs e)
    {
        var installPath = _installPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(installPath))
        {
            MessageBox.Show(this, "请先选择安装目录。", "安装器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            SetBusy(true);
            _statusLabel.Text = "正在安装…";
            _progress.Value = 10;

            Directory.CreateDirectory(installPath);
            CloseRunningApp(installPath);
            ExtractPayload(installPath);
            _progress.Value = 75;

            var exePath = Path.Combine(installPath, "KomariDeskWidget.exe");
            if (_desktopShortcutBox.Checked)
            {
                CreateDesktopShortcut(exePath);
            }
            _progress.Value = 92;

            _statusLabel.Text = "安装完成。";
            _progress.Value = 100;

            if (_launchAfterInstallBox.Checked)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = installPath,
                    UseShellExecute = true
                });
            }

            MessageBox.Show(this, "安装完成。", "Komari Desk Widget", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "安装失败。";
            MessageBox.Show(this, $"安装失败：{ex.Message}", "安装器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetBusy(false);
        }
    }

    private static void ExtractPayload(string installPath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("KomariDeskWidgetPayload.zip")
            ?? throw new InvalidOperationException("安装包缺少内置程序文件。");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name)) continue;

            var targetPath = Path.GetFullPath(Path.Combine(installPath, entry.FullName));
            var root = Path.GetFullPath(installPath);
            if (!targetPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("安装包路径无效。");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            if (entry.Name.Equals("widget.json", StringComparison.OrdinalIgnoreCase) && File.Exists(targetPath))
            {
                continue;
            }

            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private static string? GetInitialInstallPath(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals("--installPath", StringComparison.OrdinalIgnoreCase) || i + 1 >= args.Length) continue;
            return args[i + 1].Trim('"');
        }

        return null;
    }

    private static void CloseRunningApp(string installPath)
    {
        var targetExe = Path.GetFullPath(Path.Combine(installPath, "KomariDeskWidget.exe"));
        foreach (var process in Process.GetProcessesByName("KomariDeskWidget"))
        {
            try
            {
                var processPath = process.MainModule?.FileName;
                if (!string.Equals(processPath, targetExe, StringComparison.OrdinalIgnoreCase)) continue;
                process.CloseMainWindow();
                if (!process.WaitForExit(5000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch
            {
                // 忽略无法访问的进程，后续覆盖文件失败时会提示用户。
            }
        }
    }

    private static void CreateDesktopShortcut(string exePath)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktop, "Komari Desk Widget.lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("无法创建快捷方式。");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = exePath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
        shortcut.Description = "Komari Desk Widget";
        shortcut.Save();

        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }

    private void SetBusy(bool busy)
    {
        _installButton.Enabled = !busy;
        _installPathBox.Enabled = !busy;
        _desktopShortcutBox.Enabled = !busy;
        _launchAfterInstallBox.Enabled = !busy;
    }
}
