# Coverage methodology — GregLint.Tests

Goal: every reachable line covered; anything else documented, never
hidden. Current measurement: **99.05% line / 90.81% branch**
(coverlet, `--exclude [GregLint.Tests]*`).

## How to measure

```bash
dotnet test GregLint.Tests -p:CollectCoverage=true \
  -p:CoverletOutputFormat=cobertura -p:CoverletOutput=coverage/
```

## How the tests stay offline

Fixtures are built in-memory with the Mono.Cecil API
(`GregLint.Tests/Fixtures.cs`): types, attributes, method bodies
(including hand-emitted IL for calls, strings, and pathological
metadata), then either analyzed directly (rule unit tests) or written
to temp `.dll` files (engine/CLI tests in temp dirs). No compilation
step, no game assemblies, no network.

Edge I/O is tested for real: exclusive file locks (unreadable
`mod.json`), read-only directories (failed auto-fix), `chmod 000`
subtrees (enumeration guards), NUL-byte manifest entries, symlink
loops, garbage `.dll` files, truncated assemblies.

## The 7 documented lines

Each was proven unreachable-by-construction with a probe (not by
belief); behavior is still verified by the passing suite:

| Location | Line(s) | Why unreachable |
|---|---|---|
| `Program.cs` | bad-path catch, engine-failure catch | `Exists` checks return `false` instead of throwing; the engine isolates per rule/file |
| `Cecilx.cs` | 2 loop braces + 3 outer metadata guards | In-memory models never throw on member access (probed: disposed modules, null scopes); corrupt on-disk modules are rejected at read time |

Rules for keeping it that way: new code paths need positive +
negative tests; new defensive catches need either a triggering test
(see `BoomRef` in `Fixtures.cs` for metadata-read failures) or a
justification row added to this table.
