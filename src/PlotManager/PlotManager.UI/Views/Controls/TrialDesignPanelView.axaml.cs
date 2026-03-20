using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using PlotManager.Core.Models;
using PlotManager.Core.Services;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PlotManager.UI.Views.Controls;

public class TrialDesignSaveState
{
    public decimal PlotWidth { get; set; }
    public decimal PlotLength { get; set; }
    public decimal BufferWidth { get; set; }
    public decimal BufferLength { get; set; }
    public decimal Rows { get; set; }
    public decimal Columns { get; set; }
    public int DesignTypeIndex { get; set; }
    public string TreatmentsText { get; set; } = string.Empty;
}

public partial class TrialDesignPanelView : UserControl
{
    private readonly GridGenerator _gridGenerator = new();
    private readonly ExperimentDesigner _experimentDesigner = new();

    public PlotGrid? CurrentGrid { get; private set; }
    public TrialMap? CurrentTrialMap { get; private set; }
    public bool IsValid => CurrentGrid != null && CurrentTrialMap != null;
    public event EventHandler? DesignChanged;

    public TrialDesignPanelView()
    {
        InitializeComponent();
    }

    private Window? GetParentWindow() => this.GetVisualRoot() as Window;

    private async void OnGenerate(object? sender, RoutedEventArgs e)
    {
        try
        {
            var treatments = TxtTreatments.Text?
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList() ?? new System.Collections.Generic.List<string>();

            if (treatments.Count < 2)
            {
                await MessageBoxHelper.ShowWarning(GetParentWindow()!, "Потрібно задати мінімум 2 препарати.");
                return;
            }

            var parameters = new GridGenerator.GridParams
            {
                Rows = (int)(NumRows.Value ?? 10m),
                Columns = (int)(NumColumns.Value ?? 20m),
                PlotWidthMeters = (double)(NumPlotWidth.Value ?? 2.80m),
                PlotLengthMeters = (double)(NumPlotLength.Value ?? 10.24m),
                BufferWidthMeters = (double)(NumBufferWidth.Value ?? 0m),
                BufferLengthMeters = (double)(NumBufferLength.Value ?? 4m),
                Origin = new GeoPoint(0, 0),
                HeadingDegrees = 0.0,
            };

            CurrentGrid = _gridGenerator.Generate(parameters);

            ExperimentalDesignType designType = CmbDesignType.SelectedIndex switch
            {
                0 => ExperimentalDesignType.RCBD,
                1 => ExperimentalDesignType.CRD,
                2 => ExperimentalDesignType.LatinSquare,
                _ => ExperimentalDesignType.RCBD
            };

            CurrentTrialMap = _experimentDesigner.GenerateDesign(CurrentGrid, treatments, designType);

            GridPreview.SetGrid(CurrentGrid);
            GridPreview.SetTrialMap(CurrentTrialMap);

            LblGridInfo.Text = $"✅ Схема готова: {CurrentGrid.Rows} × {CurrentGrid.Columns} = {CurrentGrid.TotalPlots} делянок\nДизайн: {designType}\nПрепаратів: {treatments.Count}";
            LblGridInfo.Foreground = new SolidColorBrush(Color.Parse("#4CAF50"));

            DesignChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            await MessageBoxHelper.ShowError(GetParentWindow()!, $"Помилка генерації:\n{ex.Message}");
        }
    }

    private async void OnExportExcel(object? sender, RoutedEventArgs e)
    {
        var owner = GetParentWindow();
        if (owner == null) return;

        if (!IsValid)
        {
            await MessageBoxHelper.ShowInfo(owner, "Спочатку згенеруйте схему!");
            return;
        }

        var dlg = new Avalonia.Controls.SaveFileDialog
        {
            Filters = { new Avalonia.Controls.FileDialogFilter { Name = "Excel Files", Extensions = { "xlsx" } } },
            Title = "Експорт Схеми в Excel",
            DefaultExtension = "xlsx",
            InitialFileName = "TrialMap.xlsx"
        };
        var file = await dlg.ShowAsync(owner);
        if (!string.IsNullOrEmpty(file))
        {
            try
            {
                var exporter = new TrialExcelExporter();
                exporter.ExportToExcel(CurrentTrialMap!, CurrentGrid!, file);
                await MessageBoxHelper.ShowInfo(owner, "Схему успішно експортовано!");
            }
            catch (Exception ex)
            {
                await MessageBoxHelper.ShowError(owner, $"Помилка експорту: {ex.Message}");
            }
        }
    }

