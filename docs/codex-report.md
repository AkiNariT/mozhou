# Codex transport verification · 2026-10-28

## Scope and integration

Implemented the exact `CodexClient` / `CodexStatus` / `CodexModel` contracts in `Mozhou.Codex`. No NuGet dependencies. UI callbacks can run on the protocol reader thread; marshal callbacks asynchronously onto the dispatcher. Pass a dedicated, otherwise empty application working directory, not the manuscript folder. A failed/disposed process is deliberately not reused: dispose and create a new client to reconnect. `Models.Id` is the server's `model` slug, which is accepted by thread/turn creation.

Each send creates an ephemeral thread with explicit writing instructions, model, provider `openai`, approval policy `never`, read-only sandbox, no turn network, no dynamic tools and no environments. It unsubscribes afterward. Text is not written to manuscript files by the client. Child/thread settings disable command execution, shell snapshot, apps, plugins, hooks, browser/computer use, image viewing, multi-agent, skill search, memory tooling, workspace dependency injection and project instruction loading. MCP server overrides use an empty table. No global configuration is written. Standard SDK/CLI credentials remain managed by the official child process; this code does not read/copy token files or implement login. Parent `CODEX_*`, `OPENAI_*` and `AZURE_OPENAI*` environment variables are removed from the child to prevent task identity, pipe, sandbox or API-provider leakage. Official user profile and OS proxy environment remain available.

`account/read` must report `chatgpt` both when connecting and before each send. API key, Bedrock, GCP and missing authentication cannot generate. Unknown server tool/credential requests receive a protocol error; command/file approval requests receive a decline. These controls are defense in depth, not an OS promise of zero read access: the configured read-only sandbox itself permits reads, and app-server protocol/tool behavior may change. This build supports the inspected official CLI version; avoid upgrading it without rerunning the protocol checks.

Requests are serialized. Response routing correlates RPC ids and thread/turn ids. Streaming text, turn failure details and early process exit propagate to the caller. Cancellation sends `turn/interrupt`; if the start id is unknown or interruption does not finish promptly, only the owned process tree is stopped. Dispose stops that owned child and pending tasks. RPC timeout 45 seconds, generation timeout 10 minutes; interruption timeout 3 seconds. Output limits: 2 Mi characters per protocol line, 64 Mi characters per child, 1 Mi characters per answer, 250000 characters per prompt. Stderr is continuously drained with a 16384-character tail limit.

## Evidence

- Installed official Codex CLI: version 0.164.0-alpha.11.1, inspected locally. The machine-specific installation path is omitted from this repository.
- Inspected `codex app-server generate-json-schema --help`; generated experimental JSON schema under `work-protocol/schema` and inspected initialize, account, models, thread, turn, stream, approval, interrupt and unsubscribe schemas.
- `thread/start` uses `sandbox: "read-only"`; `turn/start` uses `sandboxPolicy: { "type": "readOnly", "networkAccess": false }`. These differ in spelling intentionally.
- Test-first harness: the initial 10 cases all failed on the throwing stub as expected. Added auth-switch regression first; it failed before implementing account revalidation.
- `dotnet run --project Mozhou.Codex.Tests` exited **0**, with **12 PASS** results: stream and unrelated-event filtering, API-key rejection, RPC error, early exit, cancellation and actual interrupt receipt, failed turn, actual approval decline, oversized stdout rejection, environment isolation, pending-generation disposal, account switch and cancellation before a start response.
- `dotnet build Mozhou.Codex.Tests -c Release` exited **0**, **0 warnings, 0 errors**.
- `dotnet run --no-build --project Mozhou.Codex.Tests -- --real-connect` outside the agent sandbox exited **0**. The official CLI handshake confirmed ChatGPT authentication and returned a nonempty model list. Account-specific plan information and the model catalog are omitted from this repository.
- The sandboxed attempt failed because the official CLI could not initialize its state database under the user Codex directory. The narrowly scoped, approved outside-sandbox connection test succeeded. That is an execution-environment restriction, not proof of a missing account.
- This agent ran **no inference**. Controller owns the single real writing request and desktop interaction acceptance check.

Fake server is the test executable itself started as a real redirected subprocess, not mocks of internal transport methods. Its scenario file lives in generated test working directories. `work-protocol/` is scratch and generated schema data; exclude it and all `bin/obj` from deliverable source ZIP.

## Official documentation actually opened

- [App Server](https://learn.chatgpt.com/docs/app-server) — initialization and thread/turn/stream/approval lifecycle.
- [Configuration Reference](https://learn.chatgpt.com/docs/config-file/config-reference) — child configuration, shell tool and app/tool options.

Local generated schema is authoritative for this installed alpha CLI. No API-key-backed OpenAI SDK was used.
