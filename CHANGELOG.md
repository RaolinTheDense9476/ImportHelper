# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project uses [Semantic Versioning](https://semver.org/).

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
