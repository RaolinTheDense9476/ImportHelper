using System.Text;
using System.Text.RegularExpressions;

namespace ImportHelper
{
  class Program
  {
    static void Main(string[] args)
    {
      // Strip surrounding quotes from all arguments
      for (int i = 0; i < args.Length; i++)
      {
        if ((args[i].StartsWith("\"") && args[i].EndsWith("\"")) ||
            (args[i].StartsWith("'") && args[i].EndsWith("'")))
        {
          args[i] = args[i].Substring(1, args[i].Length - 2);
        }
      }

      var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      ParseArguments(args, arguments);


      // Validate required arguments
      bool flowControl = ValidateArguments(arguments);
      if (!flowControl)
      {
        return;
      }

      string filePattern = arguments["FilePattern"];
      char delimiter = ParseDelimiter(arguments["Delimiter"]);

      // Extract directory and file filter from the pattern
      string directoryPath = Path.GetDirectoryName(filePattern);
      string fileFilter = Path.GetFileName(filePattern);

      // If no directory specified, use current directory
      if (string.IsNullOrEmpty(directoryPath))
      {
        directoryPath = Directory.GetCurrentDirectory();
      }

      // If the directory is relative, make it absolute
      if (!Path.IsPathRooted(filePattern))
      {
        directoryPath = Path.GetFullPath(directoryPath);
      }

      bool hasHeader = arguments.ContainsKey("HasHeader");
      bool allowEmbeddedNewlines = arguments.ContainsKey("AllowEmbeddedNewlines");
      bool forceQuotedAsString = arguments.ContainsKey("ForceQuotedAsString");
      string encodingName = arguments.ContainsKey("Encoding") ? arguments["Encoding"] : "UTF-8";

      bool generateTsql = arguments.ContainsKey("GenerateTsql");
      bool generateBcpFormat = arguments.ContainsKey("GenerateBcpFormat");
      bool prepareForBcp = arguments.ContainsKey("PrepareForBcp");
      string tsqlPrefixArg = generateTsql && !string.IsNullOrEmpty(arguments["GenerateTsql"]) ? arguments["GenerateTsql"] : null;
      string bcpPrefixArg = generateBcpFormat && !string.IsNullOrEmpty(arguments["GenerateBcpFormat"]) ? arguments["GenerateBcpFormat"] : null;
      string prepareForBcpReplacement = prepareForBcp && !string.IsNullOrEmpty(arguments["PrepareForBcp"]) ? arguments["PrepareForBcp"] : " ";
      string targetName = arguments.ContainsKey("Target") && !string.IsNullOrEmpty(arguments["Target"]) ? arguments["Target"] : "mssql";

      Encoding encoding;
      try
      {
        encoding = Encoding.GetEncoding(encodingName);
      }
      catch (ArgumentException)
      {
        Console.WriteLine($"Error: Invalid encoding name '{encodingName}'. Using default encoding UTF-8.");
        encoding = Encoding.UTF8;
      }

      TargetDefinition target;
      try
      {
        target = TargetDefinition.Load(targetName);
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error: Could not load target '{targetName}': {ex.Message}");
        return;
      }

      if (!Directory.Exists(directoryPath))
      {
        Console.WriteLine($"Error: Directory not found - {directoryPath}");
        return;
      }

      string[] files = Directory.GetFiles(directoryPath, fileFilter);

      if (!files.Any())
      {
        Console.WriteLine($"No files found matching the filter '{fileFilter}' in '{directoryPath}'.");
        return;
      }

      foreach (string filePath in files)
      {
        Console.WriteLine($"\nProcessing file: {filePath} (Encoding: {encoding.EncodingName})");
        List<ColumnInfo> columnInfos = ProcessFile(filePath, delimiter, hasHeader, encoding, allowEmbeddedNewlines, forceQuotedAsString);

        if (columnInfos != null)
        {
          string tsqlOutputPrefix = string.Empty;
          string bcpOutputPrefix = string.Empty;

          if (generateTsql)
          {
            tsqlOutputPrefix = tsqlPrefixArg ?? Path.GetFileNameWithoutExtension(filePath) + "_";
            GenerateTsqlScript(filePath, columnInfos, tsqlOutputPrefix, delimiter, hasHeader, encoding, target);
          }

          if (generateBcpFormat)
          {
            if (target.BcpFormatFile == null)
            {
              Console.WriteLine($"Target '{target.Name}' does not define a bcpFormatFile section; skipping -GenerateBcpFormat.");
            }
            else
            {
              bcpOutputPrefix = bcpPrefixArg ?? Path.GetFileNameWithoutExtension(filePath) + "_";
              GenerateBcpFormatFile(filePath, columnInfos, bcpOutputPrefix, delimiter, target.BcpFormatFile);
            }
          }

          if (prepareForBcp)
          {
            PrepareForBcpFile(filePath, delimiter, encoding, prepareForBcpReplacement);
          }
        }
      }
#if DEBUG
      Console.WriteLine("\nPress any key to exit.");
      Console.ReadKey();
#endif
    }

