using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// The translation table: one row per English line, one column per language (its code: en, ru, es...), plus an
/// optional <see cref="ContextColumn"/> telling the translator where the line is used. Stored as a CSV, so it opens
/// in any spreadsheet app as well as in the Translations window (Tools > Localization > Translations).
///
/// <para>The first language column holds the source text: the line exactly as it's written in the game (a dialogue
/// node, an inspector field, a code literal), and the key it's looked up by. Cells are trimmed; rich text tags and
/// {0} placeholders must be kept in the translations.</para>
/// </summary>
public class LocTable
{
    public const string ContextColumn = "context";

    public class Row
    {
        /// <summary>One per language of the table; [0] is the source line.</summary>
        public string[] Text;
        public string Context = "";

        public Row(int languages) => Text = new string[Math.Max(1, languages)];

        public string Source => Text[0] ?? "";

        public string Get(int column) => column >= 0 && column < Text.Length ? Text[column] ?? "" : "";

        public void Set(int column, string value)
        {
            if (column < 0) return;
            if (column >= Text.Length) Array.Resize(ref Text, column + 1);
            Text[column] = value;
        }
    }

    /// <summary>The language codes, in column order. The first one is the source language.</summary>
    public readonly List<string> Languages = new List<string>();
    public readonly List<Row> Rows = new List<Row>();

    public string SourceLanguage => Languages.Count > 0 ? Languages[0] : null;

    public int Column(string code)
    {
        if (string.IsNullOrEmpty(code)) return -1;
        for (int i = 0; i < Languages.Count; i++)
            if (string.Equals(Languages[i], code, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>The column of <paramref name="code"/>, added (empty for every row) if the table doesn't have it yet.</summary>
    public int AddLanguage(string code)
    {
        int column = Column(code);
        if (column >= 0) return column;
        Languages.Add(code);
        foreach (Row row in Rows) row.Set(Languages.Count - 1, "");
        return Languages.Count - 1;
    }

    /// <summary>A line as the table stores and looks it up: trimmed, with Windows line breaks made plain.</summary>
    public static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (text.IndexOf('\r') >= 0) text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return text.Trim();
    }

    // -------------------------------------------------------------------------------------------------------------
    // CSV
    // -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads a CSV made by <see cref="ToCsv"/> or saved from a spreadsheet app: comma, semicolon or tab separated
    /// (whichever the header line uses), quoted cells may hold line breaks. Null or empty: an empty table.
    /// </summary>
    public static LocTable Parse(string csv)
    {
        LocTable table = new LocTable();
        if (string.IsNullOrEmpty(csv)) return table;
        if (csv[0] == '﻿') csv = csv.Substring(1);

        List<List<string>> records = ReadRecords(csv, DetectDelimiter(csv));
        if (records.Count == 0) return table;

        // Header: language codes, the context column, anything else (empty headers) ignored.
        List<int> languageCells = new List<int>();
        int contextCell = -1;
        List<string> header = records[0];
        for (int i = 0; i < header.Count; i++)
        {
            string name = header[i].Trim();
            if (name.Length == 0) continue;
            if (string.Equals(name, ContextColumn, StringComparison.OrdinalIgnoreCase)) contextCell = i;
            else
            {
                table.Languages.Add(name);
                languageCells.Add(i);
            }
        }
        if (table.Languages.Count == 0) return table;

        for (int r = 1; r < records.Count; r++)
        {
            List<string> record = records[r];
            string source = Clean(Cell(record, languageCells[0]));
            if (source.Length == 0) continue;

            Row row = new Row(table.Languages.Count);
            for (int c = 0; c < languageCells.Count; c++) row.Text[c] = Clean(Cell(record, languageCells[c]));
            row.Context = contextCell >= 0 ? Clean(Cell(record, contextCell)) : "";
            table.Rows.Add(row);
        }
        return table;
    }

    /// <summary>The table as a comma separated CSV: header line, then one line per row.</summary>
    public string ToCsv()
    {
        StringBuilder csv = new StringBuilder();
        for (int i = 0; i < Languages.Count; i++)
        {
            if (i > 0) csv.Append(',');
            AppendCell(csv, Languages[i]);
        }
        csv.Append(',').Append(ContextColumn).Append('\n');

        foreach (Row row in Rows)
        {
            for (int i = 0; i < Languages.Count; i++)
            {
                if (i > 0) csv.Append(',');
                AppendCell(csv, row.Get(i));
            }
            csv.Append(',');
            AppendCell(csv, row.Context);
            csv.Append('\n');
        }
        return csv.ToString();
    }

    private static string Cell(List<string> record, int index) => index < record.Count ? record[index] : "";

    private static void AppendCell(StringBuilder csv, string value)
    {
        value ??= "";
        bool quote = value.IndexOfAny(new[] { ',', ';', '\t', '"', '\n', '\r' }) >= 0 ||
                     (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[value.Length - 1])));
        if (!quote)
        {
            csv.Append(value);
            return;
        }
        csv.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
    }

    /// <summary>The separator the first line uses most: ',' (default), ';' (Excel in many locales) or tab.</summary>
    private static char DetectDelimiter(string csv)
    {
        int commas = 0, semicolons = 0, tabs = 0;
        bool quoted = false;
        foreach (char c in csv)
        {
            if (c == '"') quoted = !quoted;
            else if (!quoted && (c == '\n' || c == '\r')) break;
            else if (!quoted && c == ',') commas++;
            else if (!quoted && c == ';') semicolons++;
            else if (!quoted && c == '\t') tabs++;
        }
        if (semicolons > commas && semicolons >= tabs) return ';';
        if (tabs > commas && tabs > semicolons) return '\t';
        return ',';
    }

    private static List<List<string>> ReadRecords(string csv, char delimiter)
    {
        List<List<string>> records = new List<List<string>>();
        List<string> record = new List<string>();
        StringBuilder cell = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < csv.Length; i++)
        {
            char c = csv[i];
            if (quoted)
            {
                if (c != '"') cell.Append(c);
                else if (i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else quoted = false;
                continue;
            }

            if (c == '"') quoted = true;
            else if (c == delimiter)
            {
                record.Add(cell.ToString());
                cell.Clear();
            }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                record.Add(cell.ToString());
                cell.Clear();
                records.Add(record);
                record = new List<string>();
            }
            else cell.Append(c);
        }

        if (cell.Length > 0 || record.Count > 0)
        {
            record.Add(cell.ToString());
            records.Add(record);
        }
        return records;
    }
}
