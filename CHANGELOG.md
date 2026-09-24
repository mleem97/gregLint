# Changelog — gregLint

Format: [Keep a Changelog](https://keepachangelog.com/en/1.0.0/). Version: see [`VERSION`](VERSION).

## [Unreleased]

## [1.0.0]

### Added

- Initial release (extracted from gregCore `tools/`): offline static
  analysis + auto-fix for gregCore mods.
- IL rules GL001–GL007 (Cecil read-only): `SetQualityLevel`/`OnGUI`
  bans, Harmony prefix signatures, patch guards, mod identity,
  `.deactivated` ownership, no hard-coded `.deactivated` paths.
- File rules GL101–GL105: top-level Melon DLLs, no active duplicates,
  `Mods/manifest.json` validation, `mod.json` schema + normalization
  (`--fix` with `.bak` backup).
- CLI: text/JSON output, `--severity-floor`, `--rules` filter,
  single-file mode; exit codes 0/1/2; custom-rule extensibility.
- 55 tests, 99.05% line coverage (see `docs/coverage.md`).
