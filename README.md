# 墨舟 Mozhou

<img src="Mozhou.Desktop/Assets/Mozhou.png" width="96" height="96" alt="墨舟朱砂折带图标">

一个安静、温润的 Windows 本地小说写作工具。以暖白纸色、宋体、留白和书刊装帧为设计基础，把正文、人物和故事世界放在同一个工作台里。

**[下载墨舟 0.8.0 Windows x64 安装版](https://github.com/AkiNariT/mozhou/releases/download/v0.8.0/Mozhou-Setup-0.8.0-x64.exe)**

双击安装包，完成后从桌面打开墨舟。安装包自带 .NET 桌面运行环境。

## 能做什么

- **小说书架**：选择作品继续写作，按最近打开排序，搜索书名与简介。
- **写作与资料**：整理正文、人设、大纲、世界观和灵感，填写分卷、标签与摘要。
- **条目参考图**：给人设、场景等资料附多张本地图片，完整缩略图、点击放大、缩放查看，可折叠收起。
- **人物关系网**：建立有方向的关系，拖动人物卡片，聚焦某个人物及其邻接关系。
- **专注写作**：收起侧栏，保留居中的纸页；支持纯文本和直接输入 Markdown 标记。
- **独立小说文档**：新建作品保存为一个 `.mozhou` 文件，包含正文、设定、关系图、参考图、AI 对话和最近 30 次保存历史。
- **保存与备份**：自动保存直接更新小说文档，支持外部修改冲突检测、历史恢复、完整 ZIP 备份以及 TXT/Markdown 正文导出。
- **Codex 写作搭档**：连接本机官方 Codex，预览将发送的上下文，接收流式回答，决定复制、追加或另存为笔记。

本地写作不依赖 AI。AI 功能使用官方 Codex 的现有 ChatGPT 登录，需要网络并使用账号额度；目前不支持 API Key 和第三方模型接口。应用本身不读取、保存或上传登录令牌。

## 人设图的造型

在人设档案中添加图片，点击图片下的“造型起名”。你可以给一组图取名“入学第一天”，把发型填为“长发”，衣着填为“校服”。三项都可以自由填写或留空。同一角色下，同名图片会归在一起。

在图库上方选造型名可看整组，也可继续按发型和衣着缩小范围。在选中的组里添加图片时，新图会沿用该组的名称和共同特征。旧图片可从“未命名”找到，再逐张整理。首次保存造型后，请使用墨舟 **0.7.0 或更新版本** 编辑这本小说。

## 开始使用

运行下载好的 `Mozhou-Setup-0.8.0-x64.exe`。安装器为当前用户创建桌面和开始菜单快捷方式。书架中的“新建小说”会让你选择书名和作品的保存位置。

第一次从便携版转入时，安装器会尝试找到旧程序目录。若出现“沿用便携版书架”，选择含旧 `Mozhou.exe` 的文件夹即可。已有小说仍从原位置打开，包括旧便携目录中的 `Library` 作品。

在书架底部或编辑器的“项目”菜单打开“版本与更新”。发现新版后，下载并运行新版安装包即可升级。这里也能进入 Windows 的应用列表，选择“墨舟”卸载。卸载保留作品、书架和设置，重新安装后可以继续使用。

新建小说采用一个 `.mozhou` 文件保存整本作品。自动保存成功后，最新内容已经写入该文件；参考图、AI 对话和最近 30 次保存历史也包含在文档内。请等待保存完成并退出墨舟，再复制或移动这个文件。保存期间旁边可能短暂出现 `-journal` 文件；正常提交后会移除。图片预览缓存可以重新生成，不需要随小说搬运。

旧版小说文件夹仍可继续编辑。要转换，打开旧稿后在项目菜单选择“另存为墨舟文档…”，选择新 `.mozhou` 文件；保存成功后切换到新文档，原文件夹保留。继续使用旧格式时，仍须保留整个文件夹，包括 `project.mozhou.json`、`assistant-chat.json`、`assets/images/`、`.bak` 和 `backups/`。

安装后双击 `.mozhou` 即可打开。如果 Windows 询问打开方式，或此前已选过其他程序，请在“打开方式”中选“墨舟”。

安装版将书架和设置存放在 `%LOCALAPPDATA%\Mozhou\UserData`。文档移动后，可从新位置重新打开或在书架重新定位。

完整 ZIP 备份仍导出为旧版逻辑目录结构。解压后选择其中的 `project.mozhou.json` 即可打开；ZIP 不是 `.mozhou` 文件，不能通过改扩展名转换。具体操作见 [单文档使用说明](docs/single-document-0.6.0.txt)。

仓库和发行包只包含软件，不包含个人小说、参考图、聊天、书架索引或账号设置。
源码中的 `CreateDemo()` 和测试包含“雾港来信”等内置示例文字，用于演示及回归检查，不会自动加入新版首次启动的空书架。

## 从源码运行

需要 Windows 和 **.NET 8 SDK**。

```powershell
dotnet restore Mozhou.Desktop/Mozhou.Desktop.csproj --disable-parallel -m:1 -p:BuildInParallel=false
dotnet build Mozhou.Desktop/Mozhou.Desktop.csproj -c Release --no-restore -m:1
dotnet run --project Mozhou.Desktop/Mozhou.Desktop.csproj -c Release --no-build
```

构建 Windows 安装包需要 Inno Setup 6 和 .NET 8 SDK。以下命令在工作目录里准备 x64 运行环境并生成 EXE。将 `$compiler` 设为本机 ISCC.exe 的路径。

```powershell
$work = Join-Path $env:TEMP ('MozhouBuild-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $work 'payload'
$compiler = Read-Host 'Inno Setup ISCC.exe 的完整路径'
.\packaging\Publish-Payload.ps1 -WorkDir $work -PayloadDir $payload
.\packaging\Build-Installer.ps1 -PayloadDir $payload -OutputDir (Join-Path $work 'setup') -IsccPath $compiler
```

0.8.0 的安装包使用 Inno Setup 6.7.4 和微软 .NET 8.0.33 Windows Desktop 运行时。[Inno Setup](https://jrsoftware.org/isdl.php) · [微软自包含部署说明](https://learn.microsoft.com/en-us/dotnet/core/deploying/)

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

0.6.0 本机 322 项检查通过，范围和限制见 [单文档验证记录](docs/single-document-verification-0.6.0.txt)。历史版本记录见 [0.4.1](docs/optimization-0.4.1.txt) 和 [0.5.0](docs/reference-images-0.5.0.txt)。

## 0.6.0 独立小说文档

`.mozhou` 使用 Windows 系统提供的 SQLite 作为容器，把正文、设定、关系图、图片、聊天和最近 30 次保存历史放在一个文件里。自动保存直接更新文档；正常保存完成并退出后，只需复制或移动这个文件即可携带整本作品。

旧目录保持兼容，转换通过项目菜单“另存为墨舟文档…”完成并保留旧稿。新格式不能由 0.5.0 打开。使用方法、移动方式、ZIP 备份及兼容边界见 [单文档使用说明](docs/single-document-0.6.0.txt)。

## 0.5.0 条目参考图

在人设、世界观或其他条目下方点击“添加图片”，可一次选择多张 PNG、JPEG、BMP 或 GIF（显示首帧）。点击缩略图放大，预览支持适应窗口与缩放。参考图区可折叠。

每张图片最多 25 MiB、4000 万像素。导入会复制原图，外部原图随后移动或删除也不影响小说中的副本；“移除”只解除当前条目关联，保留文件供历史恢复。ZIP 备份与“另存副本”包含这些图片；图片缺失时条目显示占位，完整备份或副本会提示失败。

旧目录首次添加图片后，内部小说数据格式升级为 2，需使用 **0.5.0 或更新的兼容版本** 打开；无图片的旧稿保持原格式。旧目录中的图片副本位于 `assets/images/`，请随整个文件夹保留；0.6.0 的 `.mozhou` 文档将图片存入容器，整个文档需要 0.6.0 或更新的兼容版本。参考图不会自动发送给 Codex，也不嵌入 TXT/Markdown 正文导出。

## 0.4.3 朱砂折带图标

暖白底色上，朱砂折带组合成书页与舟，折面之间保留 S 形留白。图标经 AI 概念探索后重新绘制为可编辑 SVG，不依赖字体或外部图片。EXE、窗口、任务栏、对话框及书架/编辑器页头使用同一图标。

ICO 包含 16、20、24、32、40、48、64、96、128、256 像素图层，圆角外缘透明。SVG、PNG 和 ICO 位于 `Mozhou.Desktop/Assets`；生成器支持当前素材使用的路径、线性渐变和矩阵变换，不是通用 SVG 渲染器。在 Windows 上可重新生成：

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

当前是纯文本编辑器，尚未实现富文本、云同步、多人协作、代码签名或后台自动更新。Codex 适配依赖官方协议；开发时验证的客户端版本为 `0.164.0-alpha.11.1`，未来版本可能需要调整。
