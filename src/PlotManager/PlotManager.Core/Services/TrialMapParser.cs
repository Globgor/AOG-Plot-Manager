using System.Globalization;

namespace PlotManager.Core.Services;

using PlotManager.Core.Models;

/// <summary>
/// Parses trial map CSV files into TrialMap objects.
///
/// Expected CSV format:
///   PlotId,Product
///   R1C1,Product A
///   R1C2,Control
///   R1C3,Product B
///
/// Also supports alternative format:
///   Row,Column,Product
///   1,1,Product A
///   1,2,Control
/// </summary>
public static class TrialMapParser
{
    /// <summary>
    /// Parses a trial map CSV file.
    /// </summary>
    /// <param name="filePath">Path to the CSV file.</param>
    /// <param name="trialName">Optional trial name. If null, uses the filename.</param>
    /// <returns>Parsed TrialMap.</returns>
    public static TrialMap Parse(string filePath, string? trialName = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Trial map CSV not found: {filePath}");

        string[] lines = File.ReadAllLines(filePath);
        if (lines.Length < 2)
            throw new FormatException("Trial map CSV must have at least a header and one data row.");

        string[] headers = ParseCsvLine(lines[0]);
        var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Detect format: "PlotId,Product" vs "Row,Column,Product"
        bool isRowColFormat = headers.Length >= 3 &&
            headers[0].Equals("Row", StringComparison.OrdinalIgnoreCase) &&
            headers[1].Equals("Column", StringComparison.OrdinalIgnoreCase);

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            string[] fields = ParseCsvLine(line);

            if (isRowColFormat && fields.Length >= 3)
            {
                // P4-2 FIX: Use TryParse with descriptive error message
                if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int row) ||
                    !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int col))
                {
                    throw new FormatException(
                        $"Trial map CSV line {i + 1}: expected numeric Row,Column but got '{fields[0]}','{fields[1]}'");
                }
                string product = fields[2].Trim();
                assignments[$"R{row}C{col}"] = product;
            }
            else if (fields.Length >= 2)
            {
                string plotId = fields[0].Trim();
                string product = fields[1].Trim();
                assignments[plotId] = product;
            }
        }

        return new TrialMap
        {
            TrialName = trialName ?? Path.GetFileNameWithoutExtension(filePath),
            PlotAssignments = assignments,
        };
    }

    /// <summary>
    /// Simple CSV line parser (handles quoted fields).
    /// </summary>
    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        foreach (char c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields.ToArray();
    }
}
