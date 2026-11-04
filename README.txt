墨舟 0.7.0 · 本地小说工作台

Windows 原生桌面软件：正文、人设、大纲、世界观、灵感、可拖动的人物关系网，以及嵌入式 Codex 写作搭档。
新建小说保存在一个本地 .mozhou 文档中；旧版小说文件夹仍可继续使用。Codex 使用本机官方客户端已有的 ChatGPT 登录；只接受 ChatGPT 认证，不接入 API Key。

启动进入小说书架：选择继续写作、新建小说或打开/导入已有小说。
新建时选择书名和 .mozhou 保存位置；打开/导入可选择 .mozhou 文档或旧版 project.mozhou.json。
书架按最近打开排序，可搜索书名和简介，显示章节数、正文估算字数与更新时间。
编辑器顶部“返回书架”会先保存当前内容；保存失败或 AI 仍在工作时会阻止直接切换。
卡片菜单支持重新定位和移出书架，移出不会删除小说文件。
首次运行可导入旧设置中的小说和程序目录 Library 下的小说。命令行传入 .mozhou、旧项目目录或 project.mozhou.json 可直接打开。

开始使用
运行需要 Windows 和 .NET 8 Desktop Runtime。Windows ZIP 请完整解压后启动 Mozhou.exe，不要只复制 EXE。
0.7.0 下载 https://github.com/AkiNariT/mozhou/releases/tag/v0.7.0

0.7.0 人设图造型
打开角色，在参考图卡片下点“造型起名”。可以只取组名，如“入学第一天”，也可以填写发型和衣着。同名图归为一组；三项均可组合筛选。在选中的组里添加图片，新图沿用同组共同特征。首次保存分类后，请用0.7.0或更新版本编辑这本小说。

0.6.0 独立小说文档
一本小说保存为一个 .mozhou，包含正文、设定、人物关系图、参考图、AI聊天和最近30次保存历史。
文档使用 Windows 系统提供的 SQLite 容器；自动保存成功即表示最新稿件已写入 .mozhou 文件。
移动或复制前，请等待保存完成并退出墨舟，再移动这个文档。写入期间旁边可能短暂出现 -journal 文件，正常提交后移除；图片预览临时缓存可再生，不需要一同移动。
旧版文件夹继续支持。打开旧稿后，在项目菜单选择“另存为墨舟文档…”，选定新文件；保存成功后切换到新文档，原文件夹保留。
继续使用旧格式时，须整体保留 project.mozhou.json、assistant-chat.json、assets/images、.bak 和 backups 等原目录内容。
不自动注册 .mozhou 文件关联。可从应用打开，或在 Windows“打开方式”中选择 Mozhou.exe。
0.5.0 不能打开新 .mozhou 容器；新文档需要0.6.0或更新的兼容版本。
ZIP完整备份仍导出为旧版逻辑目录。解压后选择 project.mozhou.json 打开，不要把ZIP直接改名为 .mozhou。
书架索引 bookshelf.json 与本机设置 mozhou-settings.json 位于程序目录，升级时可保留；文档移动后请从新位置打开或重新定位。
详细使用说明见 docs/single-document-0.6.0.txt。

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

0.5.0 条目参考图
人设、世界观和其他条目下方可以一次添加多张PNG/JPEG/BMP/GIF（首帧）参考图，点击放大与缩放，图库可折叠。
单张最多25 MiB、4000万像素。导入会保存图片副本，外部原图移动或删除不影响已导入图片；旧目录保存到assets/images，新 .mozhou 保存到文档内部。
移除只解除关联，保留文件供历史恢复；ZIP与另存副本包含图片。遇图片缺失，正文可编辑，但完整备份/副本会报错。
旧目录首次含图保存升级内部小说数据格式到2，需要0.5.0或更新兼容版本；旧稿无图片则保持格式1。新 .mozhou 容器整体需要0.6.0或更新兼容版本。
参考图不自动发送给Codex，也不嵌入正文TXT/Markdown导出。

0.4.3 朱砂折带图标
暖白底色与朱砂折面组成书页、小舟和 S 形留白，统一 EXE、窗口、任务栏、普通对话框及页头标识。
经过 AI 概念探索后重绘为不依赖字体的 SVG；SVG、PNG、多尺寸 ICO 位于 Mozhou.Desktop/Assets。
生成工具位于 tools/GenerateIcon，无第三方依赖，支持当前素材所用的路径、线性渐变及矩阵变换。

当前边界
首版是纯文本编辑器。分类和分卷为条目字段，未实现富文本、云同步、多人协作或第三方模型 API。
正文 Markdown/TXT 导出不含人设、图片等资料；整本小说可保存为 .mozhou 文档副本或创建 ZIP 备份。
Codex 依赖本机官方可执行文件与可用的 ChatGPT 登录，AI 请求需要网络并占用该账号额度。
当前协议针对本机 Codex 0.164.0-alpha.11.1 验证；未来官方协议变化可能需要更新适配。
应用只向 Codex 提交界面预览的写作上下文，不授予它修改小说文件的工具权限。
接收文本后，由作者决定复制、追加到原条目或另存为笔记。

官方接口参考（开发时已查阅）
https://learn.chatgpt.com/docs/app-server
https://learn.chatgpt.com/docs/auth
https://developers.openai.com/siwc/token-sharing-open-source/codex-app-server

尚未验证：不同电脑、不同 DPI/显示器组合、未来 Codex 版本，以及百万字稿件的完整编辑与保存流程。0.4.1 的百万字统计微基准不代表整机端到端性能。
