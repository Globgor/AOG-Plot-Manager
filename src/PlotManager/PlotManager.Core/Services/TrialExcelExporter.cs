using System;
using System.Drawing;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using PlotManager.Core.Models;

namespace PlotManager.Core.Services;

/// <summary>
/// Handles exporting TrialMap and PlotGrid data to Excel (.xlsx) format.
/// </summary>
public class TrialExcelExporter
{
    /// <summary>
    /// Exports a visual representation of the grid and a list of assignments to an Excel file.
    /// </summary>
    public void ExportToExcel(TrialMap trialMap, PlotGrid? grid, string filePath)
    {
        if (trialMap == null) throw new ArgumentNullException(nameof(trialMap));

        using var workbook = new XLWorkbook();
        
        // ── Sheet 1: Visual Map ──
        if (grid != null)
        {
            var wsMap = workbook.Worksheets.Add("Карта (Схема)");
            
            // Setup columns and rows for somewhat square-ish cells
            wsMap.ColumnWidth = 15;
            
            // Draw grid
            for (int r = 0; r < grid.Rows; r++)
            {
                wsMap.Row(r + 2).Height = 40; // 1-based index, row 1 is for headers

                for (int c = 0; c < grid.Columns; c++)
                {
                    string plotId = $"R{r + 1}C{c + 1}";
                    string? product = trialMap.GetProduct(plotId);
                    
                    var cell = wsMap.Cell(r + 2, c + 2); // Start at B2 (row=2, col=2)
                    
                    if (product != null)
                    {
                        cell.Value = $"{plotId}\n{product}";
                        cell.Style.Font.Bold = true;
                        cell.Style.Alignment.WrapText = true;
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        
                        // Pick a color based on product name (deterministic)
                        uint colorHash = (uint)product.GetHashCode();
                        byte red = (byte)((colorHash & 0xFF0000) >> 16);
                        byte green = (byte)((colorHash & 0x00FF00) >> 8);
                        byte blue = (byte)(colorHash & 0x0000FF);
                        
                        // Mix with white to make it pastel
                        red = (byte)((red + 255) / 2);
                        green = (byte)((green + 255) / 2);
                        blue = (byte)((blue + 255) / 2);
                        
                        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(red, green, blue);
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        cell.Style.Border.OutsideBorderColor = XLColor.Black;
                    }
                }
            }
            
            // Row and Column headers
            for (int r = 0; r < grid.Rows; r++)
            {
                var cell = wsMap.Cell(r + 2, 1);
                cell.Value = $"Ряд {r + 1}";
                cell.Style.Font.Bold = true;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            for (int c = 0; c < grid.Columns; c++)
            {
                var cell = wsMap.Cell(1, c + 2);
                cell.Value = $"Кол {c + 1}";
                cell.Style.Font.Bold = true;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
        }

        // ── Sheet 2: Data List ──
        var wsData = workbook.Worksheets.Add("Дані (Список)");
        wsData.Cell(1, 1).Value = "Ділянка (Plot ID)";
        wsData.Cell(1, 2).Value = "Препарат (Product)";
        var headerRange = wsData.Range("A1:B1");
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

        int rowIdx = 2;
        foreach (var plotId in trialMap.PlotAssignments.Keys.OrderBy(k => k))
        {
            wsData.Cell(rowIdx, 1).Value = plotId;
            wsData.Cell(rowIdx, 2).Value = trialMap.PlotAssignments[plotId];
            rowIdx++;
        }

        wsData.Columns().AdjustToContents();

        // ── Save ──
        workbook.SaveAs(filePath);
    }
}