    private static bool ValidateArguments(Dictionary<string, string> arguments)
    {
      bool retval = true;

      if (!arguments.ContainsKey("FilePattern"))
      {
        Console.WriteLine("Error: -FilePattern argument is required.");
        retval = false;
      }

      if (!arguments.ContainsKey("Delimiter"))
      {
        Console.WriteLine("Error: -Delimiter argument is required.");
        retval = false;
      }

      if (arguments.ContainsKey("Help") ||
         arguments.ContainsKey("help") ||
         arguments.ContainsKey("HELP"))
      {
        retval = false;
      }

      if (!retval)
        ShowHelpMessage();

      return retval;
    }

    // No shell interprets \t, \n, \r inside a quoted argument as an actual
    // control character, so accept them as a literal two-character escape
    // instead of silently taking the backslash as the delimiter.
    private static char ParseDelimiter(string raw)
    {
      if (raw.Length == 2 && raw[0] == '\\')
      {
        switch (raw[1])
        {
          case 't': return '\t';
          case 'n': return '\n';
          case 'r': return '\r';
          case '\\': return '\\';
        }
      }

      return raw[0];
    }

    private static void ParseArguments(string[] args, Dictionary<string, string> arguments)
    {
      for (int i = 0; i < args.Length; i++)
      {
        if (args[i].StartsWith("-"))
        {
          string key = args[i].Substring(1);
          if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
          {
            arguments[key] = args[++i];
          }
          else
          {
            arguments[key] = string.Empty;
          }
        }
        else
        {
          Console.WriteLine($"Warning: Ignoring unexpected argument '{args[i]}'");
        }
      }
    }

    private static void ShowHelpMessage()
    {
      Console.WriteLine("\nUsage: ImportHelper.exe -FilePattern <file_pattern> -Delimiter <delimiter> [-HasHeader] [-Encoding <encoding_name>] [-Target <name_or_path>] [-GenerateTsql [<output_prefix>]] [-GenerateBcpFormat [<output_prefix>]] [-AllowEmbeddedNewlines] [-ForceQuotedAsString] [-PrepareForBcp [<replacement>]]");
      Console.WriteLine("  -FilePattern <file_pattern>   : File pattern with optional wildcards (e.g., C:\\data\\*.csv, *.txt, data\\file.csv, \\\\server\\share\\file.txt)");
      Console.WriteLine("  -Delimiter <delimiter>        : Field delimiter character (e.g., ',', '\\t', '|')");
      Console.WriteLine("  -HasHeader                    : Indicates first row contains column headers");
      Console.WriteLine("  -Encoding <encoding_name>     : Encoding name (e.g., UTF-8, ASCII, UTF-16, ISO-8859-1)");
      Console.WriteLine("  -Target <name_or_path>        : Database target definition to use (default: mssql). Either a bundled name (looks for targets/<name>.yaml next to the executable) or a path to a custom YAML file.");
      Console.WriteLine("  -GenerateTsql [<prefix>]      : Generate a CREATE TABLE script for the target with optional table name prefix");
      Console.WriteLine("  -GenerateBcpFormat [<prefix>] : Generate a bcp format file with optional output file prefix (only if the target defines a bcpFormatFile section)");
      Console.WriteLine("  -AllowEmbeddedNewlines        : Suppress the warning for quoted fields containing embedded newlines");
      Console.WriteLine("  -ForceQuotedAsString          : Always treat quoted values as String instead of attempting Numeric/Date inference on them");
      Console.WriteLine("  -PrepareForBcp [<replacement>]: Write a bcp_<filename> copy with quotes removed and any delimiter/newline that was inside a quoted value replaced with <replacement> (default: a single space). Native bcp -c mode does not understand CSV quoting, so this produces a file safe for a plain bcp import.");
      return;
    }

