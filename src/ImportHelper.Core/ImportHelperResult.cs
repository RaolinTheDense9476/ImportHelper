namespace ImportHelper.Core
{
  public class FileSummary
  {
    public string FilePath { get; set; } = "";
    public List<ColumnInfo> Columns { get; set; } = new();
  }

  public class ImportHelperResult
  {
    public bool Success { get; set; } = true;
    public string OutputDirectory { get; set; } = "";
    public int FilesFound { get; set; }
    public int FilesProcessedSuccessfully { get; set; }
    public int FilesFailed { get; set; }
    public List<string> GeneratedFiles { get; } = new();
    public List<FileSummary> Files { get; } = new();
  }
}
