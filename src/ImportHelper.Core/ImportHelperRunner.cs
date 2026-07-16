using System.Text;
using System.Text.RegularExpressions;

namespace ImportHelper.Core
{
  public static class ImportHelperRunner
  {
    public static ImportHelperResult Run(ImportHelperOptions options, Action<string> log)
    {
      var result = new ImportHelperResult();

      char delimiter = DelimitedFileParser.ParseDelimiter(options.Delimiter);

      string directoryPath = Path.GetDirectoryName(options.FilePattern) ?? "";
      string fileFilter = Path.GetFileName(options.FilePattern);

      if (string.IsNullOrEmpty(directoryPath))
      {
        directoryPath = Directory.GetCurrentDirectory();
      }

      if (!Path.IsPathRooted(options.FilePattern))
      {
        directoryPath = Path.GetFullPath(directoryPath);
      }

      // Default to writing output alongside the input files, but let the
      // caller redirect everything to one destination instead.
      string outputDirectory = string.IsNullOrWhiteSpace(options.DestinationDirectory)
          ? directoryPath
          : Path.GetFullPath(options.DestinationDirectory);

      if (!string.IsNullOrWhiteSpace(options.DestinationDirectory))
      {
        Directory.CreateDirectory(outputDirectory);
      }

      result.OutputDirectory = outputDirectory;

      Encoding encoding;
      try
      {
        encoding = Encoding.GetEncoding(options.Encoding);
      }
      catch (ArgumentException)
      {
        log($"Error: Invalid encoding name '{options.Encoding}'. Using default encoding UTF-8.");
        encoding = Encoding.UTF8;
      }

      TargetDefinition target;
      try
      {
        target = TargetDefinition.Load(options.Target);
      }
      catch (Exception ex)
      {
        log($"Error: Could not load target '{options.Target}': {ex.Message}");
        result.Success = false;
        return result;
      }

      if (!Directory.Exists(directoryPath))
      {
        log($"Error: Directory not found - {directoryPath}");
        result.Success = false;
        return result;
      }

      string[] files = Directory.GetFiles(directoryPath, fileFilter);
      result.FilesFound = files.Length;

      if (!files.Any())
      {
        log($"No files found matching the filter '{fileFilter}' in '{directoryPath}'.");
        return result;
      }

      foreach (string filePath in files)
      {
        log($"\nProcessing file: {filePath} (Encoding: {encoding.EncodingName})");
        List<ColumnInfo>? columnInfos = ProcessFile(filePath, delimiter, options.HasHeader, encoding, options.AllowEmbeddedNewlines, options.ForceQuotedAsString, log);

        if (columnInfos == null)
        {
          result.Success = false;
          result.FilesFailed++;
          continue;
        }

        result.FilesProcessedSuccessfully++;
        result.Files.Add(new FileSummary { FilePath = filePath, Columns = columnInfos });

        if (options.GenerateTsql)
        {
          string tsqlOutputPrefix = options.TsqlPrefix ?? Path.GetFileNameWithoutExtension(filePath) + "_";
          string tsqlPath = GenerateTsqlScript(filePath, outputDirectory, columnInfos, tsqlOutputPrefix, delimiter, options.HasHeader, encoding, target, log);
          result.GeneratedFiles.Add(tsqlPath);
        }

        if (options.GenerateBcpFormat)
        {
          if (target.BcpFormatFile == null)
          {
            log($"Target '{target.Name}' does not define a bcpFormatFile section; skipping -GenerateBcpFormat.");
          }
          else
          {
            string bcpOutputPrefix = options.BcpFormatPrefix ?? Path.GetFileNameWithoutExtension(filePath) + "_";
            string fmtPath = GenerateBcpFormatFile(filePath, outputDirectory, columnInfos, bcpOutputPrefix, delimiter, target.BcpFormatFile, log);
            result.GeneratedFiles.Add(fmtPath);
          }
        }

        if (options.PrepareForBcp)
        {
          string preparedPath = PrepareForBcpFile(filePath, outputDirectory, delimiter, encoding, options.PrepareForBcpReplacement, log);
          result.GeneratedFiles.Add(preparedPath);
        }
      }

      return result;
    }

