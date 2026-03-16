using System.Text;

namespace Birko.Helpers;

/// <summary>
/// RFC 4180-compliant CSV parser with state machine for quoted fields.
/// Yields one row at a time as IList&lt;string&gt; for lazy evaluation.
/// </summary>
public class CsvParser
{
    private enum ReadState { NewLine, CurrentLine, QuoteText, PotentialQuote }

    private readonly Stream _stream;
    private readonly char _delimiter;
    private readonly char? _enclosure;
    private readonly Encoding _encoding;

    /// <summary>Current line number (1-based after first yield).</summary>
    public int Line { get; private set; }

    public CsvParser(Stream stream, char delimiter = ',', char? enclosure = '"', Encoding? encoding = null)
    {
        _stream = stream;
        _delimiter = delimiter;
        _enclosure = enclosure;
        _encoding = encoding ?? Encoding.UTF8;
    }

    /// <summary>
    /// Parse the stream lazily, yielding one row at a time.
    /// </summary>
    public IEnumerable<IList<string>> Parse()
    {
        using var reader = new StreamReader(_stream, _encoding, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var state = ReadState.NewLine;
        var field = new StringBuilder();
        var row = new List<string>();
        Line = 0;

        int ch;
        while ((ch = reader.Read()) != -1)
        {
            var c = (char)ch;

            switch (state)
            {
                case ReadState.NewLine:
                case ReadState.CurrentLine:
                    if (_enclosure.HasValue && c == _enclosure.Value && field.Length == 0)
                    {
                        state = ReadState.QuoteText;
                    }
                    else if (c == _delimiter)
                    {
                        row.Add(field.ToString());
                        field.Clear();
                        state = ReadState.CurrentLine;
                    }
                    else if (c == '\n')
                    {
                        row.Add(field.ToString().TrimEnd('\r'));
                        field.Clear();
                        Line++;
                        yield return row;
                        row = new List<string>();
                        state = ReadState.NewLine;
                    }
                    else
                    {
                        field.Append(c);
                        state = ReadState.CurrentLine;
                    }
                    break;

                case ReadState.QuoteText:
                    if (_enclosure.HasValue && c == _enclosure.Value)
                    {
                        state = ReadState.PotentialQuote;
                    }
                    else
                    {
                        field.Append(c);
                    }
                    break;

                case ReadState.PotentialQuote:
                    if (_enclosure.HasValue && c == _enclosure.Value)
                    {
                        // Escaped quote (doubled)
                        field.Append(c);
                        state = ReadState.QuoteText;
                    }
                    else if (c == _delimiter)
                    {
                        row.Add(field.ToString());
                        field.Clear();
                        state = ReadState.CurrentLine;
                    }
                    else if (c == '\n')
                    {
                        row.Add(field.ToString().TrimEnd('\r'));
                        field.Clear();
                        Line++;
                        yield return row;
                        row = new List<string>();
                        state = ReadState.NewLine;
                    }
                    else
                    {
                        field.Append(c);
                        state = ReadState.CurrentLine;
                    }
                    break;
            }
        }

        // Last row if no trailing newline
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            Line++;
            yield return row;
        }
    }
}
