墨舟 0.4.1 · 本地小说工作台

Windows 原生桌面软件：正文、人设、大纲、世界观、灵感、可拖动的人物关系网，以及嵌入式 Codex 写作搭档。
小说保存在本地文件夹。Codex 使用本机官方客户端已有的 ChatGPT 登录；只接受 ChatGPT 认证，不接入 API Key。

启动进入小说书架：选择继续写作、新建小说或打开已有小说文件夹。
书架按最近打开排序，可搜索书名和简介，显示章节数、正文估算字数与更新时间。
编辑器顶部“返回书架”会先保存当前内容；保存失败或 AI 仍在工作时会阻止直接切换。
卡片菜单支持重新定位和移出书架，移出不会删除小说文件。
首次运行可导入旧设置中的小说和程序目录 Library 下的小说。命令行传入项目目录或 project.mozhou.json 可直接打开。

开发与运行
需要 Windows、.NET 8 SDK（含 WPF 桌面开发支持）。无外部 NuGet 包。
在此目录运行：
  dotnet restore Mozhou.Desktop/Mozhou.Desktop.csproj --disable-parallel -m:1 -p:BuildInParallel=false
  dotnet build Mozhou.Desktop/Mozhou.Desktop.csproj -c Release --no-restore -m:1
  dotnet run --project Mozhou.Desktop/Mozhou.Desktop.csproj -c Release --no-build

测试均为依赖最少的控制台 runner，不使用 dotnet test：
  dotnet run --project Mozhou.Core.Tests -c Release -p:BuildInParallel=false
  dotnet run --project Mozhou.Codex.Tests -c Release -p:BuildInParallel=false
  dotnet run --project Mozhou.Graph.Tests -c Release -p:BuildInParallel=false
  dotnet run --project Mozhou.Desktop.Tests -c Debug -p:BuildInParallel=false
  dotnet run --project Mozhou.Bookshelf.Tests -c Debug -p:BuildInParallel=false
如果要重新构建正在运行的程序，先正常关闭该程序。
协议测试启动受控子进程，不登录真实账号、不调用真实模型。
协议测试请通过上述 dotnet run 或构建出的 Mozhou.Codex.Tests.exe 启动；不要通过 dotnet Mozhou.Codex.Tests.dll 启动，因为 runner 使用自身进程路径创建模拟服务器。

发行
  dotnet publish Mozhou.Desktop/Mozhou.Desktop.csproj -c Release --no-restore -m:1 -o publish
这是依赖 .NET 8 Desktop Runtime 的便携目录；请整体复制，不要只复制 EXE。

代码结构
Mozhou.Core：项目模型、文件保存与冲突检测、备份、导出、写作上下文。
Mozhou.Codex：官方 Codex app-server 的 stdio 通信、认证检查、模型列表、生成与取消。
Mozhou.Desktop：WPF 编辑器、关系图、项目操作和 AI 对话。
*.Tests：真实临时文件、独立进程、WPF 图与编辑器回归检查。
docs：设计、计划、审查与验证记录。
Mozhou.Benchmarks：百万字稿件的统计微基准，仅测字数/段落统计，不含界面布局、磁盘保存和 AI。

0.4.1 优化
缓存未修改条目的字数和段落统计，避免每次输入重新扫描全部正文；弱引用缓存不保留已关闭小说。
AI 流式文字约每 40 毫秒合并刷新，结束、取消或报错时刷新剩余片段；连接失败清理客户端和旧模型选项。
保存时复用已验证的文件基线，保留原有逐字节冲突检测；ZIP 历史文件和聊天记录分块复制。
验证结果、性能测量条件及复现命令见 docs/optimization-0.4.1.txt。

当前边界
首版是纯文本编辑器。分类和分卷为条目字段，未实现富文本、云同步、多人协作或第三方模型 API。
正文 Markdown/TXT 导出不含人设等资料；整本小说请用 ZIP 备份或保存完整副本。
Codex 依赖本机官方可执行文件与可用的 ChatGPT 登录，AI 请求需要网络并占用该账号额度。
当前协议针对本机 Codex 0.164.0-alpha.11.1 验证；未来官方协议变化可能需要更新适配。
应用只向 Codex 提交界面预览的写作上下文，不授予它修改小说文件的工具权限。
接收文本后，由作者决定复制、追加到原条目或另存为笔记。

官方接口参考（开发时已查阅）
https://learn.chatgpt.com/docs/app-server
https://learn.chatgpt.com/docs/auth
https://developers.openai.com/siwc/token-sharing-open-source/codex-app-server

尚未验证：不同电脑、不同 DPI/显示器组合、未来 Codex 版本，以及百万字稿件的完整编辑与保存流程。0.4.1 的百万字统计微基准不代表整机端到端性能。
