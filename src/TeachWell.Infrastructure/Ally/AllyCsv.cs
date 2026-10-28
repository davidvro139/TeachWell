using System.Text;

namespace TeachWell.Infrastructure.Ally;

public sealed class AllyReadException(string message) : Exception(message);

// An RFC 4180 CSV reader for Ally's report exports (ADR 020).
//
// Uploaded files are untrusted, so everything is bounded: the stream, the row count, the field length and the column
// count. A malformed file is rejected with a message an instructional designer can act on, never a parse exception.
//
// Columns are addressed by header name, case-insensitively and ignoring surrounding spaces, so a column order change
// in a future Ally release does not break the reader. Unknown columns are ignored rather than rejected, because
// Anthology adds them, and the expected-but-absent ones are reported instead (AllyTable.Missing).
public sealed class AllyTable
{
    public const int MaxBytes = 40 * 1024 * 1024, MaxRows = 100_000, MaxColumns = 400, MaxFieldLength = 4000;

    private readonly Dictionary<string, int> columns;
    private readonly List<string[]> rows;

    private AllyTable(Dictionary<string, int> columns, List<string[]> rows, IReadOnlyList<string> headers)
    {
        this.columns = columns; this.rows = rows; Headers = headers;
    }

    public IReadOnlyList<string> Headers { get; }
    public int Count => rows.Count;
    public bool Has(string column) => columns.ContainsKey(Key(column));

    // Ally's issue columns carry their severity in the header: Anthology documents the column as "AlternativeText:2",
    // not "AlternativeText". Whether that colon is really in the file or is the documentation's way of annotating the
    // severity is not something the documentation settles, so both forms are accepted: a header is indexed under its
    // full name and, when it ends in ":<digits>", under its base name as well.
    public static string BaseName(string header)
    {
        var value = header.Trim();
        var colon = value.LastIndexOf(':');
        if (colon <= 0 || colon == value.Length - 1) return value;
        var suffix = value[(colon + 1)..];
        return suffix.All(char.IsAsciiDigit) ? value[..colon] : value;
    }

    // The severity Ally stated in a column header, when it stated one. Used to check the documented catalogue against
    // what a real export actually says, rather than trusting the documentation silently.
    public static int? SeverityIn(string header)
    {
        var value = header.Trim();
        var colon = value.LastIndexOf(':');
        if (colon <= 0 || colon == value.Length - 1) return null;
        var suffix = value[(colon + 1)..];
        return suffix.All(char.IsAsciiDigit) && int.TryParse(suffix, out var severity) ? severity : null;
    }

    // The header this table holds for a column, by base name, or null when it has none.
    public string? HeaderFor(string column)
    {
        var key = Key(column);
        if (!columns.TryGetValue(key, out var index)) return null;
        return index < Headers.Count ? Headers[index].Trim() : null;
    }

    // The value in a row, or "" when the column is absent: a missing column is a reported gap, not a per-row failure.
    public string Value(int row, string column) =>
        columns.TryGetValue(Key(column), out var index) && index < rows[row].Length ? rows[row][index] : "";

    public int? Number(int row, string column) =>
        int.TryParse(Value(row, column).Trim(), out var value) ? value : null;
    public decimal? Decimal(int row, string column) =>
        decimal.TryParse(Value(row, column).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    public DateTime? Date(int row, string column) =>
        DateTime.TryParse(Value(row, column).Trim(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var value) ? value : null;

    // Which of the columns the caller expected are not in the file. Reported so a schema change is visible.
    public IReadOnlyList<string> Missing(IEnumerable<string> expected) =>
        [.. expected.Where(c => !Has(c)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.Ordinal)];

    // Ally writes a column per issue; a blank or zero cell means the issue was not found on that row.
    public static bool Flagged(string value) => int.TryParse(value.Trim(), out var n) ? n > 0 : value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

    private static string Key(string column) => column.Trim().ToLowerInvariant();

    public static AllyTable Read(Stream stream)
    {
        var text = Decode(stream);
        var fields = Parse(text);
        if (fields.Count == 0) throw new AllyReadException("This file is empty, so it is not an Ally report export.");
        var headers = fields[0];
        if (headers.Length > MaxColumns) throw new AllyReadException($"This file has more than {MaxColumns} columns, which is beyond TeachWell's import limit.");
        var columns = new Dictionary<string, int>();
        for (var i = 0; i < headers.Length; i++)
        {
            var key = Key(headers[i]);
            // A duplicate header keeps its first column; Ally does not emit duplicates, and guessing between them would be worse.
            if (key.Length == 0) continue;
            columns.TryAdd(key, i);
            // "AlternativeText:2" is also reachable as "AlternativeText" (see BaseName).
            var bare = Key(BaseName(headers[i]));
            if (bare != key) columns.TryAdd(bare, i);
        }
        if (columns.Count == 0) throw new AllyReadException("This file's first row has no column names, so its columns cannot be identified.");
        return new AllyTable(columns, fields.Skip(1).ToList(), headers);
    }

    private static string Decode(Stream stream)
    {
        using var buffer = new MemoryStream();
        var bytes = new byte[81920];
        int read;
        while ((read = stream.Read(bytes, 0, bytes.Length)) > 0)
        {
            if (buffer.Length + read > MaxBytes) throw new AllyReadException($"This file is larger than {MaxBytes / (1024 * 1024)} MB, which is beyond TeachWell's import limit.");
            buffer.Write(bytes, 0, read);
        }
        var data = buffer.ToArray();
        // Ally's exports carry a UTF-8 byte-order mark; Encoding.UTF8 strips it when it leads the content.
        return new UTF8Encoding(false).GetString(data).TrimStart('﻿');
    }

    // Fields, quotes and embedded commas, quotes and newlines, per RFC 4180.
    private static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var any = false;

        void EndField()
        {
            if (field.Length > MaxFieldLength) throw new AllyReadException($"This file has a value longer than {MaxFieldLength:N0} characters, which is beyond TeachWell's import limit.");
            row.Add(field.ToString()); field.Clear();
        }
        void EndRow()
        {
            EndField();
            // A trailing newline must not produce a final empty row.
            if (row.Count > 1 || row[0].Length > 0) rows.Add([.. row]);
            row.Clear();
            if (rows.Count > MaxRows) throw new AllyReadException($"This file has more than {MaxRows:N0} rows, which is beyond TeachWell's import limit.");
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            any = true;
            if (quoted)
            {
                if (c != '"') { field.Append(c); continue; }
                if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; continue; }
                quoted = false;
                continue;
            }
            switch (c)
            {
                case '"' when field.Length == 0: quoted = true; break;
                case '"': field.Append(c); break;
                case ',': EndField(); break;
                case '\r' when i + 1 < text.Length && text[i + 1] == '\n': EndRow(); i++; break;
                case '\r':
                case '\n': EndRow(); break;
                default: field.Append(c); break;
            }
        }
        if (quoted) throw new AllyReadException("This file has an unclosed quoted value, so it could not be read. Export it again from Ally.");
        if (any && (field.Length > 0 || row.Count > 0)) EndRow();
        return rows;
    }
}
