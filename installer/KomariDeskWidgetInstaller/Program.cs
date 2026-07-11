using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace KomariDeskWidgetInstaller;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var args = Environment.GetCommandLineArgs();
        if (InstallerForm.TryRunSilent(args)) return;
        Application.Run(new InstallerForm(args));
    }
}

internal sealed class InstallerForm : Form
{
    private const string AppFolderName = "komari-desk";
    private const string RegistryPath = @"Software\KomariDeskWidget";
    private readonly TextBox _installPathBox = new();
    private readonly CheckBox _desktopShortcutBox = new();
    private readonly CheckBox _launchAfterInstallBox = new();
    private readonly Button _installButton = new();
    private readonly Button _browseButton = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _statusLabel = new();
    private readonly Label _pathHintLabel = new();
    private readonly Label _descriptionLabel = new();
    private readonly bool _installPathFromArgument;
    private bool _useExactInstallPath;

    public static bool TryRunSilent(string[] args)
    {
        if (!args.Any(arg => arg.Equals("--silent", StringComparison.OrdinalIgnoreCase))) return false;

        try
        {
            TryGetInitialInstallPath(args, out var installPath);
            var launchAfterInstall = args.Any(arg => arg.Equals("--launch", StringComparison.OrdinalIgnoreCase));
            var createShortcut = !args.Any(arg => arg.Equals("--noShortcut", StringComparison.OrdinalIgnoreCase));
            installPath = string.IsNullOrWhiteSpace(installPath)
                ? FindInstallPathOrDefault()
                : Path.GetFullPath(installPath);
            InstallTo(installPath, createShortcut, launchAfterInstall);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"自动更新失败：{ex.Message}", "Komari Desk Widget", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        return true;
    }

    public InstallerForm(string[] args)
    {
        Text = "Komari Desk Widget 安装器";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(640, 390);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(31, 41, 55);

        var card = new Panel
        {
            Location = new Point(18, 18),
            Size = new Size(604, 354),
            BackColor = Color.FromArgb(43, 52, 70)
        };

        var title = new Label
        {
            Text = "安装 Komari Desk Widget",
            Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            AutoSize = false,
            Location = new Point(24, 22),
            Size = new Size(540, 32)
        };

        _descriptionLabel.Text = "请选择安装位置。首次安装会在所选目录下创建 komari-desk 文件夹；检测到旧版本时会直接覆盖更新。";
        _descriptionLabel.ForeColor = Color.FromArgb(185, 198, 216);
        _descriptionLabel.BackColor = Color.Transparent;
        _descriptionLabel.AutoSize = false;
        _descriptionLabel.Location = new Point(24, 58);
        _descriptionLabel.Size = new Size(540, 42);

        var pathLabel = new Label
        {
            Text = "安装目录",
            ForeColor = Color.FromArgb(238, 245, 255),
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(24, 118)
        };

        _installPathFromArgument = TryGetInitialInstallPath(args, out var initialInstallPath);
        _useExactInstallPath = _installPathFromArgument;
        if (!_installPathFromArgument && TryFindExistingInstall(out var existingInstallPath))
        {
            initialInstallPath = existingInstallPath;
            _useExactInstallPath = true;
            _descriptionLabel.Text = "检测到已安装版本。安装器将默认覆盖旧文件并保留 widget.json 配置。";
            _pathHintLabel.Text = "已检测到旧安装目录，将在此目录内更新。";
        }

        _installPathBox.Location = new Point(24, 142);
        _installPathBox.Size = new Size(442, 28);
        _installPathBox.BorderStyle = BorderStyle.FixedSingle;
        _installPathBox.Text = initialInstallPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            AppFolderName);
        _installPathBox.BackColor = Color.FromArgb(248, 250, 252);

        StyleButton(_browseButton, "浏览…", new Point(482, 140), new Size(92, 32), Color.FromArgb(68, 80, 102));
        _browseButton.Click += Browse;

        _pathHintLabel.ForeColor = Color.FromArgb(185, 198, 216);
        _pathHintLabel.BackColor = Color.Transparent;
        _pathHintLabel.AutoSize = false;
        _pathHintLabel.Location = new Point(24, 178);
        _pathHintLabel.Size = new Size(550, 24);
        if (string.IsNullOrWhiteSpace(_pathHintLabel.Text))
        {
            _pathHintLabel.Text = "首次安装时可选择父目录，安装器会自动创建 komari-desk 子文件夹。";
        }

        _desktopShortcutBox.Text = "创建桌面快捷方式";
        _desktopShortcutBox.Checked = true;
        _desktopShortcutBox.AutoSize = true;
        _desktopShortcutBox.Location = new Point(24, 218);
        _desktopShortcutBox.ForeColor = Color.FromArgb(238, 245, 255);
        _desktopShortcutBox.BackColor = Color.Transparent;

        _launchAfterInstallBox.Text = "安装完成后启动程序";
        _launchAfterInstallBox.Checked = true;
        _launchAfterInstallBox.AutoSize = true;
        _launchAfterInstallBox.Location = new Point(200, 218);
        _launchAfterInstallBox.ForeColor = Color.FromArgb(238, 245, 255);
        _launchAfterInstallBox.BackColor = Color.Transparent;

        _progress.Location = new Point(24, 262);
        _progress.Size = new Size(550, 16);

        _statusLabel.Text = "准备安装。";
        _statusLabel.ForeColor = Color.FromArgb(185, 198, 216);
        _statusLabel.BackColor = Color.Transparent;
        _statusLabel.AutoSize = false;
        _statusLabel.Location = new Point(24, 292);
        _statusLabel.Size = new Size(360, 28);

        StyleButton(_installButton, "安装 / 更新", new Point(444, 292), new Size(130, 38), Color.FromArgb(37, 99, 235));
        _installButton.Click += Install;

        card.Controls.AddRange([
            title,
            _descriptionLabel,
            pathLabel,
            _installPathBox,
            _browseButton,
            _pathHintLabel,
            _desktopShortcutBox,
            _launchAfterInstallBox,
            _progress,
            _statusLabel,
            _installButton
        ]);
        Controls.Add(card);
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
            _useExactInstallPath = false;
            _installPathBox.Text = EnsureAppFolder(dialog.SelectedPath);
            _pathHintLabel.Text = $"实际安装目录：{_installPathBox.Text}";
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
            installPath = NormalizeInstallPath(installPath);
            _installPathBox.Text = installPath;
            SetBusy(true);
            _statusLabel.Text = "正在安装…";
            _progress.Value = 10;

            InstallTo(installPath, _desktopShortcutBox.Checked, _launchAfterInstallBox.Checked);
            _progress.Value = 75;

            var exePath = Path.Combine(installPath, "KomariDeskWidget.exe");
            _progress.Value = 92;

            _statusLabel.Text = "安装完成。";
            _progress.Value = 100;

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

    private static void InstallTo(string installPath, bool createShortcut, bool launchAfterInstall)
    {
        Directory.CreateDirectory(installPath);
        CloseRunningApp(installPath);
        ExtractPayload(installPath);

        var exePath = Path.Combine(installPath, "KomariDeskWidget.exe");
        if (createShortcut)
        {
            CreateDesktopShortcut(exePath);
        }

        SaveInstallPath(installPath);
        if (launchAfterInstall)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = installPath,
                UseShellExecute = true
            });
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