    static List<ColumnInfo>? ProcessFile(string filePath, char delimiter, bool hasHeader, Encoding encoding, bool allowEmbeddedNewlines, bool forceQuotedAsString, Action<string> log)
    {
      if (!File.Exists(filePath))
      {
        log($"Error: File not found - {filePath}");
        return null;
      }

      // First pass to determine column count and headers
      string[]? firstLineValues;
      try
      {
        firstLineValues = DelimitedFileParser.ReadDelimitedFile(filePath, delimiter, encoding).FirstOrDefault()?.Fields;
        if (firstLineValues == null)
        {
          log("File is empty.");
          return new List<ColumnInfo>();
        }
      }
      catch (Exception ex)
      {
        log($"Error reading file '{filePath}' with encoding '{encoding.EncodingName}': {ex.Message}");
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
        foreach (DelimitedRow delimitedRow in DelimitedFileParser.ReadDelimitedFile(filePath, delimiter, encoding))
        {
          if (hasHeader && rowNumber == 0)
          {
            rowNumber++;
            continue;
          }

          if (delimitedRow.HasEmbeddedNewline && !allowEmbeddedNewlines)
          {
            log($"Warning: Row {rowNumber + 1} contains an embedded newline within a quoted field. Many import tools (e.g., native bcp) do not handle this correctly; pass -AllowEmbeddedNewlines to suppress this warning.");
          }

          string[] row = delimitedRow.Fields;
          if (row.Length != columnCount)
          {
            log($"Warning: Row {rowNumber + 1} has a different number of columns and will be skipped.");
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
        log($"Error processing file '{filePath}' with encoding '{encoding.EncodingName}': {ex.Message}");
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

      log("Column Analysis:");
      foreach (var info in columnInfos)
      {
        log($"  {(info.Name != null ? info.Name : $"Column {info.Index + 1}")}: Type = {info.DataType}, {(info.DataType == ColumnDataType.String ? $"MaxLength = {info.MaxLength}" : "")}");
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
      foreach (DelimitedRow delimitedRow in DelimitedFileParser.ReadDelimitedFile(inputFilePath, delimiter, encoding))
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

    static string GenerateTsqlScript(string inputFilePath, string outputDirectory, List<ColumnInfo> columnInfos, string outputPrefix, char delimiter, bool hasHeader, Encoding encoding, TargetDefinition target, Action<string> log)
    {
      string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputFilePath);
      string outputFilePath = Path.Combine(outputDirectory, $"{outputPrefix}{fileNameWithoutExtension}_CreateTable.sql");
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
        string columnName = columnInfos[i].Name != null ? Regex.Replace(columnInfos[i].Name!, @"[^a-zA-Z0-9_]", "") : $"Column{i + 1}";
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

      log($"T-SQL script generated: {outputFilePath}");
      log("Bulk import command:");
      log($"  {bcpCommand}");

      return outputFilePath;
    }

    static string GenerateBcpFormatFile(string inputFilePath, string outputDirectory, List<ColumnInfo> columnInfos, string outputPrefix, char delimiter, BcpFormatSpec bcpFormatFile, Action<string> log)
    {
      string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputFilePath);
      string outputFilePath = Path.Combine(outputDirectory, $"{outputPrefix}{fileNameWithoutExtension}.fmt");

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
          writer.WriteLine($"{i + 1}\tSQLSERVER\t{entry.SqlType}\t0\t{fieldLength}\t\"{(i < columnInfos.Count - 1 ? (delimiter == '\t' ? "\\t" : delimiter.ToString()) : "")}\"\t{i + 1}\t{(columnInfos[i].Name != null ? Regex.Replace(columnInfos[i].Name!, @"[^a-zA-Z0-9_]", "") : $"Column{i + 1}")}");
        }
      }

      log($"BCP format file generated: {outputFilePath}");

      return outputFilePath;
    }

    // Native bcp (-c mode) has no concept of CSV quoting: it splits purely on
    // the raw delimiter byte and passes quote characters through literally,
    // so a quoted value containing the delimiter or a newline breaks the
    // import. This rewrites the file with quotes stripped (already handled
    // by the CSV-aware parser) and any delimiter/newline that was protected
    // by quoting neutralized, producing a flat file safe for plain bcp -c.
    static string PrepareForBcpFile(string inputFilePath, string outputDirectory, char delimiter, Encoding encoding, string replacement, Action<string> log)
    {
      string delimiterString = delimiter.ToString();
      string outputFilePath = Path.Combine(outputDirectory, "bcp_" + Path.GetFileName(inputFilePath));

      using (StreamWriter writer = new StreamWriter(outputFilePath, false, encoding))
      {
        writer.NewLine = "\r\n";

        foreach (DelimitedRow row in DelimitedFileParser.ReadDelimitedFile(inputFilePath, delimiter, encoding))
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

      log($"Bcp-ready file generated: {outputFilePath}");

      return outputFilePath;
    }
  }
}
