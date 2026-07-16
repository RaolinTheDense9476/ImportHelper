using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ImportHelper
{
  // Everything that differs between database systems / bulk-import tools is
  // meant to live here as data (type names, quoting, command templates), not
  // as C# branching. Behavior that genuinely varies (e.g. how the file needs
  // to look on disk) stays in code; this only decides how to *label* it.
  class StringLengthRule
  {
    public int? MaxLength { get; set; }
    public string Type { get; set; } = "";
  }

  class TypeMapping
  {
    public string Integer { get; set; } = "";
    public string Float { get; set; } = "";
    public string Date { get; set; } = "";
    public List<StringLengthRule> String { get; set; } = new();
  }

  class IdentifierQuote
  {
    public string Prefix { get; set; } = "";
    public string Suffix { get; set; } = "";
  }

  class CreateTableSpec
  {
    // Placeholders: {tableName} {columns}
    public string Template { get; set; } = "";
    // Placeholders: {quotedName} {type}
    public string ColumnTemplate { get; set; } = "";
    public string ColumnSeparator { get; set; } = ",\n";
  }

  class EncodingRule
  {
    public int? MatchCodePage { get; set; }
    public string Flags { get; set; } = "";
  }

  class BulkImportSpec
  {
    // Placeholders: {table} {filePath} {delimiterEscaped} {encodingFlags} {headerFlag}
    public string CommandTemplate { get; set; } = "";
    public string HeaderFlagWhenHasHeader { get; set; } = "";
    public List<EncodingRule> EncodingRules { get; set; } = new();
    // Placeholder: {codePage}
    public string DefaultEncodingFlagsTemplate { get; set; } = "";
    // Placeholders: {encodingName} {codePage} {headerNote}
    public string NotesTemplate { get; set; } = "";
    public string HeaderNoteWhenHasHeader { get; set; } = "";
  }

  class BcpFormatTypeEntry
  {
    public string SqlType { get; set; } = "";
    public int Length { get; set; }
    public bool LengthFromMaxLength { get; set; }
    public int MinLength { get; set; }
  }

  // Optional: not every bulk-import mechanism has an analogous format-file
  // step (this one is specific to SQL Server's bcp utility).
  class BcpFormatSpec
  {
    public string Version { get; set; } = "12.0";
    public BcpFormatTypeEntry Numeric { get; set; } = new();
    public BcpFormatTypeEntry Date { get; set; } = new();
    public BcpFormatTypeEntry String { get; set; } = new();
  }

  class TargetDefinition
  {
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public IdentifierQuote IdentifierQuote { get; set; } = new();
    public TypeMapping TypeMapping { get; set; } = new();
    public CreateTableSpec CreateTable { get; set; } = new();
    public BulkImportSpec BulkImport { get; set; } = new();
    public BcpFormatSpec? BcpFormatFile { get; set; }

    public static TargetDefinition Load(string nameOrPath)
    {
      string yamlPath = ResolvePath(nameOrPath);
      string yamlText = File.ReadAllText(yamlPath);

      var deserializer = new DeserializerBuilder()
          .WithNamingConvention(CamelCaseNamingConvention.Instance)
          .IgnoreUnmatchedProperties()
          .Build();

      return deserializer.Deserialize<TargetDefinition>(yamlText);
    }

    private static string ResolvePath(string nameOrPath)
    {
      if (File.Exists(nameOrPath))
      {
        return nameOrPath;
      }

      string bundledPath = Path.Combine(AppContext.BaseDirectory, "targets", $"{nameOrPath}.yaml");
      if (File.Exists(bundledPath))
      {
        return bundledPath;
      }

      throw new FileNotFoundException($"Could not find a target definition named '{nameOrPath}' (checked '{nameOrPath}' and '{bundledPath}').");
    }

    public string QuoteIdentifier(string name) => $"{IdentifierQuote.Prefix}{name}{IdentifierQuote.Suffix}";

    public string GetStringType(int maxLength)
    {
      foreach (StringLengthRule rule in TypeMapping.String)
      {
        if (rule.MaxLength == null || maxLength <= rule.MaxLength.Value)
        {
          return rule.Type;
        }
      }

      return TypeMapping.String.Count > 0 ? TypeMapping.String[^1].Type : "";
    }

    public string GetEncodingFlags(Encoding encoding)
    {
      foreach (EncodingRule rule in BulkImport.EncodingRules)
      {
        if (rule.MatchCodePage.HasValue && rule.MatchCodePage.Value == encoding.CodePage)
        {
          return rule.Flags;
        }
      }

      return BulkImport.DefaultEncodingFlagsTemplate.Replace("{codePage}", encoding.CodePage.ToString());
    }
  }
}
