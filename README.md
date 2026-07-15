# ImportHelper

A command-line utility that analyzes delimited text files (CSV, TSV, etc.) and optionally generates T-SQL table creation scripts, BCP format files, and bcp-ready cleaned copies for importing into Microsoft SQL Server.

## Features

- Infers each column's data type (`String`, `Numeric`, `Date`) and maximum length by scanning the entire file
- Standard CSV-style quoting support: quoted fields, `""` escaping, and delimiters/newlines embedded inside quotes
- Generates a `CREATE TABLE` T-SQL script with inferred types, plus a ready-to-run `bcp` command
- Generates BCP format files
- `-PrepareForBcp` rewrites a file to be safe for a plain `bcp -c` import, which has no CSV-quoting awareness of its own

## Requirements

- .NET 8 SDK to build and run
- The `bcp` utility and a SQL Server instance if you intend to use the generated import commands

## Build

```
dotnet build
```

## Usage

```
ImportHelper -FilePattern <file_pattern> -Delimiter <delimiter>
             [-HasHeader] [-Encoding <encoding_name>]
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
| `-Delimiter <delimiter>` | **Required.** Field delimiter character (e.g. `,`, `;`, `\|`). Also accepts the literal two-character escapes `\t`, `\n`, `\r`, and `\\` for control characters no shell will type directly. |
| `-HasHeader` | First row of each file contains column headers, used as column names in the analysis report and generated T-SQL (after sanitization). |
| `-Encoding <encoding_name>` | Character encoding of the input files (e.g. `UTF-8`, `ASCII`, `UTF-16`, `ISO-8859-1`). Defaults to `UTF-8`. |
| `-GenerateTsql [<output_prefix>]` | Generate a T-SQL `CREATE TABLE` script (`<prefix><filename>_CreateTable.sql`) plus a ready-to-run `bcp` command, printed to the console as well. The command's `-c`/`-w` mode and code page are derived from the encoding actually used to read the file, and `-F 2` is included automatically when `-HasHeader` was specified. |
| `-GenerateBcpFormat [<output_prefix>]` | Generate a BCP format file (`<prefix><filename>.fmt`) for use with the `bcp` utility. |
| `-AllowEmbeddedNewlines` | Suppress the warning printed when a quoted field contains an embedded newline. Many import tools, including native `bcp`, don't handle this correctly. |
| `-ForceQuotedAsString` | Always treat quoted values as `String`, instead of the default of still attempting Numeric/Date inference on them. Useful when quoting is a deliberate "this is text" signal from the source. |
| `-PrepareForBcp [<replacement_value>]` | Write a `bcp_<filename>` copy with quoting stripped and any delimiter/newline that was protected inside quotes replaced with `<replacement_value>` (default: a single space). See [Why -PrepareForBcp?](#why--preparefor-bcp) below. |

## Quoting and escaping

ImportHelper parses delimited fields using standard CSV-style quoting:

- A field may be wrapped in double quotes (`"`) to contain the delimiter character or a literal newline.
- A literal double quote inside a quoted field is escaped by doubling it (`""`).
- A quote only opens a quoted field when it's the first character of the field; a quote appearing mid-field is treated as a literal character.
- Whitespace inside a quoted field is preserved (not trimmed) when computing `MaxLength`, since quoting is often used specifically to protect meaningful leading/trailing whitespace. Values are still trimmed before being evaluated for Numeric/Date inference, whether or not they were quoted.

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

## Notes

- The utility scans the entire content of each file to accurately determine data types and maximum string lengths.
- For numeric columns, the utility attempts to distinguish between integer (`INT`) and floating-point (`FLOAT`) types.
- String column lengths in generated T-SQL are rounded up to the next size step (10, 50, 100, 255, 500, 1000, `MAX`).
- BCP format files use generic SQL Server data types (`SQLCHAR`, `SQLFLT8`, `SQLDATETIME`).

## Cross-platform notes

ImportHelper runs on Linux and macOS as well as Windows — it targets .NET 8 with no platform-specific APIs, so `dotnet build`/`dotnet run` work unchanged. A few things to be aware of:

- Row parsing already treats `\r\n`, standalone `\n`, and standalone `\r` all as valid row terminators, including files that mix line-ending styles. No special flag is needed for LF- vs. CRLF-terminated input.
- `-PrepareForBcp` always writes `\r\n` row terminators in its output, regardless of the host OS, since that matches `bcp`'s own default row-terminator convention rather than the platform's.
- File patterns use forward slashes on Linux/macOS (e.g. `/opt/import/*.dat`) instead of backslashes, and filename matching is case-sensitive there — `*.CSV` will not match `data.csv` the way it does on Windows.
- `-Encoding` values beyond `UTF-8`, `UTF-16`, `UTF-32`, `ASCII`, and Latin-1/`ISO-8859-1` (e.g. legacy Windows code pages like `Windows-1252`) require registering `System.Text.Encoding.CodePages` in code — this is a .NET runtime limitation on every platform, not something specific to Linux.

##AI Disclosure:
Yes, I used AI to generate a lot of this. I had written a similar utility years back and the code was lost in a version control migration, so when I recreated it I applied many lessons learned over years of imports and had AI streamline the process. Do with that what you will. 

## License

[MIT](LICENSE)
