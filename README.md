# Komari Desk Widget

一个轻量级的 Windows 原生桌面小组件，用于实时监控 [Komari](https://github.com/komari-monitoring/komari) 面板中指定 VPS 节点的状态。使用 WPF (.NET 8) 构建，零第三方依赖，支持自包含单文件发布。

## 功能特性

- 实时显示 CPU、内存、硬盘使用率
- 实时显示网络上下行速率
- 可配置自动刷新间隔（默认 10 秒）
- 支持多节点同时监控
- 可在设置窗口中选择要展示的节点
- 可在设置窗口中开启或关闭窗口置顶
- 无边框透明窗口，可拖动、可缩放
- 支持锁定窗口位置，避免误拖动
- 半透明卡片布局，风格接近 Komari 面板
- 提供安装器版本，可选择安装目录并创建桌面快捷方式
- 支持应用内在线检查更新，下载并启动最新版安装器
- 零第三方依赖，纯原生 WPF

## 项目结构

```
komari-desktop-widget/
├── App.xaml              # WPF 应用入口
├── App.xaml.cs
├── MainWindow.xaml       # 界面布局
├── MainWindow.xaml.cs    # 核心逻辑（数据获取、刷新、绑定）
├── KomariDeskWidget.csproj
├── widget.json           # 本地配置（已 gitignore，不会上传）
├── widget.example.json   # 配置示例
├── .gitignore
├── LICENSE
└── README.md
```

## 快速开始

### 方式一：直接下载 Release 运行（推荐）

1. 前往本仓库的 [Releases](../../releases) 页面，下载最新的 `KomariDeskWidget.zip`
2. 解压到任意目录
3. 将压缩包中的 `widget.json` 改为你自己的面板地址和节点 ID（见下方[配置说明](#配置说明)）
4. 双击 `KomariDeskWidget.exe` 即可运行

> Release 版本是自包含的单文件 exe（约 150MB），不需要安装 .NET 运行时。

### 方式二：从源码编译

#### 环境要求

- Windows 10/11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 或更高版本

#### 步骤

```bash
# 1. 克隆仓库
git clone git@github.com:Deng-Amor/vps-desktop-monitor.git
cd vps-desktop-monitor

# 2. 复制配置文件并编辑
cp widget.example.json widget.json
# 用文本编辑器打开 widget.json，填入你的面板地址和节点 ID

# 3. 编译发布（自包含单文件）
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# 4. 运行
# 双击 bin\Release\net8.0-windows\win-x64\publish\KomariDeskWidget.exe
# 或命令行启动：
./bin/Release/net8.0-windows/win-x64/publish/KomariDeskWidget.exe
```

> 如果只是本地调试，也可以用 `dotnet run` 直接运行（需要安装 .NET 8 SDK）。

## 配置说明

程序启动时会读取与 exe 同目录下的 `widget.json` 文件。参考 `widget.example.json` 创建你自己的配置：

```json
{
  "endpoint": "https://your-komari-panel.example.com",
  "refreshSeconds": 10,
  "topmost": true,
  "locked": false,
  "nodeIds": [
    "0625f4ce-6c6d-4373-b199-32bd9ef28a5b",
    "56969b25-8fc6-42b6-9c27-2d10abf0fcd7"
  ]
}
```

新安装时 `endpoint` 默认为空。首次运行如果还没有填写 Komari 面板地址，窗口底部会提示点击 `⚙` 进行设置。

| 字段 | 类型 | 说明 |
|------|------|------|
| `endpoint` | string | Komari 面板地址（含 `https://`，不带尾部斜杠） |
| `refreshSeconds` | int | 自动刷新间隔（秒），最小 3 秒 |
| `topmost` | bool | 是否让窗口始终置顶，`true` 为置顶，`false` 为普通窗口 |
| `locked` | bool | 是否锁定窗口移动，`true` 时无法拖动窗口 |
| `nodeIds` | string[] | 要显示的节点 UUID 列表；留空数组 `[]` 则显示全部节点 |

也可以点击窗口右上角的 `⚙` 设置按钮，在图形界面中修改置顶状态、锁定状态、刷新间隔，并勾选要展示的节点。

### 如何获取节点 UUID

1. 打开你的 Komari 面板网页
2. 在节点列表中，右键点击节点 →「检查」打开浏览器开发者工具
3. 在网络请求中找到 `/api/nodes` 的响应，每个节点的 `uuid` 字段就是你要的值
4. 将 UUID 复制到 `widget.json` 的 `nodeIds` 数组中

### 如何新增节点

只需在 `widget.json` 的 `nodeIds` 数组中添加新的 UUID 即可，程序下次刷新时自动加载：

```json
{
  "nodeIds": [
    "已有的-uuid",
    "新增的-uuid"
  ]
}
```

修改后保存文件，程序会在下次自动刷新时生效，无需重启。

## 使用方法

- **拖动窗口**：在窗口任意空白处按住鼠标左键拖动
- **缩放窗口**：拖动右下角的斜线缩放手柄
- **锁定窗口**：点击右上角 🔓 / 🔒 按钮，锁定后不能拖动窗口
- **立即刷新**：点击右上角 ↻ 按钮
- **最小化**：点击右上角 — 按钮
- **关闭**：点击右上角 × 按钮
- **设置**：点击右上角 ⚙ 按钮，可开关置顶、修改刷新间隔并选择展示节点
- **检查更新**：点击右上角 ⇧ 按钮，或在设置窗口中点击“检查更新”

## 在线更新

从 `v1.0.4` 开始，应用会在启动后静默检查 GitHub Releases 是否有新版本。如果发现新版本，会提示用户下载最新版安装器；确认后应用会下载 `Setup.exe` 到临时目录，启动安装器，并自动退出当前应用。

安装器会默认选择当前应用所在目录，因此已安装用户可以直接覆盖更新。更新时会保留已有的 `widget.json`，不会覆盖用户的 Komari 面板地址、节点选择、置顶状态和锁定状态。

从 `v1.0.5` 开始，新安装时选择安装位置后，安装器会自动在所选目录下创建 `komari-desk` 子文件夹。例如选择 `G:\Enviroment`，实际安装目录会变成 `G:\Enviroment\komari-desk`，避免程序文件散落在父目录中。

## 常见问题

### 双击 exe 没有反应

检查任务管理器中是否有 `KomariDeskWidget.exe` 进程残留，如果有多个，全部结束后重新双击。如果使用的是 Debug 版本（体积很小），需要先安装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)。推荐使用 Release 版本的自包含 exe。

### 窗口显示"刷新失败"

检查 `widget.json` 中的 `endpoint` 地址是否正确，以及你的电脑能否访问该地址。程序调用的是 Komari 的公开 API（`/api/nodes` 和 `/api/recent/{uuid}`），不需要 API Key。

### 窗口看不见但进程存在

本组件使用无边框透明窗口。如果窗口被其他窗口遮挡，检查任务栏是否有 Komari 图标，点击即可聚焦。窗口默认置顶，正常情况下不会被遮挡。

### 防火墙拦截

程序需要通过 HTTPS 访问 Komari 面板。如果被 Windows Defender 防火墙拦截，在弹出的提示中点击"允许访问"。

## 技术细节

- **框架**：.NET 8 + WPF
- **依赖**：零第三方 NuGet 包
- **API**：调用 Komari 的 `/api/nodes`（获取节点列表）和 `/api/recent/{uuid}`（获取节点最近状态）
- **更新源**：调用 GitHub Releases latest API 检查最新版，并下载 `*-Setup.exe`
- **数据格式**：JSON，使用 `System.Text.Json` 解析
- **窗口样式**：`WindowStyle="None"` + `AllowsTransparency="True"`，置顶状态由 `widget.json` 控制

## 从源码编译（Debug 调试）

如果需要调试或修改代码：

```bash
# 直接运行（需要 .NET 8 SDK）
dotnet run

# 或者用 Visual Studio 打开 .csproj 文件，按 F5 调试
```

Debug 版本的 exe 在 `bin\Debug\net8.0-windows\` 目录下，体积很小但需要系统安装 .NET 8 运行时。

## License

[MIT License](LICENSE) - Copyright (c) 2026 Deng-Amor
