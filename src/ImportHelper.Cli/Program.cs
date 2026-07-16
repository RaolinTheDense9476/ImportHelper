using ImportHelper.Core;

namespace ImportHelper.Cli
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

      bool flowControl = ValidateArguments(arguments);
      if (!flowControl)
      {
        return;
      }

      var options = new ImportHelperOptions
      {
        FilePattern = arguments["FilePattern"],
        DestinationDirectory = arguments.ContainsKey("DestinationDirectory") && !string.IsNullOrEmpty(arguments["DestinationDirectory"]) ? arguments["DestinationDirectory"] : null,
        Delimiter = arguments["Delimiter"],
        HasHeader = arguments.ContainsKey("HasHeader"),
        Encoding = arguments.ContainsKey("Encoding") ? arguments["Encoding"] : "UTF-8",
        Target = arguments.ContainsKey("Target") && !string.IsNullOrEmpty(arguments["Target"]) ? arguments["Target"] : "mssql",
        GenerateTsql = arguments.ContainsKey("GenerateTsql"),
        TsqlPrefix = arguments.ContainsKey("GenerateTsql") && !string.IsNullOrEmpty(arguments["GenerateTsql"]) ? arguments["GenerateTsql"] : null,
        GenerateBcpFormat = arguments.ContainsKey("GenerateBcpFormat"),
        BcpFormatPrefix = arguments.ContainsKey("GenerateBcpFormat") && !string.IsNullOrEmpty(arguments["GenerateBcpFormat"]) ? arguments["GenerateBcpFormat"] : null,
        AllowEmbeddedNewlines = arguments.ContainsKey("AllowEmbeddedNewlines"),
        ForceQuotedAsString = arguments.ContainsKey("ForceQuotedAsString"),
        PrepareForBcp = arguments.ContainsKey("PrepareForBcp"),
        PrepareForBcpReplacement = arguments.ContainsKey("PrepareForBcp") && !string.IsNullOrEmpty(arguments["PrepareForBcp"]) ? arguments["PrepareForBcp"] : " ",
      };

      ImportHelperRunner.Run(options, Console.WriteLine);

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
      Console.WriteLine("\nUsage: ImportHelper.exe -FilePattern <file_pattern> -Delimiter <delimiter> [-DestinationDirectory <path>] [-HasHeader] [-Encoding <encoding_name>] [-Target <name_or_path>] [-GenerateTsql [<output_prefix>]] [-GenerateBcpFormat [<output_prefix>]] [-AllowEmbeddedNewlines] [-ForceQuotedAsString] [-PrepareForBcp [<replacement>]]");
      Console.WriteLine("  -FilePattern <file_pattern>   : File pattern with optional wildcards (e.g., C:\\data\\*.csv, *.txt, data\\file.csv, \\\\server\\share\\file.txt)");
      Console.WriteLine("  -DestinationDirectory <path>  : Where to write generated files. Defaults to the same directory as each input file. Created if it doesn't exist.");
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
  }
}