    // Reads delimited rows from a file, honoring standard CSV-style quoting:
    // fields may be wrapped in double quotes to contain the delimiter or a
    // newline, and a literal quote inside a quoted field is escaped as "".
    static IEnumerable<DelimitedRow> ReadDelimitedFile(string filePath, char delimiter, Encoding encoding)
    {
      using (StreamReader reader = new StreamReader(filePath, encoding))
      {
        foreach (DelimitedRow row in ReadDelimitedRows(reader, delimiter))
        {
          yield return row;
        }
      }
    }

    static IEnumerable<DelimitedRow> ReadDelimitedRows(TextReader reader, char delimiter)
    {
      const char quote = '"';
      var fields = new List<string>();
      var quotedFlags = new List<bool>();
      var field = new StringBuilder();
      bool inQuotes = false;
      bool currentFieldQuoted = false;
      bool rowStarted = false;
      bool hasEmbeddedNewline = false;

      int current;
      while ((current = reader.Read()) != -1)
      {
        char c = (char)current;

        if (inQuotes)
        {
          if (c == quote)
          {
            if (reader.Peek() == quote)
            {
              reader.Read();
              field.Append(quote);
            }
            else
            {
              inQuotes = false;
            }
          }
          else
          {
            if (c == '\r' || c == '\n')
            {
              hasEmbeddedNewline = true;
            }
            field.Append(c);
          }
          rowStarted = true;
        }
        else if (c == quote && field.Length == 0)
        {
          // A quote only opens a quoted field at the field's start; a quote
          // appearing mid-field is treated as a literal character.
          inQuotes = true;
          currentFieldQuoted = true;
          rowStarted = true;
        }
        else if (c == delimiter)
        {
          fields.Add(field.ToString());
          quotedFlags.Add(currentFieldQuoted);
          field.Clear();
          currentFieldQuoted = false;
          rowStarted = true;
        }
        else if (c == '\r' || c == '\n')
        {
          if (c == '\r' && reader.Peek() == '\n')
          {
            reader.Read();
          }
          fields.Add(field.ToString());
          quotedFlags.Add(currentFieldQuoted);
          field.Clear();
          currentFieldQuoted = false;
          yield return new DelimitedRow { Fields = fields.ToArray(), QuotedFields = quotedFlags.ToArray(), HasEmbeddedNewline = hasEmbeddedNewline };
          fields.Clear();
          quotedFlags.Clear();
          rowStarted = false;
          hasEmbeddedNewline = false;
        }
        else
        {
          field.Append(c);
          rowStarted = true;
        }
      }

      if (rowStarted || fields.Count > 0)
      {
        fields.Add(field.ToString());
        quotedFlags.Add(currentFieldQuoted);
        yield return new DelimitedRow { Fields = fields.ToArray(), QuotedFields = quotedFlags.ToArray(), HasEmbeddedNewline = hasEmbeddedNewline };
      }
    }

