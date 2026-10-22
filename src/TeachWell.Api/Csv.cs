using System.Text;

namespace TeachWell.Api;

// CSV downloads. RFC 4180 quoting, plus formula-injection protection: a value a spreadsheet would treat as a formula (starting with
// = + - @, tab or carriage return) is prefixed with an apostrophe, so text from course titles or names can never run as a formula.
public sealed class Csv
{
    private readonly StringBuilder text = new();

    public Csv(params string[] headers) => Row(headers);

    public void Row(params string[] values) => text.Append(string.Join(',', values.Select(Field))).Append("\r\n");

    public static string Field(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static string Slug(string value) => new(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());

    public override string ToString() => text.ToString();

    // A download with a UTF-8 byte-order mark, so spreadsheet programs read accented names correctly.
    public IResult File(string fileName) =>
        Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray(), "text/csv; charset=utf-8", fileName);
}
