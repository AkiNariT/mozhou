# 墨舟交付前独立审查

审查日期：2026-10-28。范围：`docs/design.md`、Core/Codex/Desktop 全部生产源代码及 Core/Codex 测试。没有基准提交，按首版实现审查。未修改生产代码，未操作实际桌面，未执行模型推理。

## 修复后复核结论

父任务修复后再次检查了导出、损坏文件恢复、聊天记录校验、完整副本路径和编辑框撤销隔离，并对新的 Release DLL 运行回归。**本次审查发现的四项问题均已在源码中处理；复核范围内没有剩余可确认的阻断缺陷，代码质量审查通过。** 下面保留首次发现记录，不能将历史问题误读为当前仍未修复。

| 原问题 | 修复与复核结果 |
| --- | --- |
| P1 冲突后导出绕过保护 | 桌面不再直接写出正文，始终调用 Store 导出。实际桌面 Flush 冲突后，四类内部路径均拒绝导出；安全目标包含未保存正文，外部主文件字节不变。 |
| P2 损坏启动恢复不可达 | OpenFolder 在针对 JSON/结构/缺失文件的加载异常时，检测 `.bak` 并询问恢复；恢复使用失败目录的 Store。启动和默认示例也识别仅备份存在的目录。已审查接线，并实测 Core 恢复及保留损坏现场；原生确认对话框点击流程由父任务负责，本次未点击。 |
| P2 无效聊天阻断新请求 | 校验集合、元素、文本及角色后才加入历史；失败时清空内存历史、保留原文件并显示解释。`[null]`、`null`、null 文本、无效角色四类输入均实测通过，新 BuildRequest 正常且 PersistChat 不覆盖原记录。 |
| P2 非正文冲突修改无保全路径 | 新增“另存当前小说副本（含全部未保存内容）”。直接序列化整个当前项目，包含全部 Entries/Relations，保存到新目录后才切换；不要求原项目 Flush 成功。底部提示和切换确认均引导此路径。源码审查确认不遗漏模型字段；原生目录选择/切换整条 UI 流程未由本次 harness 点击。 |

另实测五个编辑框在条目切换后 `CanUndo == false`，避免将上一条目编辑撤销进新条目。

复核命令：`dotnet run --project ../ReviewChecks/ReviewChecks.csproj -c Release -m:1 --no-restore`。实际结果：**退出码 0，26 条 PASS**。引用父任务最新构建的桌面 DLL，检查真实 MainWindow 的聊天加载/上下文/Flush/SelectEntry，以及关联 Store 的真实文件读写。未使用真实模型，未操纵桌面。该局部复核不替代父任务原生界面验收和完整测试套件。

### 随源码交付的可复现回归项目

相同的 26 项检查现已纳入 `Mozhou.Desktop.Tests/Program.cs`，项目通过 `ProjectReference` 引用 `Mozhou.Desktop/Mozhou.Desktop.csproj`，不再依赖 scratch harness 或预先存在的 DLL 路径。Windows/.NET 8 SDK 环境下，在源码根目录运行：

```powershell
dotnet restore Mozhou.Desktop.Tests/Mozhou.Desktop.Tests.csproj --disable-parallel -p:BuildInParallel=false -m:1
dotnet run --project Mozhou.Desktop.Tests -c Debug -p:BuildInParallel=false --no-restore
```

交付项目实际运行结果：**退出码 0，26/26 passed**。测试文件和测试设置仅写入自身 `bin/Debug/net8.0-windows` 下，不打开窗口、不调用模型；失败断言会以非零进程退出。采用 Debug 是为了与父任务同时进行的 Release 原生界面验收隔离：最初尝试 Release 遇到运行中 Mozhou.exe 文件锁（MSB3027），随后 Debug 完整构建和测试成功。关闭正在运行的对应配置应用后，也可将命令中的 Debug 改为 Release。

### 父任务补充的原生界面证据

以下为父任务实际执行后报告的结果，与上述独立 26 项检查分开记录：损坏小说启动时出现恢复确认，点击“是”后主文件有效，原始 `{corrupt-ui-test` 内容在 backups 中保留；外部修改主文件标题后编辑正文产生保存冲突，通过项目菜单“另存副本”和目录选择器成功保存 9 条目、4 条关系及未保存正文标记，原文件外部标题不变。父任务还报告真实 ChatGPT 连接、一条小型模型生成、结果保存为笔记、图节点拖动持久化已通过。审查者没有重复进行这些交互或推理。

## 首次审查结论（历史，已修复）

主要功能已经实现，结构清晰；本地小说与 Codex 协议分离、正文原子替换与基线比较、AI 回复绑定发起时条目、生成期间禁止切换小说、每次发送新建临时线程等设计符合要求。但下列数据保护与恢复缺陷建议交付前修复，因此本次质量结论为 **需要修改后复核**。这不是对父任务后续修复结果的评价。

规格覆盖：正文/设定编辑、目录排序/搜索、人物关系编辑和持久化、导出与备份、ChatGPT 认证检查、流式/取消/手动采用均有实现。损坏小说恢复在 Core 可行，但 UI 路径未接通；冲突后的完整内存数据保全存在缺口。实际桌面布局、拖动与登录/一条真实生成由父任务验证，本次不作已通过声明。

## 首次问题明细（历史行号）

### 1. [P1] 保存冲突后的导出绕过内部数据路径保护

