# ImportHelper

A utility that analyzes delimited text files (CSV, TSV, etc.) and optionally generates a `CREATE TABLE` script, a bulk-import command, and bcp-ready cleaned copies for importing into a database. The target database/bulk-import tool is a YAML definition, not hardcoded — Microsoft SQL Server ships as the built-in reference target, but you can add another database system or import mechanism without touching the C# code. Available as a command-line tool and as a cross-platform desktop GUI, both built on the same underlying engine.

## Features

- Infers each column's data type (`String`, `Numeric`, `Date`) and maximum length by scanning the entire file
- Standard CSV-style quoting support: quoted fields, `""` escaping, and delimiters/newlines embedded inside quotes
- Generates a `CREATE TABLE` script with inferred types, plus a ready-to-run bulk-import command — both driven by a swappable [target definition](#database-targets)
- Generates BCP format files (for targets that define one)
- `-PrepareForBcp` rewrites a file to be safe for a plain `bcp -c` import, which has no CSV-quoting awareness of its own
- A CLI for scripting/automation and a GUI for interactive use ([see below](#gui)), sharing one core engine so behavior is identical either way

## Download

Prebuilt, self-contained binaries (no .NET runtime required) for Windows, Linux, and macOS (x64 and arm64) are attached to each [GitHub Release](https://github.com/RaolinTheDense9476/ImportHelper/releases/latest).

## Requirements

Only needed if you're building from source rather than using a prebuilt binary above:

- .NET 8 SDK to build and run
- The `bcp` utility and a SQL Server instance if you intend to use the generated import commands

## Project layout

This is a solution of three projects under `src/`:

| Project | What it is |
| --- | --- |
| `ImportHelper.Core` | The actual engine — CSV parsing, type inference, [target definitions](#database-targets), and script/command generation. No UI code at all. |
| `ImportHelper.Cli` | The command-line tool (`ImportHelper.exe`/`ImportHelper`) — parses arguments and calls into `Core`. |
| `ImportHelper.Gui` | The [Avalonia](https://avaloniaui.net/)-based desktop GUI — one codebase, native windows on Windows/Linux/macOS — that calls the same `Core` engine. |

## Build

```
dotnet build
```

Builds the whole solution. To build or run just one project: `dotnet build src/ImportHelper.Cli` / `dotnet run --project src/ImportHelper.Gui`.

## GUI

`ImportHelper.Gui` is an [Avalonia](https://avaloniaui.net/) desktop app — one codebase producing native windows on Windows, Linux, and macOS, so the experience is consistent across platforms rather than being a native-looking app on one OS and an afterthought on the others. It exposes the same options as the CLI flags below as form fields (file pattern with a browse button — a checkbox next to it switches Browse between picking a single file or a whole folder — an optional destination directory with its own folder browser, delimiter, target with a file browser for custom YAML, and checkboxes for the rest — "First row has column headers" defaults checked in the GUI, unlike the CLI's `-HasHeader`, since most files people pick interactively have one), a Run button, and a log pane showing the same output the CLI would print. When a run finishes, a popup reports each file's column analysis — name, inferred type, and max length for strings, the same report the CLI prints per file — plus a failure count if any file couldn't be processed; dismissing it opens the output folder in the OS file manager. It calls the exact same `ImportHelper.Core` engine as the CLI, so results are identical between the two.

Run it with `dotnet run --project src/ImportHelper.Gui`, or use a prebuilt binary from [Releases](https://github.com/RaolinTheDense9476/ImportHelper/releases/latest).

## CLI Usage

```
ImportHelper -FilePattern <file_pattern> -Delimiter <delimiter>
             [-HasHeader] [-Encoding <encoding_name>]
             [-Target <name_or_path>]
             [-GenerateTsql [<output_prefix>]]
             [-GenerateBcpFormat [<output_prefix>]]
             [-AllowEmbeddedNewlines]
             [-ForceQuotedAsString]
             [-PrepareForBcp [<replacement_value>]]
```

### Options

| Option | Description |
| --- | --- |
| `-FilePattern <file_pattern>` | **Required.** File pattern with optional wildcards (e.g. `C:\data\*.csv`, `*.txt`, `\\server\share\file.txt`). If no directory is given, the current directory is used. |
| `-DestinationDirectory <path>` | Where to write generated files (`.sql`, `.fmt`, `bcp_*` copies). Defaults to the same directory as each input file. Created automatically if it doesn't already exist. |
| `-Delimiter <delimiter>` | **Required.** Field delimiter character (e.g. `,`, `;`, `\|`). Also accepts the literal two-character escapes `\t`, `\n`, `\r`, and `\\` for control characters no shell will type directly. |
| `-HasHeader` | First row of each file contains column headers, used as column names in the analysis report and generated `CREATE TABLE` script (after sanitization). |
| `-Encoding <encoding_name>` | Character encoding of the input files (e.g. `UTF-8`, `ASCII`, `UTF-16`, `ISO-8859-1`). Defaults to `UTF-8`. |
| `-Target <name_or_path>` | Which [target definition](#database-targets) to use. Either a bundled name (looks for `targets/<name>.yaml` next to the executable) or a path to a custom YAML file. Defaults to `mssql`. |
| `-GenerateTsql [<output_prefix>]` | Generate a `CREATE TABLE` script (`<prefix><filename>_CreateTable.sql`) plus a ready-to-run bulk-import command for the selected target, printed to the console as well. |
| `-GenerateBcpFormat [<output_prefix>]` | Generate a bcp format file (`<prefix><filename>.fmt`), if the selected target defines one. |
| `-AllowEmbeddedNewlines` | Suppress the warning printed when a quoted field contains an embedded newline. Many import tools, including native `bcp`, don't handle this correctly. |
| `-ForceQuotedAsString` | Always treat quoted values as `String`, instead of the default of still attempting Numeric/Date inference on them. Useful when quoting is a deliberate "this is text" signal from the source. |
| `-PrepareForBcp [<replacement_value>]` | Write a `bcp_<filename>` copy with quoting stripped and any delimiter/newline that was protected inside quotes replaced with `<replacement_value>` (default: a single space). See [Why -PrepareForBcp?](#why--preparefor-bcp) below. |

## Quoting and escaping

ImportHelper parses delimited fields using standard CSV-style quoting:

- A field may be wrapped in double quotes (`"`) to contain the delimiter character or a literal newline.
- A literal double quote inside a quoted field is escaped by doubling it (`""`).
- A quote only opens a quoted field when it's the first character of the field; a quote appearing mid-field is treated as a literal character.
- Whitespace inside a quoted field is preserved (not trimmed) when computing `MaxLength`, since quoting is often used specifically to protect meaningful leading/trailing whitespace. Values are still trimmed before being evaluated for Numeric/Date inference, whether or not they were quoted.

## Database targets

What's genuinely *data* (type names, identifier quoting, the command template) lives in a YAML file per target; what's genuinely *behavior* (parsing the file, figuring out if a numeric column is really an integer) stays in code. [`src/ImportHelper.Core/targets/mssql.yaml`](src/ImportHelper.Core/targets/mssql.yaml) is the reference implementation — SQL Server isn't special-cased in C#, it's just the target that ships by default.

A target definition has these sections:

| Section | Purpose |
| --- | --- |
| `identifierQuote.prefix` / `.suffix` | Wraps a generated column/table name, e.g. `[` / `]` for SQL Server, `"` / `"` for Postgres. |
| `typeMapping.integer` / `.float` / `.date` | The type name to emit for each of those inferred kinds. |
| `typeMapping.string` | An ordered list of `{maxLength, type}` rules; the first entry whose `maxLength` is omitted or `>=` the column's actual max length wins — put the catch-all (no `maxLength`) last. |
| `createTable.template` | The whole `CREATE TABLE` statement, with `{tableName}` and `{columns}` placeholders. |
| `createTable.columnTemplate` / `.columnSeparator` | How each column line is rendered (`{quotedName}`, `{type}`) and how lines are joined. |
| `bulkImport.commandTemplate` | The bulk-import command line, with `{table}`, `{filePath}`, `{delimiterEscaped}`, `{encodingFlags}`, `{headerFlag}` placeholders. |
| `bulkImport.headerFlagWhenHasHeader` | Text substituted into `{headerFlag}` when `-HasHeader` was passed (empty otherwise). |
| `bulkImport.encodingRules` / `.defaultEncodingFlagsTemplate` | Ordered `{matchCodePage, flags}` rules substituted into `{encodingFlags}`; falls back to the default template (`{codePage}` placeholder) when nothing matches. This is how `mssql.yaml` picks bcp's `-w` for UTF-16 vs. `-c -C <codepage>` for everything else. |
| `bulkImport.notesTemplate` / `.headerNoteWhenHasHeader` | Optional trailing comment line in the generated script (`{encodingName}`, `{codePage}`, `{headerNote}` placeholders). Leave `notesTemplate` empty to omit it entirely. |
| `bcpFormatFile` | Optional. Only meaningful for targets with an analogous format-file step (bcp's `.fmt` file); omit this section entirely for targets that don't have one, and `-GenerateBcpFormat` will say so instead of generating anything. |

To add a new target, copy `src/ImportHelper.Core/targets/mssql.yaml`, adjust it for your database/tool's conventions, and either drop it in `targets/<name>.yaml` next to the executable (it ships alongside both the CLI and GUI builds) or pass `-Target <path-to-file.yaml>` directly — no rebuild required.

## Why `-PrepareForBcp`?

Native `bcp` (in `-c` character mode) has **no concept of CSV quoting** — it splits purely on the raw delimiter byte and passes quote characters through literally. That causes two distinct failures:

- A quoted value with no embedded delimiter (e.g. `"8096000000.00"` destined for a `FLOAT` column) fails to cast, because bcp tries to convert the literal string *including the surrounding quote characters*.
- A quoted value with an embedded delimiter (e.g. `"Smith, John"`) splits into extra columns, misaligning the rest of the row.

`-PrepareForBcp` produces a flat, quote-free file where neither case can happen, so it can be bulk-copied with a plain `bcp -c` and the same `-t` delimiter — no quote-awareness required on bcp's part.

## Examples

Analyze all CSV files in the `DataFiles` directory using a comma delimiter, assuming a header row:
```
ImportHelper -FilePattern "DataFiles\*.csv" -Delimiter "," -HasHeader
```

Analyze tab-separated text files in the current directory using UTF-16 encoding, and generate T-SQL scripts prefixed `staging_`:
```
ImportHelper -FilePattern "*.txt" -Delimiter "\t" -Encoding "UTF-16" -GenerateTsql staging_
```

Analyze pipe-separated data files and generate BCP format files prefixed `load_`:
```
ImportHelper -FilePattern "/opt/import/*.dat" -Delimiter "|" -GenerateBcpFormat load_
```

Analyze CSV files without a header and generate both T-SQL and BCP format files using default prefixes:
```
ImportHelper -FilePattern "DataFiles\*.csv" -Delimiter "," -GenerateTsql -GenerateBcpFormat
```

Analyze a quoted export where every field is quoted regardless of type, suppressing embedded-newline warnings:
```
ImportHelper -FilePattern "*.csv" -Delimiter "," -HasHeader -AllowEmbeddedNewlines
```

Treat quoting as an explicit "this is text" signal, even for values that look numeric or date-like:
```
ImportHelper -FilePattern "*.csv" -Delimiter "," -HasHeader -ForceQuotedAsString
```

Prepare a quoted CSV for a plain bcp import, replacing protected delimiters/newlines with an underscore instead of the default space:
```
ImportHelper -FilePattern "*.csv" -Delimiter "," -HasHeader -PrepareForBcp "_"
```

Generate a `CREATE TABLE` script and bulk-import command for a custom target instead of the built-in `mssql`:
```
ImportHelper -FilePattern "*.csv" -Delimiter "," -HasHeader -Target "path\to\postgres.yaml" -GenerateTsql
```

Write generated files to a separate output folder instead of alongside the source data:
```
ImportHelper -FilePattern "DataFiles\*.csv" -Delimiter "," -HasHeader -DestinationDirectory "C:\staging\output" -GenerateTsql
```

## Exit codes (CLI)

The CLI can be used as a "is this file well-formed?" check in scripts or CI: it exits **0** if every matched file was found, readable, and had no malformed rows, and **1** otherwise — file not found, no files matched the pattern, missing required arguments, or a row with the wrong number of columns (which is still parsed and reported, just flagged). `-Help`/`-help` always exits 0. An embedded-newline warning ([see above](#quoting-and-escaping)) does *not* affect the exit code by itself, since the data is still valid CSV — pass `-AllowEmbeddedNewlines` to silence the warning text, or check the log output if you need to detect it separately.

## Notes

- The utility scans the entire content of each file to accurately determine data types and maximum string lengths.
- For numeric columns, the utility attempts to distinguish between integer and floating-point types (`INT`/`FLOAT` in the default `mssql` target — the actual names come from the target's `typeMapping`).
- String column lengths are rounded up to the next size step defined in the target's `typeMapping.string` list (10, 50, 100, 255, 500, 1000, `MAX` for the default `mssql` target).
- The default `mssql` target's bcp format files use generic SQL Server data types (`SQLCHAR`, `SQLFLT8`, `SQLDATETIME`).

## Cross-platform notes

ImportHelper runs on Linux and macOS as well as Windows — it targets .NET 8 with no platform-specific APIs, so `dotnet build`/`dotnet run` work unchanged. A few things to be aware of:

- Row parsing already treats `\r\n`, standalone `\n`, and standalone `\r` all as valid row terminators, including files that mix line-ending styles. No special flag is needed for LF- vs. CRLF-terminated input.
- `-PrepareForBcp` always writes `\r\n` row terminators in its output, regardless of the host OS, since that matches `bcp`'s own default row-terminator convention rather than the platform's.
- File patterns use forward slashes on Linux/macOS (e.g. `/opt/import/*.dat`) instead of backslashes, and filename matching is case-sensitive there — `*.CSV` will not match `data.csv` the way it does on Windows.
- `-Encoding` values beyond `UTF-8`, `UTF-16`, `UTF-32`, `ASCII`, and Latin-1/`ISO-8859-1` (e.g. legacy Windows code pages like `Windows-1252`) require registering `System.Text.Encoding.CodePages` in code — this is a .NET runtime limitation on every platform, not something specific to Linux.

## AI Disclosure:
Yes, I used AI to generate a lot of this. I had written a similar utility years back and the code was lost in a version control migration, so when I recreated it I applied many lessons learned over years of imports and had AI streamline the process. Do with that what you will. 

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## Releasing

Pushing a tag matching `v*` (e.g. `v1.5.0`) triggers [`.github/workflows/release.yml`](.github/workflows/release.yml), which builds self-contained single-file CLI + GUI binaries for all four platforms, archives them the same way as the Downloads above, and creates the GitHub Release automatically — using the matching `## [x.y.z]` section of `CHANGELOG.md` as the release notes. So a release is just:

```
git tag -a v1.5.0 -m "..."
git push origin v1.5.0
```

## License

[MIT](LICENSE)
