using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using PlotManager.Core.Models;
using System;
using System.IO;
using System.Linq;

namespace PlotManager.UI.Views;

public partial class ProfileManagerWindow : Window
{
    private readonly string _profilesDir;
    
    public MachineProfile? SelectedProfile { get; private set; }
    public string? SelectedPath { get; private set; }

    public ProfileManagerWindow()
    {
        InitializeComponent();
        _profilesDir = GetProfilesDirectory();
        Directory.CreateDirectory(_profilesDir);
        LoadProfileList();
    }

    public static string GetProfilesDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "AOGPlotManager", "profiles");
    }

    public static void AutoSaveProfile(MachineProfile profile)
    {
        string dir = GetProfilesDirectory();
        Directory.CreateDirectory(dir);

        string safeName = SanitizeFileName(profile.ProfileName);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "profile";

        string path = Path.Combine(dir, $"{safeName}.json");
        profile.SaveToFile(path);
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string result = new(name.Where(c => !invalid.Contains(c)).ToArray());
        return result.Trim();
    }

    private void LoadProfileList()
    {
        lstProfiles.Items.Clear();
        SelectedProfile = null;
        SelectedPath = null;

        if (!Directory.Exists(_profilesDir))
        {
            lblInfo.Text = "📁 Папка профілів порожня. Створіть новий профіль.";
            return;
        }

        var files = Directory.GetFiles(_profilesDir, "*.json")
            .OrderByDescending(File.GetLastWriteTime)
            .ToArray();

        if (files.Length == 0)
        {
            lblInfo.Text = "📁 Немає збережених профілів. Створіть новий.";
            return;
        }

        lblInfo.Text = $"Знайдено {files.Length} профіль(ів).";

        foreach (string file in files)
        {
            try
            {
                var p = MachineProfile.LoadFromFile(file);
                string display = $"  {p.ProfileName}   ({p.Booms.Count} штанг, {p.Nozzle.Model})";
                lstProfiles.Items.Add(new ProfileListItem(file, p, display));
            }
            catch
            {
                lstProfiles.Items.Add(new ProfileListItem(file, null, $"  ⚠ {Path.GetFileName(file)} (пошкоджений)"));
            }
        }

        if (lstProfiles.Items.Count > 0)
            lstProfiles.SelectedIndex = 0;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (lstProfiles.SelectedItem is not ProfileListItem item || item.Profile == null)
        {
            lblDetails.Text = "";
            return;
        }

        var p = item.Profile;
        int enabled = p.Booms.Count(b => b.Enabled);
        lblDetails.Text =
            $"📋 {p.ProfileName}   |   🔧 {p.Booms.Count} штанг ({enabled} акт.)   |   💧 {p.Nozzle.Model} ({p.Nozzle.ColorCode})   |   " +
            $"🚜 {p.TargetSpeedKmh:F1} км/год   |   📊 {p.TargetRateLPerHa:F0} л/га\n" +
            $"📅 {File.GetLastWriteTime(item.FilePath):yyyy-MM-dd HH:mm}   |   📁 {Path.GetFileName(item.FilePath)}";
    }

    private void OnLoadSelected(object? sender, RoutedEventArgs e)
    {
        if (lstProfiles.SelectedItem is not ProfileListItem item || item.Profile == null)
        {
            // TODO: Replace with proper MessageDialog if needed
            return;
        }

        SelectedProfile = item.Profile;
        SelectedPath = item.FilePath;
        Close(true);
    }

    private void OnDeleteSelected(object? sender, RoutedEventArgs e)
    {
        if (lstProfiles.SelectedItem is not ProfileListItem item)
        {
            return;
        }

        // Ideally show an async confirmation dialog here before deleting.
        // For now, we will just delete it to replicate form behavior but without blocking UI.
        // In real Avalonia port we'd use a MessageBox alternative.
        try
        {
            File.Delete(item.FilePath);
            LoadProfileList();
        }
        catch (Exception)
        {
            // Ignore for now
        }
    }

    private async void OnCreateNew(object? sender, RoutedEventArgs e)
    {
        var newProfile = MachineProfile.CreateDefault();
        var form = new MachineProfileWindow(newProfile);
        
        var result = await form.ShowDialog<bool>(this);
        if (result)
        {
            AutoSaveProfile(form.Profile);
            SelectedProfile = form.Profile;
            SelectedPath = null;
            Close(true);
        }
    }

    private void OnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    public class ProfileListItem
    {
        public string FilePath { get; }
        public MachineProfile? Profile { get; }
        public string DisplayName { get; }

        public ProfileListItem(string filePath, MachineProfile? profile, string display)
        {
            FilePath = filePath;
            Profile = profile;
            DisplayName = display;
        }
    }
}
