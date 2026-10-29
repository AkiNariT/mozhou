# 墨舟 Mozhou

<img src="Mozhou.Desktop/Assets/Mozhou.png" width="96" height="96" alt="墨舟藏书印图标">

一个安静、温润的 Windows 本地小说写作工具。以暖白纸色、宋体、留白和书刊装帧为设计基础，把正文、人物和故事世界放在同一个工作台里。

当前版本：**0.4.2**。使用 C#、.NET 8 和 WPF 开发，无第三方 NuGet 依赖。

## 能做什么

- **小说书架**：选择作品继续写作，按最近打开排序，搜索书名与简介。
- **写作与资料**：整理正文、人设、大纲、世界观和灵感，填写分卷、标签与摘要。
- **人物关系网**：建立有方向的关系，拖动人物卡片，聚焦某个人物及其邻接关系。
- **专注写作**：收起侧栏，保留居中的纸页；支持纯文本和直接输入 Markdown 标记。
- **保存与备份**：自动保存、外部修改冲突检测、历史备份、完整 ZIP 备份以及 TXT/Markdown 正文导出。
- **Codex 写作搭档**：连接本机官方 Codex，预览将发送的上下文，接收流式回答，决定复制、追加或另存为笔记。

本地写作不依赖 AI。AI 功能使用官方 Codex 的现有 ChatGPT 登录，需要网络并使用账号额度；目前不支持 API Key 和第三方模型接口。应用本身不读取、保存或上传登录令牌。

## 开始使用

在本仓库的 Releases 中下载 Windows ZIP，完整解压后运行 `Mozhou.exe`。不要只复制 EXE；同目录的 DLL 和运行配置必须保留。

运行环境：Windows 和 **.NET 8 Desktop Runtime**。启动后，在书架新建小说，或打开已有的小说文件夹。

每本小说保存在用户选择的本地文件夹中：

| 文件 | 用途 |
| --- | --- |
| `project.mozhou.json` | 正文、资料、人物关系和布局 |
| `assistant-chat.json` | 该小说的 AI 对话 |
| `project.mozhou.json.bak`、`backups/` | 保存与恢复使用的历史版本 |

迁移和备份请保留整个小说文件夹。书架索引 `bookshelf.json` 和本机设置 `mozhou-settings.json` 位于程序目录；升级时保留这两个文件可沿用书架和设置。小说文件不会因升级自动搬移。

仓库和发行包只包含软件，不包含个人小说、聊天、书架索引或账号设置。
源码中的 `CreateDemo()` 和测试包含“雾港来信”等内置示例文字，用于演示及回归检查，不会自动加入新版首次启动的空书架。

## 从源码运行

需要 Windows 和 **.NET 8 SDK**。

```powershell
dotnet restore Mozhou.Desktop/Mozhou.Desktop.csproj --disable-parallel -m:1 -p:BuildInParallel=false
dotnet build Mozhou.Desktop/Mozhou.Desktop.csproj -c Release --no-restore -m:1
dotnet run --project Mozhou.Desktop/Mozhou.Desktop.csproj -c Release --no-build
```

发布为便携目录：

```powershell
dotnet publish Mozhou.Desktop/Mozhou.Desktop.csproj -c Release --no-restore -m:1 -o publish
```

重新构建同一路径的 EXE 前，请正常关闭正在运行的程序。

## 测试

测试采用控制台 runner，不使用 `dotnet test`。在源码根目录依次执行：

```powershell
dotnet run --project Mozhou.Core.Tests -c Debug -p:BuildInParallel=false
dotnet run --project Mozhou.Bookshelf.Tests -c Debug -p:BuildInParallel=false
dotnet run --project Mozhou.Codex.Tests -c Debug -p:BuildInParallel=false
dotnet run --project Mozhou.Graph.Tests -c Debug -p:BuildInParallel=false
dotnet run --project Mozhou.Desktop.Tests -c Debug -p:BuildInParallel=false
```

Codex 协议测试启动受控模拟子进程，不调用真实模型。请通过 `dotnet run` 或构建出的测试 EXE 运行，避免用 `dotnet Mozhou.Codex.Tests.dll` 启动：该 runner 依赖自身 EXE 路径创建模拟服务器。

0.4.1 在开发电脑上通过 **196 项检查**。详细范围见 [优化与验证记录](docs/optimization-0.4.1.txt)。

## 0.4.2 软件图标

原创“藏书印”图标：朱砂印面、暖白手绘舟字，矢量轮廓不依赖字体。EXE、窗口、任务栏、对话框及书架/编辑器页头使用同一图标。

ICO 包含 16、20、24、32、40、48、64、96、128、256 像素透明图层。SVG、PNG 和 ICO 位于 `Mozhou.Desktop/Assets`；在 Windows 上可重新生成：

```powershell
dotnet run --project tools/GenerateIcon -c Release -- Mozhou.Desktop/Assets
```

## 0.4.1 优化

- 缓存未修改条目的字数和段落，避免每次输入扫描整部小说。
- AI 流式文字约每 40 毫秒合并显示，结束、取消和错误时保留收到的尾段。
- 减少保存时的重复解析，并分块读取 ZIP 备份中的历史文件和聊天记录。

本机使用百万字、200 章稿件模拟 100 次编辑，统计环节耗时中位数首次测量约为 **464 ms → 8 ms**。这只测统计计算，不包含编辑器布局、磁盘保存或 AI。复现命令：

```powershell
dotnet run --project Mozhou.Benchmarks -c Release -p:BuildInParallel=false
```

## 代码结构

| 目录 | 职责 |
| --- | --- |
| `Mozhou.Core` | 小说模型、书架、统计、保存、备份、导出与写作上下文 |
| `Mozhou.Codex` | 官方 Codex app-server 通信、模型列表、生成和取消 |
| `Mozhou.Desktop` | WPF 书架、编辑器、人物关系网与 AI 对话 |
| `*.Tests` | 文件、协议、WPF 控件和图布局回归检查 |
| `Mozhou.Benchmarks` | 统计性能微基准 |
| `docs` | 设计、历史审查和验证记录 |

当前是纯文本编辑器，尚未实现富文本、云同步、多人协作、安装器、代码签名或自动更新。Codex 适配依赖官方协议；开发时验证的客户端版本为 `0.164.0-alpha.11.1`，未来版本可能需要调整。
