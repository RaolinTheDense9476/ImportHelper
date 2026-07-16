using System.Text;

namespace ImportHelper.Core
{
  public class DelimitedRow
  {
    public string[] Fields { get; set; } = Array.Empty<string>();
    public bool[] QuotedFields { get; set; } = Array.Empty<bool>();
    public bool HasEmbeddedNewline { get; set; }
  }

  public static class DelimitedFileParser
  {
    // Reads delimited rows from a file, honoring standard CSV-style quoting:
    // fields may be wrapped in double quotes to contain the delimiter or a
    // newline, and a literal quote inside a quoted field is escaped as "".
    public static IEnumerable<DelimitedRow> ReadDelimitedFile(string filePath, char delimiter, Encoding encoding)
    {
      using (StreamReader reader = new StreamReader(filePath, encoding))
      {
        foreach (DelimitedRow row in ReadDelimitedRows(reader, delimiter))
        {
          yield return row;
        }
      }
    }

    public static IEnumerable<DelimitedRow> ReadDelimitedRows(TextReader reader, char delimiter)
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

    // No shell interprets \t, \n, \r inside a quoted argument as an actual
    // control character, so accept them as a literal two-character escape
    // instead of silently taking the backslash as the delimiter.
    public static char ParseDelimiter(string raw)
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
  }
}