    static List<ColumnInfo> ProcessFile(string filePath, char delimiter, bool hasHeader, Encoding encoding, bool allowEmbeddedNewlines, bool forceQuotedAsString)
    {
      if (!File.Exists(filePath))
      {
        Console.WriteLine($"Error: File not found - {filePath}");
        return null;
      }

      // First pass to determine column count and headers
      string[] firstLineValues;
      try
      {
        firstLineValues = ReadDelimitedFile(filePath, delimiter, encoding).FirstOrDefault()?.Fields;
        if (firstLineValues == null)
        {
          Console.WriteLine("File is empty.");
          return new List<ColumnInfo>();
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error reading file '{filePath}' with encoding '{encoding.EncodingName}': {ex.Message}");
        return null;
      }

      int columnCount = firstLineValues.Length;
      List<ColumnInfo> columnInfos = Enumerable.Range(0, columnCount)
          .Select(i => new ColumnInfo { Index = i, DataType = ColumnDataType.String, MaxLength = 0 })
          .ToList();

      if (hasHeader)
      {
        for (int i = 0; i < columnCount && i < firstLineValues.Length; i++)
        {
          columnInfos[i].Name = firstLineValues[i].Trim();
        }
      }

      bool[] couldBeNumeric = Enumerable.Repeat(true, columnCount).ToArray();
      bool[] couldBeDate = Enumerable.Repeat(true, columnCount).ToArray();

      // Second pass (streaming): Infer data types and max lengths
      try
      {
        int rowNumber = 0;
        foreach (DelimitedRow delimitedRow in ReadDelimitedFile(filePath, delimiter, encoding))
        {
          if (hasHeader && rowNumber == 0)
          {
            rowNumber++;
            continue;
          }

          if (delimitedRow.HasEmbeddedNewline && !allowEmbeddedNewlines)
          {
            Console.WriteLine($"Warning: Row {rowNumber + 1} contains an embedded newline within a quoted field. Many import tools (e.g., native bcp) do not handle this correctly; pass -AllowEmbeddedNewlines to suppress this warning.");
          }

          string[] row = delimitedRow.Fields;
          if (row.Length != columnCount)
          {
            Console.WriteLine($"Warning: Row {rowNumber + 1} has a different number of columns and will be skipped.");
            rowNumber++;
            continue;
          }

          for (int j = 0; j < columnCount; j++)
          {
            bool wasQuoted = delimitedRow.QuotedFields[j];
            // Preserve untrimmed content for quoted values (whitespace inside
            // quotes is meaningful), but always trim before type-checking.
            string value = wasQuoted ? row[j] : row[j].Trim();
            string valueForTypeCheck = wasQuoted ? row[j].Trim() : value;
            ColumnInfo columnInfo = columnInfos[j];

            if (wasQuoted && forceQuotedAsString)
            {
              // Some exports quote every field regardless of its type, so by
              // default a quoted value is still eligible for Numeric/Date
              // inference. Pass -ForceQuotedAsString to always treat a quoted
              // value as an explicit signal that the source considers this
              // column text, regardless of what it looks like.
              couldBeNumeric[j] = false;
              couldBeDate[j] = false;
            }
            else
            {
              if (couldBeNumeric[j] && !string.IsNullOrEmpty(valueForTypeCheck) && !double.TryParse(valueForTypeCheck, out _))
              {
                couldBeNumeric[j] = false;
              }

              if (couldBeDate[j] && !string.IsNullOrEmpty(valueForTypeCheck) && !DateTime.TryParse(valueForTypeCheck, out _))
              {
                couldBeDate[j] = false;
              }
            }

            columnInfo.MaxLength = Math.Max(columnInfo.MaxLength, value.Length);
          }
          rowNumber++;
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error processing file '{filePath}' with encoding '{encoding.EncodingName}': {ex.Message}");
        return null;
      }

      // Finalizing data types after full scan
      for (int j = 0; j < columnCount; j++)
      {
        if (couldBeNumeric[j] && !couldBeDate[j])
        {
          columnInfos[j].DataType = ColumnDataType.Numeric;
        }
        else if (!couldBeNumeric[j] && couldBeDate[j])
        {
          columnInfos[j].DataType = ColumnDataType.Date;
        }
      }

      Console.WriteLine("Column Analysis:");
      foreach (var info in columnInfos)
      {
        Console.WriteLine($"  {(info.Name != null ? info.Name : $"Column {info.Index + 1}")}: Type = {info.DataType}, {(info.DataType == ColumnDataType.String ? $"MaxLength = {info.MaxLength}" : "")}");
      }

      return columnInfos;
    }

    // Determines whether a Numeric column's values all parse as integers,
    // by rescanning the file. This is a fact about the data, not a per-target
    // decision, so it stays in code; the target only decides what string to
    // emit for "integer" vs. "float".
    static bool IsIntegerColumn(string inputFilePath, char delimiter, bool hasHeader, Encoding encoding, int columnIndex)
    {
      int rowNum = 0;
      foreach (DelimitedRow delimitedRow in ReadDelimitedFile(inputFilePath, delimiter, encoding))
      {
        if (hasHeader && rowNum == 0)
        {
          rowNum++;
          continue;
        }
        string[] values = delimitedRow.Fields;
        if (columnIndex < values.Length && !string.IsNullOrWhiteSpace(values[columnIndex]))
        {
          if (!int.TryParse(values[columnIndex].Trim(), out _))
          {
            return false;
          }
        }
        rowNum++;
      }
      return true;
    }

    static string GetSqlType(ColumnInfo columnInfo, string inputFilePath, char delimiter, bool hasHeader, Encoding encoding, TargetDefinition target)
    {
      switch (columnInfo.DataType)
      {
        case ColumnDataType.Numeric:
          bool isInteger = IsIntegerColumn(inputFilePath, delimiter, hasHeader, encoding, columnInfo.Index);
          return isInteger ? target.TypeMapping.Integer : target.TypeMapping.Float;
        case ColumnDataType.Date:
          return target.TypeMapping.Date;
        case ColumnDataType.String:
        default:
          return target.GetStringType(columnInfo.MaxLength);
      }
    }

    static void GenerateTsqlScript(string inputFilePath, List<ColumnInfo> columnInfos, string outputPrefix, char delimiter, bool hasHeader, Encoding encoding, TargetDefinition target)
    {
      string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputFilePath);
      string outputFilePath = Path.Combine(Path.GetDirectoryName(inputFilePath), $"{outputPrefix}{fileNameWithoutExtension}_CreateTable.sql");
      string tableName = $"{outputPrefix}{fileNameWithoutExtension}";
      string delimiterForBcp = delimiter == '\t' ? "\\t" : delimiter.ToString();
      string encodingFlags = target.GetEncodingFlags(encoding);
      string headerFlag = hasHeader ? target.BulkImport.HeaderFlagWhenHasHeader : "";
      string bcpCommand = target.BulkImport.CommandTemplate
          .Replace("{table}", tableName)
          .Replace("{filePath}", inputFilePath)
          .Replace("{delimiterEscaped}", delimiterForBcp)
          .Replace("{encodingFlags}", encodingFlags)
          .Replace("{headerFlag}", headerFlag);
      string headerNote = hasHeader ? target.BulkImport.HeaderNoteWhenHasHeader : "";
      string bcpNotes = target.BulkImport.NotesTemplate
          .Replace("{encodingName}", encoding.EncodingName)
          .Replace("{codePage}", encoding.CodePage.ToString())
          .Replace("{headerNote}", headerNote);

      var columnLines = new List<string>();
      for (int i = 0; i < columnInfos.Count; i++)
      {
        string columnName = columnInfos[i].Name != null ? Regex.Replace(columnInfos[i].Name, @"[^a-zA-Z0-9_]", "") : $"Column{i + 1}";
        string sqlType = GetSqlType(columnInfos[i], inputFilePath, delimiter, hasHeader, encoding, target);
        string columnLine = target.CreateTable.ColumnTemplate
            .Replace("{quotedName}", target.QuoteIdentifier(columnName))
            .Replace("{type}", sqlType);
        columnLines.Add(columnLine);
      }

      string createTableScript = target.CreateTable.Template
          .Replace("{tableName}", tableName)
          .Replace("{columns}", string.Join(target.CreateTable.ColumnSeparator, columnLines));

      using (StreamWriter writer = new StreamWriter(outputFilePath))
      {
        writer.WriteLine($"-- T-SQL Table Creation Script for {Path.GetFileName(inputFilePath)}");
        writer.WriteLine($"-- Generated on {DateTime.Now}");
        writer.WriteLine($"-- Target: {target.DisplayName} ({target.Name})");
        writer.WriteLine($"");
        writer.WriteLine(createTableScript);
        writer.WriteLine($"");
        writer.WriteLine($"-- You can use the following command to import the data:");
        writer.WriteLine($"-- {bcpCommand}");
        if (!string.IsNullOrEmpty(bcpNotes))
        {
          writer.WriteLine($"-- {bcpNotes}");
        }
      }

      Console.WriteLine($"T-SQL script generated: {outputFilePath}");
      Console.WriteLine("Bulk import command:");
      Console.WriteLine($"  {bcpCommand}");
    }

    static void GenerateBcpFormatFile(string inputFilePath, List<ColumnInfo> columnInfos, string outputPrefix, char delimiter, BcpFormatSpec bcpFormatFile)
    {
      string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputFilePath);
      string outputFilePath = Path.Combine(Path.GetDirectoryName(inputFilePath), $"{outputPrefix}{fileNameWithoutExtension}.fmt");

      using (StreamWriter writer = new StreamWriter(outputFilePath, false, Encoding.ASCII))
      {
        writer.WriteLine(bcpFormatFile.Version);
        writer.WriteLine($"{columnInfos.Count}");

        for (int i = 0; i < columnInfos.Count; i++)
        {
          BcpFormatTypeEntry entry;
          switch (columnInfos[i].DataType)
          {
            case ColumnDataType.Numeric:
              entry = bcpFormatFile.Numeric;
              break;
            case ColumnDataType.Date:
              entry = bcpFormatFile.Date;
              break;
            case ColumnDataType.String:
            default:
              entry = bcpFormatFile.String;
              break;
          }
          int fieldLength = entry.LengthFromMaxLength ? Math.Max(entry.MinLength, columnInfos[i].MaxLength) : entry.Length;
          writer.WriteLine($"{i + 1}\tSQLSERVER\t{entry.SqlType}\t0\t{fieldLength}\t\"{(i < columnInfos.Count - 1 ? (delimiter == '\t' ? "\\t" : delimiter.ToString()) : "")}\"\t{i + 1}\t{(columnInfos[i].Name != null ? Regex.Replace(columnInfos[i].Name, @"[^a-zA-Z0-9_]", "") : $"Column{i + 1}")}");
        }
      }

      Console.WriteLine($"BCP format file generated: {outputFilePath}");
    }

