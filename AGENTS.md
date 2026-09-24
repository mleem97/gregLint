# AGENTS.md — Notes for AI Agents (gregLint)

Repo: [https://github.com/mleem97/gregLint](https://github.com/mleem97/gregLint) · License: Apache-2.0 · Version: see `VERSION`.

## Duties

1. **Read first:** `README.md`, `GregLint/README.md`, `docs/coverage.md` — only then make changes.
2. **Do not commit secrets** (keys, tokens, `.env`). Only via environment variables.
3. **Preserve history:** no `push --force`, no history rewrite without instruction.
4. **Verify changes:** `dotnet build GregLint` and `dotnet test GregLint.Tests` before reporting done. Coverage for new/changed rules: `dotnet test GregLint.Tests -p:CollectCoverage=true -p:CoverletOutputFormat=cobertura`.
5. **Keep docs in sync:** rule changes update `GregLint/README.md` (catalog) + `CHANGELOG.md` (Unreleased) in the same change.
6. **Conventions:** Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:` …), one logical change per commit.
7. **If unsure:** stop and ask instead of guessing — especially for deletes, rule-severity changes, CI.

## Rules for rules

- New rules get an ID from the next free slot (GL0xx IL, GL1xx files), a severity with justification, a positive AND negative test, and a catalog row.
- IL rules are read-only (Cecil, never load assemblies); findings carry a concrete fix hint.
- Auto-fix (`--fix`) only rewrites with a `.bak` backup first. IL findings stay report-only.
- Output stays deterministic (sorted); exit-code contract in `GregLint/README.md` is frozen — changing it is breaking.

## Layout

- `GregLint/` — tool (`Program.cs` CLI, `Engine.cs`, `Finding.cs`, `Report.cs`, `Rules/`).
- `GregLint.Tests/` — fixtures (`Fixtures.cs`, in-memory Cecil) + rule/file/CLI/coverage tests.
- `docs/coverage.md` — coverage methodology.
