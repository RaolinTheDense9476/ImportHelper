namespace ImportHelper.Core
{
  public class ImportHelperOptions
  {
    public string FilePattern { get; set; } = "";
    public string Delimiter { get; set; } = ",";
    public bool HasHeader { get; set; }
    public string Encoding { get; set; } = "UTF-8";
    public string Target { get; set; } = "mssql";
    public bool GenerateTsql { get; set; }
    public string? TsqlPrefix { get; set; }
    public bool GenerateBcpFormat { get; set; }
    public string? BcpFormatPrefix { get; set; }
    public bool AllowEmbeddedNewlines { get; set; }
    public bool ForceQuotedAsString { get; set; }
    public bool PrepareForBcp { get; set; }
    public string PrepareForBcpReplacement { get; set; } = " ";
  }
}