    // Native bcp (-c mode) has no concept of CSV quoting: it splits purely on
    // the raw delimiter byte and passes quote characters through literally,
    // so a quoted value containing the delimiter or a newline breaks the
    // import. This rewrites the file with quotes stripped (already handled
    // by the CSV-aware parser) and any delimiter/newline that was protected
    // by quoting neutralized, producing a flat file safe for plain bcp -c.
    static void PrepareForBcpFile(string inputFilePath, char delimiter, Encoding encoding, string replacement)
    {
      string delimiterString = delimiter.ToString();
      string outputFilePath = Path.Combine(Path.GetDirectoryName(inputFilePath), "bcp_" + Path.GetFileName(inputFilePath));

      using (StreamWriter writer = new StreamWriter(outputFilePath, false, encoding))
      {
        writer.NewLine = "\r\n";

        foreach (DelimitedRow row in ReadDelimitedFile(inputFilePath, delimiter, encoding))
        {
          string[] cleanedFields = new string[row.Fields.Length];
          for (int i = 0; i < row.Fields.Length; i++)
          {
            cleanedFields[i] = row.Fields[i]
                .Replace("\r\n", replacement)
                .Replace("\r", replacement)
                .Replace("\n", replacement)
                .Replace(delimiterString, replacement);
          }
          writer.WriteLine(string.Join(delimiterString, cleanedFields));
        }
      }

      Console.WriteLine($"Bcp-ready file generated: {outputFilePath}");
    }
  }

  enum ColumnDataType
  {
    String,
    Numeric,
    Date
  }

  class ColumnInfo
  {
    public int Index { get; set; }
    public string Name { get; set; }
    public ColumnDataType DataType { get; set; }
    public int MaxLength { get; set; }
  }

  class DelimitedRow
  {
    public string[] Fields { get; set; }
    public bool[] QuotedFields { get; set; }
    public bool HasEmbeddedNewline { get; set; }
  }
}