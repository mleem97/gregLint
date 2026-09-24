# GregLint — static analysis + auto-fix for gregCore mods

Offline, deterministic, never loads assemblies (Mono.Cecil read-only).
For modders: catch broken mods **before** they touch the game.
For gregCore: the automated quality gate behind the mod collection.

```bash
dotnet run --project GregLint -- /path/to/Mods
# or publish once:
dotnet publish GregLint -c Release -o /opt/greglint
greglint /path/to/Mods
```

## Usage

```text
greglint [--format text|json] [--severity-floor info|warning|error]
         [--fix] [--rules GL001,GL002,...] <path>...
```

| Input | Meaning |
|---|---|
| mod folder / game `Mods/` dir | Full check (IL + layout + manifests) |
| single `.dll` | Only findings for that file (+ its dir's tree findings) |

| Flag | Effect |
|---|---|
| `--format json` | Machine output (`tool/version/findings/appliedFixes/summary`) |
| `--severity-floor` | Exit 1 from this severity up (default: `error`) |
| `--fix` | Normalize `mod.json` files (sorted keys, indent; `.bak` backup first). IL findings are **report-only** — rewriting IL risks breaking signatures. |
| `--rules` | Run only these rule IDs (comma-separated) |

Exit codes: `0` clean · `1` findings at/above floor · `2` usage/IO error.
All output is sorted — identical inputs produce identical reports.
CI example: `greglint --severity-floor warning --format json Mods/ > lint.json`.

## Rules

### IL rules (managed DLLs, Cecil read-only)

| ID | Sev | Rule |
|---|---|---|
| GL001 | Error | No `QualitySettings.SetQualityLevel` (stomps game tiers — use granular levers with snapshot/restore) |
| GL002 | Error | No `OnGUI` methods (stripped in IL2CPP — use UIToolkit) |
| GL003 | Warning | Harmony prefixes return `void`/`bool` (`false` skips the original) |
| GL004 | Warning | Patch methods carry exception handling (SafePatch convention) |
| GL005 | Warning | Mod identity present (`MelonInfo` / `GregMod` / `IGregNativePlugin`) |
| GL006 | Error | Only gregCore touches `GregModActivationService` (`.deactivated` moves) |
| GL007 | Warning | No hard-coded `.deactivated` paths (loaders must never read them) |

### File/layout rules (no game needed)

| ID | Sev | Rule |
|---|---|---|
| GL101 | Error | Melon mods live directly in `Mods/` (no subfolders; `gregNative` tree excepted) |
| GL102 | Warning | No duplicate active copy of a deactivated mod (same filename active + `.deactivated`) |
| GL103 | Error/Warning | `Mods/manifest.json` valid: `.dll` only, exists, no traversal, never `.deactivated` (errors: broken JSON, traversal, deactivated refs) |
| GL104 | Error | `mod.json` schema: valid JSON, required `id`, entry file exists; `--fix` normalizes formatting |
| GL105 | Warning | `mod.json` recommended `name` / SemVer `version` |

Native/unmanaged DLLs are skipped for IL rules (file rules still apply).
`.deactivated` trees are never scanned for loading — except by GL102,
which exists precisely to catch duplicates.

## Extending

```csharp
var engine = new Engine(
    extraAssemblyRules: new IAssemblyRule[] { new MyRule() },
    extraTreeRules: new ITreeRule[] { new MyDirRule() });
```

Rules are isolated: a throwing rule yields a warning, never aborts the run.

## Coverage

`GregLint.Tests` (xunit + FluentAssertions, fully offline — fixtures are
built in-memory with the Cecil API, no compilation, no game):

```bash
dotnet test GregLint.Tests
dotnet test GregLint.Tests -p:CollectCoverage=true \
  -p:CoverletOutputFormat=cobertura -p:CoverletOutput=coverage/
```

Measured: **99.05% line / 90.81% branch**. Every reachable line is
covered. The 7 remaining lines are defensive single-statements proven
unreachable-by-construction (verified by probe, not by belief):

- `Program.cs`: `bad-path` / engine-failure catches — `Exists` checks
  return `false` instead of throwing; the engine isolates per rule/file.
- `Cecilx.cs`: outer metadata guards — in-memory models never throw on
  member access (probed: disposed modules, null scopes); corrupt on-disk
  modules are already rejected at read time (tested: truncated DLLs).
