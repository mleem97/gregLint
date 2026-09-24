# GregLint — static analysis + auto-fix for gregCore mods

Offline, deterministic, never loads assemblies (Mono.Cecil read-only).
Catch broken mods **before** they touch the game — locally and in CI.

Part of the GregFramework: framework gregCore (`github.com/mleem97/gregCore`),
user guide `.wiki/Developer-GregLint.md` (in the gregCore repo).

## Quickstart

Requirements: .NET 8 SDK.

```bash
dotnet run --project GregLint -- /path/to/Mods
```

Or publish once and use everywhere:

```bash
dotnet publish GregLint -c Release -o /opt/greglint
/opt/greglint/greglint /path/to/Mods
```

```bash
# Full check with JSON report, warnings fail the build:
greglint --severity-floor warning --format json Mods/ > lint.json

# Normalize mod.json files (with .bak backup):
greglint --fix MyMod/
```

Exit codes: `0` clean · `1` findings at/above floor · `2` usage/IO error.

## What it checks

IL rules (GL001–GL007): `SetQualityLevel` ban, `OnGUI` ban, Harmony
prefix signatures, patch exception guards, mod identity, `.deactivated`
ownership (gregCore only), no hard-coded `.deactivated` paths.
File rules (GL101–GL105): top-level Melon DLLs, no active duplicates of
disabled mods, `Mods/manifest.json` validation, `mod.json` schema +
recommended fields. Full catalog: [`GregLint/README.md`](GregLint/README.md).

## Tests & coverage

```bash
dotnet test GregLint.Tests
```

55 tests, fully offline (in-memory Cecil fixtures — no compilation, no
game). Measured **99.05% line / 90.81% branch**; every reachable line is
covered, the 7 remaining defensive lines are documented in
[`GregLint/README.md`](GregLint/README.md). Methodology:
[`docs/coverage.md`](docs/coverage.md).

## Repository layout

```text
GregLint/          # the tool (net8.0 console, Mono.Cecil)
GregLint.Tests/    # xunit + FluentAssertions + coverlet
docs/              # methodology and guides
```

## Contributing

Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:` …), one logical
change per commit. No force-push, no history rewrite. Docs
(`README.md` + `docs/` + `CHANGELOG.md` Unreleased) stay in sync with
features. Security issues follow `SECURITY.md` — never a public issue.
