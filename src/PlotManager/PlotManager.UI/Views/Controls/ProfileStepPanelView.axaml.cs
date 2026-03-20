using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using PlotManager.Core.Models;
using System;
using System.IO;
using System.Linq;

namespace PlotManager.UI.Views.Controls;

public partial class ProfileStepPanelView : UserControl
{
    private static readonly string LastProfilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AOGPlotManager", "last_profile.txt");

    // Stub to emulate WinForms FormProfileManager.AutoSaveProfile behavior until its rewritten
    private static void AutoSaveProfile(MachineProfile p) { /* TODO in Phase 4 */ }

    private MachineProfile? _profile;
    public MachineProfile? Profile => _profile;
    public bool IsValid => _profile != null;
    public event EventHandler? ProfileChanged;

    public ProfileStepPanelView()
    {
        InitializeComponent();
        TryLoadLastProfile();
    }

    public void SetProfile(MachineProfile profile)
    {
        _profile = profile;
        UpdateSummary();
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSummary()
    {
        if (_profile == null)
        {
            LblStatus.Text = "⏳ Профіль не завантажено";
            LblStatus.Foreground = new SolidColorBrush(Color.Parse("#FFA726")); // AccentOrange
            LblProfileName.Text = "—";
            LblBoomCount.Text = "—";
            LblNozzle.Text = "—";
            LblSpeed.Text = "—";
            LblConnections.Text = "—";
            return;
        }

        LblStatus.Text = "✅ Профіль завантажено";
        LblStatus.Foreground = new SolidColorBrush(Color.Parse("#4CAF50")); // AccentGreen
        LblProfileName.Text = _profile.ProfileName;

        int enabled = _profile.Booms.Count(b => b.Enabled);
        LblBoomCount.Text = $"{_profile.Booms.Count} шт. ({enabled} активних)";

        LblNozzle.Text = string.IsNullOrEmpty(_profile.Nozzle.Model)
            ? "Не задано"
            : $"{_profile.Nozzle.Model} ({_profile.Nozzle.ColorCode}), {_profile.Nozzle.FlowRateLPerMin:F1} л/хв, норма {_profile.TargetRateLPerHa:F0} л/га";

        LblSpeed.Text = $"{_profile.TargetSpeedKmh:F1} ± {_profile.SpeedToleranceKmh:F1} км/год";

        LblConnections.Text = $"Teensy: {_profile.Connection.TeensyComPort}, AOG: {_profile.Connection.AogHost}:{_profile.Connection.AogUdpListenPort}/{_profile.Connection.AogUdpSendPort}";
    }

    private Window? GetParentWindow() => this.GetVisualRoot() as Window;

    private async void OnEditProfile(object? sender, RoutedEventArgs e)
    {
        _profile ??= MachineProfile.CreateDefault();
        var win = new MachineProfileWindow(_profile);
        // Emulating DialogResult.OK: assuming MachineProfileWindow sets its public Profile property on OK 
        // For now, in Avalonia we can return bool from ShowDialog
        var ok = await win.ShowDialog<bool>(GetParentWindow()!);
        if (ok)
        {
            SetProfile(win.Profile);
            AutoSaveProfile(win.Profile);
            SaveLastProfilePath(win.Profile.ProfileName);
        }
    }

    private async void OnNewProfile(object? sender, RoutedEventArgs e)
    {
        var newProfile = MachineProfile.CreateDefault();
        var win = new MachineProfileWindow(newProfile);
        var ok = await win.ShowDialog<bool>(GetParentWindow()!);
        if (ok)
        {
            SetProfile(win.Profile);
            AutoSaveProfile(win.Profile);
            SaveLastProfilePath(win.Profile.ProfileName);
        }
    }

    private async void OnOpenProfileManager(object? sender, RoutedEventArgs e)
    {
        // TODO: Implement ProfileManagerWindow
        await MessageBoxHelper.ShowInfo(GetParentWindow()!, "Profile Manager yet to be implemented in Phase 4.");
    }

    private async void OnLoadFromFile(object? sender, RoutedEventArgs e)
    {
        var owner = GetParentWindow();
        if (owner == null) return;

        var dlg = new Avalonia.Controls.OpenFileDialog
        {
            Filters = { new Avalonia.Controls.FileDialogFilter { Name = "Machine Profile", Extensions = { "json" } } },
            Title = "Завантажити профіль машини"
        };
        var files = await dlg.ShowAsync(owner);
        if (files is { Length: > 0 })
        {
            try
            {
                var loaded = MachineProfile.LoadFromFile(files[0]);
                SetProfile(loaded);
                SaveLastProfilePath(loaded.ProfileName);
            }
            catch (Exception ex)
            {
                await MessageBoxHelper.ShowError(owner, $"Помилка завантаження:\n{ex.Message}");
            }
        }
    }

    private static void SaveLastProfilePath(string profileName)
    {
        try
        {
            var dir = Path.GetDirectoryName(LastProfilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(LastProfilePath, profileName);
        }
        catch { }
    }

    private void TryLoadLastProfile()
    {
        try
        {
            if (!File.Exists(LastProfilePath)) return;
            var profileName = File.ReadAllText(LastProfilePath).Trim();
            if (string.IsNullOrEmpty(profileName)) return;

            var profilesDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AOGPlotManager", "profiles");

            if (!Directory.Exists(profilesDir)) return;

            foreach (var file in Directory.GetFiles(profilesDir, "*.json"))
            {
                try
                {
                    var p = MachineProfile.LoadFromFile(file);
                    if (p.ProfileName == profileName)
                    {
                        SetProfile(p);
                        return;
                    }
                }
                catch { }
            }
        }
        catch { }
    }
}
