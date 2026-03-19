using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PlotManager.UI.Views;

/// <summary>
/// View model for a single wizard step item (drives data binding in WizardNavBar).
/// </summary>
public class StepItemVm
{
    public string Symbol         { get; set; } = "";
    public string Label          { get; set; } = "";
    public IBrush CircleBackground { get; set; } = Brushes.Gray;
    public IBrush CircleForeground { get; set; } = Brushes.White;
    public IBrush LabelColor       { get; set; } = Brushes.Gray;
    public IBrush LineColor        { get; set; } = Brushes.Transparent;
}

public partial class WizardNavBar : UserControl
{
    // ── Color resources ──
    private static readonly IBrush StepDoneBrush    = new SolidColorBrush(Color.Parse("#4CAF50"));
    private static readonly IBrush StepActiveBrush  = new SolidColorBrush(Color.Parse("#4287F5"));
    private static readonly IBrush StepPendingBrush = new SolidColorBrush(Color.Parse("#464B5A"));
    private static readonly IBrush TextPrimaryBrush = new SolidColorBrush(Color.Parse("#E6E9F0"));
    private static readonly IBrush TextDimBrush     = new SolidColorBrush(Color.Parse("#6E7687"));

    public string[] Steps { get; set; } = Array.Empty<string>();

    private int _currentStep;
    private int _maxReachedStep;

    public int CurrentStep
    {
        get => _currentStep;
        set
        {
            _currentStep = Steps.Length > 0 ? Math.Clamp(value, 0, Steps.Length - 1) : 0;
            if (_currentStep > _maxReachedStep) _maxReachedStep = _currentStep;
            RefreshSteps();
            StepChanged?.Invoke(this, _currentStep);
        }
    }

    public event EventHandler<int>? StepChanged;
    public event EventHandler? BackRequested;
    public event EventHandler? NextRequested;

    public WizardNavBar()
    {
        InitializeComponent();
    }

    public void SetNextText(string text)    => BtnNext.Content = text;
    public void SetNextEnabled(bool enabled) => BtnNext.IsEnabled = enabled;
    public void SetBackVisible(bool visible) => BtnBack.IsVisible = visible;

    /// <summary>Sets green accent on next button (for Launch), blue otherwise.</summary>
    public void SetNextAccent(bool green)
    {
        BtnNext.Classes.Clear();
        BtnNext.Classes.Add(green ? "success" : "accent");
    }

    private void RefreshSteps()
    {
        var items = new ObservableCollection<StepItemVm>();
        for (int i = 0; i < Steps.Length; i++)
        {
            IBrush bg, fg, label, line;
            string symbol;

            if (i < _currentStep)
            {
                bg     = StepDoneBrush;
                fg     = Brushes.White;
                label  = TextDimBrush;
                symbol = "✓";
                line   = StepDoneBrush;
            }
            else if (i == _currentStep)
            {
                bg     = StepActiveBrush;
                fg     = Brushes.White;
                label  = TextPrimaryBrush;
                symbol = (i + 1).ToString();
                line   = i == 0 ? Brushes.Transparent : StepDoneBrush;
            }
            else
            {
                bg     = StepPendingBrush;
                fg     = TextDimBrush;
                label  = TextDimBrush;
                symbol = (i + 1).ToString();
                line   = StepPendingBrush;
            }

            items.Add(new StepItemVm
            {
                Symbol          = symbol,
                Label           = Steps[i],
                CircleBackground = bg,
                CircleForeground = fg,
                LabelColor       = label,
                LineColor        = i == 0 ? Brushes.Transparent : line,
            });
        }

        StepsPanel.ItemsSource = items;
    }

    private void OnBack(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnNext(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        NextRequested?.Invoke(this, EventArgs.Empty);
}
