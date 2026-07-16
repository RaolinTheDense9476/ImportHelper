namespace ImportHelper.Core
{
  public enum ColumnDataType
  {
    String,
    Numeric,
    Date
  }

  public class ColumnInfo
  {
    public int Index { get; set; }
    public string? Name { get; set; }
    public ColumnDataType DataType { get; set; }
    public int MaxLength { get; set; }
  }
}