    private static bool TryGetInitialInstallPath(string[] args, out string? installPath)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals("--installPath", StringComparison.OrdinalIgnoreCase) || i + 1 >= args.Length) continue;
            installPath = args[i + 1].Trim('"');
            return true;
        }

        installPath = null;
        return false;
    }

    private string NormalizeInstallPath(string selectedPath)
    {
        var normalized = Path.GetFullPath(selectedPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (_useExactInstallPath || File.Exists(Path.Combine(normalized, "KomariDeskWidget.exe")))
        {
            return normalized;
        }

        return EnsureAppFolder(normalized);
    }

    private static string EnsureAppFolder(string selectedPath)
    {
        var normalized = Path.GetFullPath(selectedPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var folderName = Path.GetFileName(normalized);
        return folderName.Equals(AppFolderName, StringComparison.OrdinalIgnoreCase)
            ? normalized
            : Path.Combine(normalized, AppFolderName);
    }

    private static bool TryFindExistingInstall(out string installPath)
    {
        if (TryGetRegistryInstallPath(out installPath)) return true;
        if (TryGetShortcutInstallPath(out installPath)) return true;

        var localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        var candidates = new[]
        {
            Path.Combine(localPrograms, AppFolderName),
            Path.Combine(localPrograms, "Komari Desk Widget"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppFolderName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Komari Desk Widget")
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(Path.Combine(candidate, "KomariDeskWidget.exe"))) continue;
            installPath = candidate;
            return true;
        }

        installPath = "";
        return false;
    }

    private static string FindInstallPathOrDefault()
    {
        return TryFindExistingInstall(out var installPath)
            ? installPath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppFolderName);
    }

    private static bool TryGetRegistryInstallPath(out string installPath)
    {
        installPath = "";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            var value = key?.GetValue("InstallPath") as string;
            if (string.IsNullOrWhiteSpace(value) || !File.Exists(Path.Combine(value, "KomariDeskWidget.exe"))) return false;
            installPath = value;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetShortcutInstallPath(out string installPath)
    {
        installPath = "";
        var shortcutPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Komari Desk Widget.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Komari Desk Widget.lnk")
        };

        foreach (var shortcutPath in shortcutPaths)
        {
            if (!File.Exists(shortcutPath)) continue;
            var targetPath = TryReadShortcutTarget(shortcutPath);
            if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath)) continue;
            if (!Path.GetFileName(targetPath).Equals("KomariDeskWidget.exe", StringComparison.OrdinalIgnoreCase)) continue;
            installPath = Path.GetDirectoryName(targetPath) ?? "";
            return !string.IsNullOrWhiteSpace(installPath);
        }

        return false;
    }

    private static string? TryReadShortcutTarget(string shortcutPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            string? targetPath = shortcut.TargetPath;
            Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
            return targetPath;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveInstallPath(string installPath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key?.SetValue("InstallPath", installPath);
        }
        catch
        {
            // 注册表写入失败不影响安装，下一次仍可通过快捷方式或常见目录检测。
        }
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
        shortcut.IconLocation = exePath;
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
        _browseButton.Enabled = !busy;
    }

    private static void StyleButton(Button button, string text, Point location, Size size, Color backColor)
    {
        button.Text = text;
        button.Location = location;
        button.Size = size;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = backColor;
        button.ForeColor = Color.White;
        button.Cursor = Cursors.Hand;
    }
}
