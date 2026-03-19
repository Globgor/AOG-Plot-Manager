using Avalonia.Controls;
using Avalonia.Interactivity;
using PlotManager.Core.Services;
using System;
using System.IO;

namespace PlotManager.UI.Views;

public partial class WelcomePanel : UserControl
{
    private readonly SessionService _sessionSvc = new();
    private string? _lastSessionPath;

    /// <summary>Fires when user wants to create a new setup.</summary>
    public event EventHandler? NewSetupRequested;

    /// <summary>Fires when user wants to load an existing machine profile.</summary>
    public event EventHandler? LoadProfileRequested;

    /// <summary>Fires when user wants to resume the last field session.</summary>
    public event EventHandler<string>? ResumeSessionRequested;

    public WelcomePanel()
    {
        InitializeComponent();
        RefreshResumeButton();
    }

    private void RefreshResumeButton()
    {
        _lastSessionPath = _sessionSvc.GetLatestSessionPath();
        BtnResume.IsEnabled = _lastSessionPath != null;
        LastSessionLabel.Text = _lastSessionPath != null
            ? $"↑ {Path.GetFileNameWithoutExtension(_lastSessionPath)}"
            : "Немає збережених сесій";
    }

    private void OnNew(object? sender, RoutedEventArgs e) =>
        NewSetupRequested?.Invoke(this, EventArgs.Empty);

    private void OnLoad(object? sender, RoutedEventArgs e) =>
        LoadProfileRequested?.Invoke(this, EventArgs.Empty);

    private void OnResume(object? sender, RoutedEventArgs e)
    {
        if (_lastSessionPath != null)
            ResumeSessionRequested?.Invoke(this, _lastSessionPath);
    }
}
