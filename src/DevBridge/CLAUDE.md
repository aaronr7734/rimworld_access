# DevBridge Module (DEBUG-ONLY)

## Purpose
A localhost HTTP server that lets external tooling (Claude Code via `curl`) run arbitrary
C# against the **live, running game** for verification and inspection. This exists to kill
the project's #1 recurring failure: guessing a label/key/mechanism instead of checking it.
Decompiled source and XML get close; this confirms what the game actually returns at runtime
(e.g. `"ViewEntityCodex".Translate()`, whether a pawn carries a given gizmo, a def's real
label).

## CRITICAL: Debug builds only
Every file in this module is wrapped in `#if DEBUG`, and the Roslyn package + its runtime
DLLs are confined to the Debug configuration in `rimworld_access.csproj`. The Release package
ships **only** `rimworld_access.dll` — verified: no Roslyn, no `System.*` deps. Players never
receive any of this. Any mono/Roslyn compatibility wrinkles affect local Debug builds only.

## Files
- **MainThreadDispatcher.cs** — marshals work from the HTTP thread onto RimWorld's main
  thread (game state is not thread-safe). `RunOnMainThread<T>` enqueues and blocks until the
  next frame drains it; 30s timeout so a frozen game fails the request instead of hanging.
- **RoslynEvaluator.cs** — compiles + runs script text via `CSharpScript.EvaluateAsync`.
  References every on-disk loaded assembly (so `Find`, `DefDatabase`, etc. resolve) and
  imports Verse/RimWorld/UnityEngine/etc. Returns the trailing expression's value; captures
  Console output. MUST run on the main thread.
- **DevBridgeServer.cs** — `HttpListener` bound to `127.0.0.1` only. Endpoints below.
- **DevBridgePatch.cs** — Harmony postfix on `UIRoot.UIRootOnGUI` that lazily starts the
  server and drains the main-thread queue every frame (works at main menu and in-game).

## Endpoints
- `GET  /health` — liveness + `roslyn=<bool>` + `programState` (Entry / Playing).
- `POST /eval` — request body is C# script text; returns `OK\n<result>` or `ERROR\n<detail>`.
- `GET  /eval?code=...` — same, for url-encoded one-liners.

Port defaults to **8787**; override with the `RWA_DEVBRIDGE_PORT` env var.

## Usage (from a shell on the same machine while the Debug game runs)
```bash
curl -s http://127.0.0.1:8787/health
curl -s -X POST --data-binary 'return "ViewEntityCodex".Translate().ToString();' http://127.0.0.1:8787/eval
curl -s -X POST --data-binary 'return DefDatabase<ConceptDef>.AllDefs.Count();' http://127.0.0.1:8787/eval
```
The script's last statement should `return` the value to inspect. Imports already in scope:
System, System.Linq, System.Collections(.Generic), System.Text, Verse, RimWorld,
RimWorld.Planet, UnityEngine, RimWorldAccess.

## Notes
- Scripts run on the main thread, so a heavy/looping script briefly blocks the game frame.
- The result formatter expands non-string `IEnumerable` one item per line, capped at 500.
- Two patches share `UIRootOnGUI` (UnifiedKeyboardPatch prefix + this postfix) — fine.