位置：`Mozhou.Desktop/MainWindow.Project.cs:49-52`，特别是第 52 行。

场景：外部修改或删除项目文件，编辑器存在待保存修改，`Flush()` 返回 false；用户按底部提示导出。该分支直接 `File.WriteAllText(dialog.FileName, ...)`，完全绕过 `ProjectStore.ValidateDestination` 和临时文件原子替换。保存对话框过滤器不是路径访问限制；显式选择当前 `project.mozhou.json`、`.bak`、`assistant-chat.json` 或 `backups` 下文件并确认覆盖，会用正文文本破坏对应 JSON/恢复副本。正常导出分支已有明确保护，恰好在最需要保护的冲突路径失效。

修正方向：把从内存导出也放进同一受保护、原子写入的 Store 接口，不在桌面错误处理分支直接写任意目标。测试应覆盖“保存被拒绝之后”的导出，而不仅是当前 Core 正常导出。

证据：生产分支直接静态可达；Core 测试仅验证 Store 导出路径，没有覆盖此桌面绕过。未自动操纵 SaveFileDialog。

### 2. [P2] 无法从 UI 恢复启动时已经损坏的小说

位置：`Mozhou.Desktop/MainWindow.xaml.cs:47-48`、`Mozhou.Desktop/MainWindow.Project.cs:66`。

场景：用户上次小说的主 JSON 损坏，但 `.bak` 完整。`OpenFolder` 在 `store.Load()` 抛异常后尚未赋值 `_store`；首次启动保持 `_store == null`。用户点击“恢复上一次保存”，`RestoreClick` 第 66 行立即返回，没有提示也没有恢复。重新选择相同目录仍然先 Load 失败。Core 的 `RestorePrevious()` 本来明确支持恢复损坏主文件，但该能力无法通过应用使用。

修正方向：打开失败时针对失败目录提供显式恢复入口，或保留独立的待恢复 Store；不可误恢复此前打开的其他小说。验证损坏文件、有效备份、UI恢复、重新读取和保留损坏现场的整条路径。

证据：独立 STA 反射检查调用实际已构建 MainWindow：损坏 OpenFolder 抛 JsonReaderException，`_store == null`，调用 RestoreClick 后文件仍为原损坏文本；同目录 `new ProjectStore(dir).RestorePrevious()` 成功恢复。

### 3. [P2] 含 null 条目的损坏聊天记录使整个小说无法再发送 AI 请求

位置：`Mozhou.Desktop/MainWindow.Assistant.cs:171-175`，后续失败点第 70 行。

场景：`assistant-chat.json` 是合法 JSON `[null]`。反序列化成功，并先把 null 加入 `_chat`；渲染消息时异常被 catch，标记损坏，却没有移除无效内存数据。之后查看上下文或发送请求都会在 `_chat.Where(x => x.Complete)` 抛 NullReferenceException。错误发生在 SendClick 的生成 try/catch 之前；全局异常处理能保住应用，但用户只能手工修复文件才能继续用 AI。

修正方向：先验证整份记录（非 null 集合、非 null 元素、合法字段）再替换 `_chat`；加载失败应保留原文件并清空/隔离无效内存历史。对 `[null]`、`null`、空/异常字段都增加检查。

证据：独立 STA 反射检查直接调用实际 ClearChatForProject，输出 `_chatDamaged=True`，随后 BuildRequest 抛 NullReferenceException。

### 4. [P2] 冲突提示推荐的导出不能保全设定与关系图修改

位置：`Mozhou.Desktop/MainWindow.xaml.cs:127`，`Mozhou.Desktop/MainWindow.Project.cs:51`、第 60 行。

场景：用户修改人物、世界观、大纲、笔记或关系图时主文件发生外部变化，保存暂停。底部明确要求“先导出当前内容，再重新打开项目”，但失败保存后的导出仅筛选 `Kind == chapter`，不包含任何这些待保存内容。ZIP 备份又因 `!Flush()` 直接返回。按提示导出成功后重新打开，会丢弃尚未保全的非正文修改。

普通 Markdown/TXT 只导出正文符合设计；问题在于把它作为所有修改的冲突恢复路径。应增加保存完整内存项目副本的路径，或至少明确提示各类未保存修改不能由正文导出保全，并提供可操作的保全方法。

证据：状态提示、导出筛选和 ZIP 守卫的直接代码路径；未执行有意丢弃用户修改的 UI 操作。

## 验证与边界

- 读取现有 Core 17 项与 Codex 12 项测试实现；父任务已报告通过，本次未重复执行这些全套测试，也未把父任务报告当成本次独立测试结果。
- 新增独立 scratch harness `../ReviewChecks`，直接引用已构建桌面/Core/Codex DLL，无第三方包，不修改生产项目。命令：`dotnet run --project ../ReviewChecks/ReviewChecks.csproj -c Release -m:1 -p:RestoreIgnoreFailedSources=true`，退出码 0，复现上述问题 2、3。
- 未发现可确认的跨小说 AI 结果误追加：回复闭包带 projectId 和原 entryId，切换小说清除卡片，生成期间打开/新建小说被禁止。未发现确定的图编辑保存或协议取消的新阻断缺陷。
- 协议测试是受控子进程；本次没有重新验证实际官方模型、配置合并或网络服务行为，也没有声称这些当前服务行为已由本次确认。
