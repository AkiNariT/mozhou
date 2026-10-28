# Core 实现报告

实现范围：`Mozhou.Core/ProjectStore.cs`、`ProjectContent.cs`、`Mozhou.Core.Tests/`。未改动共享 Models、桌面层或 Codex 协议层；未访问网络或凭据。

## 公开接口

```csharp
public sealed class ConflictException : IOException;
public sealed class ProjectStore
{
    public ProjectStore(string folder);
    public string Folder { get; }
    public string FilePath { get; }
    public NovelProject Load();
    public void Save(NovelProject project);
    public NovelProject RestorePrevious();
    public void CreateBackup(string destinationZip);
    public void ExportMarkdown(string destination);
    public void ExportText(string destination);
}
public static class ProjectContent
{
    public static NovelProject CreateEmpty(string title);
    public static NovelProject CreateDemo();
    public static NovelEntry CreateEntry(string kind);
    public static string BuildPrompt(NovelProject project, NovelEntry? current,
        string instruction, bool includeSettings);
    public static int CountWords(string text);
}
```

## 数据与恢复行为

- 文件名固定为 `project.mozhou.json`。已有文件必须先成功 Load，Save 才能覆盖；逐字节比较保存基线，能检测相同大小与修改时间的外部改写及外部删除。
- 同目录临时文件写入并 Flush(true)，首次用禁止覆盖的 Move，后续用 File.Replace 原子替换。保存前验证数据版本、必要结构、条目编号、有限坐标和关系端点。
- `project.mozhou.lock` 以独占共享模式避免两个墨舟实例同时写入；该小文件保留在小说目录，可忽略。
- `.bak` 为上一次可读保存；`backups/project-时间-随机编号.json` 保留最多 30 份。清理在正文替换前完成，保存失败仍保留正文与 `.bak`。
- Load 错误不会改动磁盘。RestorePrevious 先验证 `.bak`，保存恢复前的现场到时间戳备份，再原子替换，返回恢复后的模型。恢复前现场可能是损坏文件，`.bak` 保持可读。调用者应先取得用户恢复确认。
- 支持 UTF-8 BOM。JSON 保留可读中文与 Unicode 字符。
- ZIP 仅包含小说主文件、存在的 `.bak`、内部 JSON 备份、存在的 `assistant-chat.json`，不扫描其他本地目录。
- Markdown/TXT 按 Entries 中章节的顺序输出分卷、章名和正文；不导出设定。导出使用当前 Store 持有的模型，桌面调用前应刷新编辑；不要求保存成功，外部冲突后仍可直接导出内存草稿。所有导出拒绝覆盖项目、聊天文件和内部备份路径。

## 内容与计数

- 工厂支持 `chapter`、`character`、`world`、`outline`、`note`。示例含一章、四个人物、世界观、大纲、灵感及四条有向关系；人物坐标在 1000×650 画布内。
- BuildPrompt 首先放当前条目；启用设定时依次附加小说标题/简介、人物关系网、character/world/outline。关系以人物名字、`来源 → 目标`、Label 与 Note 展示，保留方向。当前条目不重复发送，其他章节与灵感不进入上下文。关闭设定时不附带小说资料、其他条目或关系网。
- 总长度不超过 24,000 个 UTF-16 代码单元；用户指令最多 4,000，截断时明确说明。切割不会拆开代理项对。当前条目过长可能耗尽设定预算，仍会显示截断提示。
- CountWords 按 Unicode 标量逐个统计汉字；其余连续字母或数字算一个词，词内连字符、撇号及组合音标不另拆词。空白、其他标点与表情不计数。例如 `中文，hello world！𠀀 test-case 123` 为 7。

## 实际验证

先建立抛出 NotImplementedException 的服务桩，运行控制台测试：预期 **0/12 通过**。实现后扩展了结构验证、BOM、被锁文件、失败时备份上限及导出目标保护的回归检查；发现的问题均先记录失败再修复。后续补充小说资料与人物关系上下文、冲突草稿导出三项检查：先运行得到 **18/20 通过**（两项新增上下文测试预期失败；既有草稿导出行为已满足），再实现关系上下文。

最终运行：

```powershell
dotnet run --project Mozhou.Core.Tests -c Release --no-restore
```

结果：**退出码 0，20/20 通过**。无外部 NuGet 包。测试在 `Mozhou.Core.Tests/bin/.../test-data/随机目录` 创建真实临时小说，结束后删除；OS TEMP 在当前受限环境中允许写入但拒绝重命名，因此测试目录限定在工作区内。

覆盖：Unicode/改名/关系坐标重开；相同大小与时间戳外部冲突；删除冲突；上一版恢复与 30 份上限；损坏 JSON 不改动并从有效 `.bak` 恢复；无效版本/结构/备份与悬空关系拒绝；UTF-8 BOM；锁定目标保存失败保持数据与清理临时文件；ZIP 内容和聊天记录；导出数据路径保护/顺序/正文范围；冲突后内存脏稿 Markdown/TXT 导出且不改写所有内部文件；上下文隔离/长度/截断/小说资料/有向人物关系与关闭设定隔离；混合中英文计数；示例关系有效性。

## 限制

- 未进行断电或磁盘写满故障注入；已通过真实文件锁验证替换失败路径。
- 墨舟实例通过共享独占锁序列化；不遵守该锁的第三方进程，仍可能在最后一次内容检查与操作系统原子替换之间的极短窗口竞态改写。已在备份写入后再次比对，普通外部修改会拒绝保存。原子替换防止半写文件，不是跨任意外部写者的文件系统事务。
- 根据接口约定，同一 Store 的调用由桌面 UI 串行执行，模型不在保存过程中并发修改。
- Git 身份未配置，未创建局部提交。
