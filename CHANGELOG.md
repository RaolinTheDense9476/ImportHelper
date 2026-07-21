# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- `.github/workflows/release.yml`: pushing a `v*` tag now automatically builds self-contained CLI + GUI binaries for all four platforms, archives them, and creates the GitHub Release with the matching `CHANGELOG.md` section as notes — the same process previously run by hand.

## [1.4.0] - 2026-07-16

### Added

- The CLI now returns a real process exit code: `0` on success, `1` if a file wasn't found, no files matched the pattern, required arguments were missing, or a row had the wrong number of columns — usable as a "is this file well-formed?" check in scripts/CI. `-Help` always exits `0`.

### Fixed

- Previously, `Main` was `void` and the CLI always exited `0` regardless of outcome — malformed files, missing files, and even missing required arguments all silently reported success to the calling process.

### Removed

- The Debug-only `Console.ReadKey()` "Press any key to exit" prompt, which crashed with an unhandled exception whenever stdin was redirected (e.g. piped input, CI, non-interactive shells).

## [1.3.0] - 2026-07-16

### Added

- `-DestinationDirectory <path>` (CLI) and a matching "Destination directory" field with a folder browser (GUI): where to write generated files, instead of always assuming the same directory as each input file. Created automatically if it doesn't exist. Left blank/unset, behavior is unchanged.

## [1.2.0] - 2026-07-16

### Added

- `ImportHelper.Gui`: a cross-platform desktop GUI ([Avalonia](https://avaloniaui.net/)), exposing the same options as the CLI as form fields, with a log pane, a completion popup showing each file's column analysis (name/type/max length), and automatic output-folder opening once the popup is dismissed. Runs identically on Windows, Linux, and macOS from one codebase. Unlike the CLI's `-HasHeader` (which defaults off), the GUI's "First row has column headers" checkbox defaults checked — leaving it off on a file that does have a header row treats the header text as a data row, which fails numeric/date parsing and makes every column infer as `String`. A checkbox next to the file pattern's Browse button switches it between picking a single file or a whole folder.

### Changed

- Restructured into a `src/` solution of three projects: `ImportHelper.Core` (the engine — CSV parsing, type inference, target definitions, generation logic, with no UI or argument-parsing code), `ImportHelper.Cli` (the existing command-line tool, now a thin wrapper over `Core`), and `ImportHelper.Gui` (the new GUI, also a thin wrapper over `Core`). CLI behavior and output are unchanged — verified byte-for-byte identical against the pre-refactor version.
- `src/ImportHelper.Core/targets/mssql.yaml` is the new location of the bundled reference target (previously `targets/mssql.yaml` at the repo root); it still ships as `targets/mssql.yaml` next to both the CLI and GUI executables.

## [1.1.0] - 2026-07-16

### Added

- Pluggable YAML-based database target definitions (`-Target <name_or_path>`). What's genuinely data (type names, identifier quoting, the `CREATE TABLE` template, the bulk-import command template, encoding-flag rules) now lives in a target's YAML file instead of hardcoded C#; what's genuinely behavior (CSV parsing, determining whether a numeric column is really an integer) stays in code.
- `targets/mssql.yaml`: the built-in reference target, reproducing the tool's original SQL Server / `bcp` behavior exactly, and serving as the template for adding another database system or bulk-import tool.
- `-GenerateBcpFormat` now checks whether the selected target defines a format-file section (not every bulk-import mechanism has an analogous step) and says so instead of generating anything when it doesn't.

### Fixed

- `-Delimiter "\t"` previously took the literal backslash character, since no shell expands that escape inside quotes. Delimiters now also accept the two-character escapes `\t`, `\n`, `\r`, and `\\`.

### Changed

- Repository-scoped `NuGet.Config` pins package restore to `nuget.org`, so builds don't depend on whatever NuGet feeds happen to be configured on a given machine.

## [1.0.0] - 2026-07-15

Initial public release.

### Added

- Column type inference (`String`, `Numeric`, `Date`) and maximum-length detection by scanning an entire delimited file.
- Standard CSV-style quoting support: quoted fields, `""`-escaped quotes, and delimiters or newlines embedded inside quotes.
- `-GenerateTsql`: generates a `CREATE TABLE` script plus a ready-to-run `bcp` command, with the command's `-c`/`-w` mode and code page derived from the encoding actually used to read the file (instead of assuming raw/native), and `-F 2` included automatically when `-HasHeader` was specified.
- `-GenerateBcpFormat`: generates a bcp `.fmt` format file.
- `-AllowEmbeddedNewlines`: suppresses the warning printed when a quoted field contains an embedded newline (most import tools, including native `bcp`, don't handle this correctly).
- `-ForceQuotedAsString`: forces quoted values to `String` instead of the default of still attempting Numeric/Date inference on them, for sources that quote every field indiscriminately.
- `-PrepareForBcp`: writes a `bcp_<filename>` copy with quoting stripped and any delimiter/newline that was protected inside quotes replaced with a configurable value, since native `bcp` has no CSV-quoting awareness of its own and otherwise miscasts or misaligns quoted rows.
- MIT license.
- Verified to run on Linux/macOS as well as Windows; row parsing already treats CRLF, LF, and CR uniformly, including files that mix line-ending styles.