    private async void OnSaveJson(object? sender, RoutedEventArgs e)
    {
        var owner = GetParentWindow();
        if (owner == null) return;

        var dlg = new Avalonia.Controls.SaveFileDialog
        {
            Filters = { new Avalonia.Controls.FileDialogFilter { Name = "JSON Files", Extensions = { "json" } } },
            Title = "Зберегти Дизайн Досліду",
            DefaultExtension = "json",
            InitialFileName = "TrialDesign.json"
        };
        var file = await dlg.ShowAsync(owner);
        if (!string.IsNullOrEmpty(file))
        {
            try
            {
                var state = new TrialDesignSaveState
                {
                    PlotWidth = NumPlotWidth.Value ?? 2.80m,
                    PlotLength = NumPlotLength.Value ?? 10.24m,
                    BufferWidth = NumBufferWidth.Value ?? 0m,
                    BufferLength = NumBufferLength.Value ?? 4m,
                    Rows = NumRows.Value ?? 10m,
                    Columns = NumColumns.Value ?? 20m,
                    DesignTypeIndex = CmbDesignType.SelectedIndex,
                    TreatmentsText = TxtTreatments.Text ?? ""
                };
                string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(file, json);
                await MessageBoxHelper.ShowInfo(owner, "Дизайн успішно збережено!");
            }
            catch (Exception ex)
            {
                await MessageBoxHelper.ShowError(owner, $"Помилка збереження:\n{ex.Message}");
            }
        }
    }

    private async void OnLoadJson(object? sender, RoutedEventArgs e)
    {
        var owner = GetParentWindow();
        if (owner == null) return;

        var dlg = new Avalonia.Controls.OpenFileDialog
        {
            Filters = { new Avalonia.Controls.FileDialogFilter { Name = "JSON Files", Extensions = { "json" } } },
            Title = "Завантажити Дизайн Досліду"
        };
        var files = await dlg.ShowAsync(owner);
        if (files is { Length: > 0 })
        {
            try
            {
                string json = File.ReadAllText(files[0]);
                var state = JsonSerializer.Deserialize<TrialDesignSaveState>(json);
                if (state != null)
                {
                    NumPlotWidth.Value = state.PlotWidth;
                    NumPlotLength.Value = state.PlotLength;
                    NumBufferWidth.Value = state.BufferWidth;
                    NumBufferLength.Value = state.BufferLength;
                    NumRows.Value = state.Rows;
                    NumColumns.Value = state.Columns;
                    if (state.DesignTypeIndex >= 0 && state.DesignTypeIndex < CmbDesignType.Items.Count)
                        CmbDesignType.SelectedIndex = state.DesignTypeIndex;
                    TxtTreatments.Text = state.TreatmentsText;

                    await MessageBoxHelper.ShowInfo(owner, "Дизайн успішно завантажено. Натисніть 'Згенерувати Схему', щоб відтворити сітку.");
                }
            }
            catch (Exception ex)
            {
                await MessageBoxHelper.ShowError(owner, $"Помилка завантаження:\n{ex.Message}");
            }
        }
    }

    private async void OnClear(object? sender, RoutedEventArgs e)
    {
        var owner = GetParentWindow();
        if (owner == null) return;

        bool ok = await MessageBoxHelper.ShowConfirm(owner, "Очистити поточний дизайн та сітку?");
        if (ok)
        {
            CurrentGrid = null;
            CurrentTrialMap = null;
            GridPreview.SetGrid(null);
            GridPreview.SetTrialMap(null);
            LblGridInfo.Text = "";
            DesignChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
