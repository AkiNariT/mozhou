# 墨舟 Implementation Plan

> **For agentic workers:** Use subagent-driven-development to implement the independently owned tasks below; run behavior checks before completion.

**Goal:** Deliver a usable Windows local novel editor with a character graph and embedded Codex assistant.
**Architecture:** Three .NET 8 projects: Core owns local persistence and context, Codex owns the app-server protocol, Desktop owns WPF UI. No external packages. Shared model contracts are in Mozhou.Core/Models.cs.
**Tech Stack:** .NET 8, WPF, System.Text.Json, Process/stdio, ZIP.
**Spec:** docs/design.md

## Global Constraints
- Chinese UI; local manuscript persistence; existing ChatGPT Codex login only.
- Never read or copy credential files, change the global Codex config, or silently use API-key billing.
- Safe saves with backups and refusal to overwrite external changes.
- AI text is a suggestion until the user explicitly adopts it.

## Task 1: Persistence and novel context
Files: Mozhou.Core/ProjectStore.cs, ProjectContent.cs; Mozhou.Core.Tests/{csproj,Program.cs}.
Consumes Models.cs. Produces ProjectStore(string folder), Folder, FilePath, Load(), Save(NovelProject), CreateBackup(string destinationZip), RestorePrevious(), ExportMarkdown(string destination), ExportText(string destination). ConflictException indicates another writer changed the file. Load records the baseline, Save must not silently clobber a different disk state. Maintain last known good .bak and timestamp backups (bounded to 30). Load errors preserve disk contents.
Produces ProjectContent.CreateEmpty(string title), CreateDemo(), CreateEntry(string kind), BuildPrompt(NovelProject,NovelEntry?,string instruction,bool includeSettings), CountWords(string). Count Chinese non-whitespace characters and Latin words consistently for UI; document semantics. Context includes current entry first; optional settings are character/world/outline, not unrelated chapter prose. Cap context, disclose truncation.
- [x] Write a dependency-free console test runner exercising real temp directories: Unicode roundtrip, title rename, external conflict, .bak recovery, invalid JSON preservation, relationship links and graph positions, ZIP contents, export ordering, context excludes other chapter text.
- [x] Run tests with initial throwing service stubs and record expected failures.
- [x] Implement file replacement and context behavior; run tests to exit 0.

## Task 2: Codex transport
Files: Mozhou.Codex/CodexClient.cs and supporting files; Mozhou.Codex.Tests/{csproj,Program.cs}.
Contract: CodexClient(string executablePath,string workingDirectory), static FindExecutable():string?, ConnectAsync(CancellationToken):Task<CodexStatus>, Models:IReadOnlyList<CodexModel>, GenerateAsync(string prompt,string? model,Action<string>? onDelta,CancellationToken):Task<string>, DisposeAsync(). CodexStatus(bool IsChatGpt,string Message,string? PlanType=null); CodexModel(string Id,string DisplayName,bool IsDefault).
- [x] Inspect locally generated app-server schema and official docs; confirm initialize, account/read, model/list, thread/start, turn/start, turn/completed, delta and interrupt contracts.
- [x] Write tests with an external fake protocol process for streaming completion, API-key auth rejection, server error, early exit and cancellation. No mock-only assertions.
- [x] Implement child process with ArgumentList, no shell, bounded stderr, RPC pending routing, process cleanup, timeouts. Child inherits official login; reject account/read if not ChatGPT. Explicit readOnly sandbox and no approval, disable tool execution where documented. Prompts are fiction tasks, not repository work. Serialize generations. Stop only own child.
- [x] Verify schema handshake with installed Codex. Only controller runs the actual tiny inference to avoid duplicate quota use.

## Task 3: Desktop workbench (controller)
Files: Mozhou.Desktop/App.xaml(+cs), MainWindow.xaml(+cs), GraphView.cs, Dialogs.cs, SettingsStore.cs.
- [x] Build native shell with top project controls, left navigation/list, central editor, right assistant; create/open project folder and clear onboarding.
- [x] Connect editor autosave (750ms), flush on navigation/close, failure state remains visible; Ctrl+S; entry rename, summary, category, tags, chapter order, search, confirmation before delete.
- [x] Render draggable character cards and labelled directional relations; add/edit/remove edges, focus selected person, open its profile. Save node positions on drag end.
- [x] Hook connection/model controls, selected-context preview, streaming assistant, cancel, copy/append/save-as-note; show actual account connection and errors. Store chat draft transcript per project locally without credentials.
- [x] Add word counts, focus mode, export Markdown/TXT, ZIP backup and previous save recovery.

## Task 4: Validation and packaging
- [x] Run both real test harnesses and clean Release build.
- [x] Review storage/transport/UI ownership boundaries and fix actionable findings.
- [x] Use native UI automation to verify edit/save/reopen, graph operations and AI generation. Inspect native screenshots and record results in docs/acceptance.txt.
- [x] Publish framework-dependent app for this machine to outputs/Mozhou; create source ZIP without bin/obj; include Chinese quickstart and tested limitations.

## Interface scan
Task 1 ↔ Task 3: Models classes are mutable POCOs; UI mutates on dispatcher; Save synchronous and serialized by UI, no simultaneous writes.
Task 2 ↔ Task 3: async client callbacks may be worker-thread; UI marshals deltas; CancellationToken drives turn interruption.
Task 1 ↔ Task 2: no shared source; only complete prompt string crosses at Desktop.

## Rulings and progress
Ruling: new project folder is isolated; no existing Git repository/worktree to reuse. Keep all scratch/build work under work/Mozhou and publish deliverables under outputs.
Ruling: independently owned Core and Codex files may be implemented in parallel to reduce time; Desktop is controller-owned, shared contracts fixed above.
Ruling: user's instruction to proceed with local app and confirmed embedded Codex settles implementation scope; no further generic approval gate is necessary.
