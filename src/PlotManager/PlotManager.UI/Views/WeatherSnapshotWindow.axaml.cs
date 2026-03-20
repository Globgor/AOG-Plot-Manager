using Avalonia.Controls;
using Avalonia.Interactivity;
using PlotManager.Core.Models;
using System;
using System.Collections.Generic;

namespace PlotManager.UI.Views;

public partial class WeatherSnapshotWindow : Window
{
    public WeatherSnapshot? Result { get; private set; }

    public WeatherSnapshotWindow()
    {
        InitializeComponent();

        nudTemperature.ValueChanged += (_, _) => ValidateAndWarn();
        nudHumidity.ValueChanged += (_, _) => ValidateAndWarn();
        nudWindSpeed.ValueChanged += (_, _) => ValidateAndWarn();
    }

    private void BtnStart_Click(object? sender, RoutedEventArgs e)
    {
        Result = new WeatherSnapshot
        {
            TemperatureC = (double)(nudTemperature.Value ?? 20.0m),
            HumidityPercent = (double)(nudHumidity.Value ?? 60.0m),
            WindSpeedMs = (double)(nudWindSpeed.Value ?? 1.0m),
            WindDirection = (cboWindDirection.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "N/A",
            Notes = txtNotes.Text?.Trim() ?? string.Empty,
            Timestamp = DateTime.Now,
        };
        Close(Result);
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close(null);
    }

    private void ValidateAndWarn()
    {
        if (lblWarnings == null) return;

        var snapshot = new WeatherSnapshot
        {
            TemperatureC = (double)(nudTemperature.Value ?? 20.0m),
            HumidityPercent = (double)(nudHumidity.Value ?? 60.0m),
            WindSpeedMs = (double)(nudWindSpeed.Value ?? 1.0m),
        };

        List<string> warnings = snapshot.Validate();
        lblWarnings.Text = warnings.Count > 0
            ? "⚠️ " + string.Join(" | ", warnings)
            : string.Empty;
    }
}
