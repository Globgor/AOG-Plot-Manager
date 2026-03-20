using Avalonia.Controls;
using Avalonia.Interactivity;
using PlotManager.Core.Models;
using PlotManager.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace PlotManager.UI.Views;

public partial class MachineProfileWindow : Window
{
    private readonly MachineProfile _profile;
    private readonly NozzleCatalog _catalog;

    public ObservableCollection<BoomProfileWrapper> BoomItems { get; } = new();

    public MachineProfile Profile => _profile;

    public MachineProfileWindow()
    {
        InitializeComponent();
        
        // This is ONLY for Avalonia XAML designer preview. Do not use at runtime.
     _profile = MachineProfile.CreateDefault();
     _catalog = NozzleCatalog.CreateDefault();
    }

    public MachineProfileWindow(MachineProfile? existingProfile = null)
    {
        _profile = existingProfile ?? MachineProfile.CreateDefault();
        _catalog = NozzleCatalog.CreateDefault();

        InitializeComponent();

        dgvBooms.ItemsSource = BoomItems;

        // Setup handlers
        cmbNozzleType.SelectionChanged += OnNozzleTypeFilterChanged;
        cmbNozzle.SelectionChanged += OnNozzleSelectionChanged;
        
        var nuds = new[] { nudCalcSpeed, nudTargetRate, nudCalcSwath, nudCalcNozzlesPerBoom, nudFlowRate };
        foreach (var nud in nuds)
        {
            nud.ValueChanged += (_, _) => RecalcRate();
        }

        PopulateFromProfile();
    }

    // ── Validation and Data Transfer ──

    private void PopulateFromProfile()
    {
        // ── Tab 1: General ──
        txtProfileName.Text = _profile.ProfileName;
        txtNotes.Text = _profile.Notes;
        cmbFluidType.SelectedIndex = Math.Clamp((int)_profile.FluidType, 0, 2);
        nudPressure.Value = (decimal)_profile.OperatingPressureBar;
        nudAntennaHeight.Value = (decimal)_profile.AntennaHeightMeters;

        // ── Tab 2: Nozzle Calculator (Initially populated by Nozzle) ──
        nudCalcSpeed.Value = 6.0m;
        nudTargetRate.Value = (decimal)_profile.TargetRateLPerHa;
        nudCalcSwath.Value = 2.8m;
        nudCalcNozzlesPerBoom.Value = 1m;

        // ── Tab 3: Booms ──
        BoomItems.Clear();
        foreach (var b in _profile.Booms)
        {
            BoomItems.Add(new BoomProfileWrapper(b));
        }
        UpdateBoomsCountText();

        // ── Tab 4: Delays ──
        nudActivationDelay.Value = (decimal)_profile.SystemActivationDelayMs;
        nudDeactivationDelay.Value = (decimal)_profile.SystemDeactivationDelayMs;
        nudPreActivation.Value = (decimal)_profile.PreActivationMeters;
        nudPreDeactivation.Value = (decimal)_profile.PreDeactivationMeters;

        // ── Tab 5: Speed + GPS ──
        nudTargetSpeed.Value = (decimal)_profile.TargetSpeedKmh;
        nudSpeedTolerance.Value = (decimal)_profile.SpeedToleranceKmh;
        nudRtkTimeout.Value = (decimal)_profile.RtkLossTimeoutSeconds;
        nudGpsHz.Value = _profile.GpsUpdateRateHz;
        nudCogThreshold.Value = (decimal)_profile.CogHeadingThresholdDegrees;

        // ── Tab 6: Connections ──
        txtTeensyPort.Text = _profile.Connection.TeensyComPort;
        nudBaudRate.Value = _profile.Connection.TeensyBaudRate;
        txtAogHost.Text = _profile.Connection.AogHost;
        nudAogListenPort.Value = _profile.Connection.AogUdpListenPort;
        nudAogSendPort.Value = _profile.Connection.AogUdpSendPort;
        txtWeatherPort.Text = _profile.Connection.WeatherComPort;

        // Setup nozzle catalog now so it selects the right one
        OnNozzleTypeFilterChanged(null, null);
    }

    private bool TryCollectToProfile()
    {
        try
        {
            _profile.ProfileName = txtProfileName.Text?.Trim() ?? "New Profile";
            _profile.Notes = txtNotes.Text?.Trim() ?? "";
            _profile.FluidType = (FluidType)cmbFluidType.SelectedIndex;
            _profile.OperatingPressureBar = (double)(nudPressure.Value ?? 3.0m);
            _profile.AntennaHeightMeters = (double)(nudAntennaHeight.Value ?? 2.5m);

            _profile.SystemActivationDelayMs = (int)(nudActivationDelay.Value ?? 300m);
            _profile.SystemDeactivationDelayMs = (int)(nudDeactivationDelay.Value ?? 150m);
            _profile.PreActivationMeters = (double)(nudPreActivation.Value ?? 0.5m);
            _profile.PreDeactivationMeters = (double)(nudPreDeactivation.Value ?? 0.2m);

            _profile.TargetSpeedKmh = (double)(nudTargetSpeed.Value ?? 5.0m);
            _profile.SpeedToleranceKmh = (double)(nudSpeedTolerance.Value ?? 1.0m);
            _profile.RtkLossTimeoutSeconds = (double)(nudRtkTimeout.Value ?? 2.0m);
            _profile.GpsUpdateRateHz = (int)(nudGpsHz.Value ?? 10m);
            _profile.CogHeadingThresholdDegrees = (double)(nudCogThreshold.Value ?? 3.0m);

            _profile.Connection.TeensyComPort = txtTeensyPort.Text?.Trim() ?? "";
            _profile.Connection.TeensyBaudRate = (int)(nudBaudRate.Value ?? 115200m);
            _profile.Connection.AogHost = txtAogHost.Text?.Trim() ?? "127.0.0.1";
            _profile.Connection.AogUdpListenPort = (int)(nudAogListenPort.Value ?? 8888m);
            _profile.Connection.AogUdpSendPort = (int)(nudAogSendPort.Value ?? 9999m);
            _profile.Connection.WeatherComPort = txtWeatherPort.Text?.Trim() ?? "";

            _profile.TargetRateLPerHa = (double)(nudTargetRate.Value ?? 200m);

            // Fetch nozzle from UI selection
            string matchedModel = cmbNozzle.SelectedItem?.ToString() ?? "";
            var selectedNozzle = _catalog.Nozzles.FirstOrDefault(n => n.ToString() == matchedModel);
            if (selectedNozzle != null) 
            {
                _profile.Nozzle = new NozzleSpec
                {
                    Model = selectedNozzle.Model,
                    SprayAngleDegrees = selectedNozzle.SprayAngleDegrees,
                    FlowRateLPerMin = selectedNozzle.FlowRateLPerMinAtRef,
                    ColorCode = selectedNozzle.IsoColorCode
                };
            }
            else
            {
                // Fallback custom from UI
                _profile.Nozzle = new NozzleSpec
                {
                    Model = "Custom",
                    FlowRateLPerMin = (double)(nudFlowRate.Value ?? 1.14m),
                    SprayAngleDegrees = (int)(nudSprayAngle.Value ?? 110m),
                    ColorCode = txtNozzleColor.Text?.Trim() ?? "Custom"
                };
            }


            // Boom collection
            if (BoomItems.Count == 0) throw new Exception("Має бути хоча б одна штанга.");
            _profile.Booms.Clear();
            foreach (var b in BoomItems)
            {
                _profile.Booms.Add(b.ToProfile());
            }

            return true;
        }
        catch (Exception)
        {
            // Fallback (real app should use an async MessageBox equivalent but code-behind pattern means we will just do nothing and not close)
            return false;
        }
    }

    // ── Nozzle Logic ──

    private void OnNozzleTypeFilterChanged(object? sender, SelectionChangedEventArgs? e)
    {
        int filterIdx = cmbNozzleType.SelectedIndex;

        cmbNozzle.Items.Clear();
        var filtered = filterIdx switch
        {
            1 => _catalog.Nozzles.Where(n => n.Type == NozzleType.Slit),
            2 => _catalog.Nozzles.Where(n => n.Type == NozzleType.Injector),
            _ => _catalog.Nozzles.AsEnumerable(),
        };

        foreach (var n in filtered)
            cmbNozzle.Items.Add(n.ToString());

        // Try to match current profile nozzle or default to first
        int targetIdx = 0;
        string profTarget = _profile.Nozzle?.Model ?? "";
        for (int i=0; i < cmbNozzle.Items.Count; i++)
        {
            if (cmbNozzle.Items[i].ToString()?.Contains(profTarget) == true)
            {
                targetIdx = i;
                break;
            }
        }

        if (cmbNozzle.Items.Count > 0) cmbNozzle.SelectedIndex = targetIdx;
    }

    private void OnNozzleSelectionChanged(object? sender, SelectionChangedEventArgs? e)
    {
        string selectedText = cmbNozzle.SelectedItem?.ToString() ?? "";
        NozzleDefinition? selected = _catalog.Nozzles.FirstOrDefault(n => n.ToString() == selectedText);

        if (selected == null) return;

        nudSprayAngle.Value = selected.SprayAngleDegrees;
        nudFlowRate.Value = (decimal)selected.FlowRateLPerMinAtRef;
        txtNozzleColor.Text = selected.IsoColorCode;

        RecalcRate();
    }

    private void RecalcRate()
    {
        if (lblCalcWarnings == null) return; // not initialized yet

        try
        {
            double speed = (double)(nudCalcSpeed.Value ?? 6.0m);
            double targetRate = (double)(nudTargetRate.Value ?? 200m);
            double swath = (double)(nudCalcSwath.Value ?? 2.8m);
            int nozzlesPerBoom = (int)(nudCalcNozzlesPerBoom.Value ?? 1m);

            var nozzle = new NozzleDefinition
            {
                Model = cmbNozzle.SelectedItem?.ToString() ?? "Unknown",
                FlowRateLPerMinAtRef = (double)(nudFlowRate.Value ?? 1.14m),
                ReferencePressureBar = 2.76,
                SprayAngleDegrees = (int)(nudSprayAngle.Value ?? 110m),
            };

            double requiredFlowPerNozzle = targetRate * speed * swath / (600.0 * nozzlesPerBoom);
            double requiredPressure = nozzle.GetPressureForFlowRate(requiredFlowPerNozzle);
            double actualRate = RateCalculator.CalculateRateLPerHa(nozzle, requiredPressure, speed, swath, nozzlesPerBoom);

            lblCalcPressure.Text = $"🔧 Потрібний тиск: {requiredPressure:F2} бар";
            bool pressureOk = nozzle.IsPressureInRange(requiredPressure);
            lblCalcPressure.Foreground = Avalonia.Media.SolidColorBrush.Parse(pressureOk ? "#4CAF50" : "#F44336");

            lblCalcFlowPerNozzle.Text = $"💧 Вилив на форсунку: {requiredFlowPerNozzle:F2} л/хв";

            double flowAtRef = nozzle.FlowRateLPerMinAtRef;
            double recommendedSpeed = flowAtRef * 600.0 * nozzlesPerBoom / (targetRate * swath);

            lblCalcRecommendedSpeed.Text = $"🚜 Рекомендована швидкість: {recommendedSpeed:F1} км/год (при {nozzle.ReferencePressureBar:F1} бар)";
            bool speedOk = recommendedSpeed >= 2.0 && recommendedSpeed <= 12.0;
            lblCalcRecommendedSpeed.Foreground = Avalonia.Media.SolidColorBrush.Parse(speedOk ? "#4CAF50" : "#FF9800");

            lblCalcActualRate.Text = $"📊 Фактична норма: {actualRate:F1} л/га";

            var suggestions = new List<string>();
            foreach (var candidate in _catalog.Nozzles)
            {
                double candPressure = candidate.GetPressureForFlowRate(requiredFlowPerNozzle);
                if (candidate.IsPressureInRange(candPressure))
                {
                    double candFlow = candidate.GetFlowRateAtPressure(candPressure);
                    string typeTag = candidate.Type == NozzleType.Injector ? "інж" : "щіл";
                    suggestions.Add($"  ✅ {candidate.Model} [{typeTag}] → {candPressure:F1} бар, {candFlow:F2} л/хв");
                }
            }

            lblCalcNozzleSuggestion.Text = suggestions.Count > 0
                ? "🔎 Підходящі форсунки:\n" + string.Join("\n", suggestions.Take(5))
                : "❌ Жодна форсунка з каталогу не підходить!";
            lblCalcNozzleSuggestion.Foreground = Avalonia.Media.SolidColorBrush.Parse(suggestions.Count > 0 ? "#4CAF50" : "#F44336");

            var msgs = new List<string>();
            if (!pressureOk)
                msgs.Add($"⚠ Тиск {requiredPressure:F1} бар поза діапазоном ({nozzle.MinPressureBar:F1}–{nozzle.MaxPressureBar:F1}) — оберіть іншу форсунку зі списку ☝");
            if (speed < 2) msgs.Add("⚠ Швидкість < 2 км/год — дуже повільно.");
            if (speed > 10) msgs.Add("⚠ Швидкість > 10 км/год — можливий знос.");
            if (requiredPressure > 6) msgs.Add("💡 Спробуйте форсунку з більшим виливом (наприклад -04, -05).");
            if (requiredPressure < 1) msgs.Add("💡 Спробуйте форсунку з меншим виливом (наприклад -01, -02).");

            lblCalcWarnings.Text = msgs.Count > 0 ? string.Join("\n", msgs) : "✅ Параметри в нормі";
            lblCalcWarnings.Foreground = Avalonia.Media.SolidColorBrush.Parse(msgs.Count > 0 ? "#FF9800" : "#4CAF50");
        }
        catch
        {
            lblCalcPressure.Text = "";
            lblCalcFlowPerNozzle.Text = "";
            lblCalcActualRate.Text = "";
            lblCalcNozzleSuggestion.Text = "";
            lblCalcWarnings.Text = "";
        }
    }

    // ── Booms Editor Logic ──

    private void UpdateBoomsCountText()
    {
        lblBoomsCount.Text = $"Штанг: {BoomItems.Count}";
    }

    private void BtnAddBoom_Click(object? sender, RoutedEventArgs e)
    {
        int nextId = BoomItems.Count;
        var b = new BoomProfile
        {
            BoomId = nextId,
            Name = $"Boom {nextId + 1}",
            ValveChannel = nextId,
            YOffsetMeters = 0,
            XOffsetMeters = 0,
            SprayWidthMeters = 0.25,
            ActivationOverlapPercent = 70,
            DeactivationOverlapPercent = 30,
            ActivationDelayOverrideMs = -1,
            DeactivationDelayOverrideMs = -1,
            HoseLengthMeters = 0,
            Enabled = true
        };
        BoomItems.Add(new BoomProfileWrapper(b));
        UpdateBoomsCountText();
    }

    private void BtnRemoveBoom_Click(object? sender, RoutedEventArgs e)
    {
        if (dgvBooms.SelectedItems == null || dgvBooms.SelectedItems.Count == 0) return;
        
        var toRemove = dgvBooms.SelectedItems.Cast<BoomProfileWrapper>().ToList();
        foreach (var item in toRemove)
        {
            BoomItems.Remove(item);
        }
        
        // Re-index booms
        for (int i = 0; i < BoomItems.Count; i++)
        {
            BoomItems[i].BoomIdLabel = i + 1;
        }

        UpdateBoomsCountText();
    }

    // ── Main Action Buttons ──

    private void BtnOk_Click(object? sender, RoutedEventArgs e)
    {
        if (TryCollectToProfile())
        {
            Close(true);
        }
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void BtnLoadFile_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Port to proper StorageProvider API
    }

    private async void BtnSaveFile_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryCollectToProfile()) return;
        // TODO: Port to proper StorageProvider API
    }

    // ── Boom Data Wrapper for Avalonia Binding ──
    public class BoomProfileWrapper
    {
        public int BoomIdLabel { get; set; }
        public string Name { get; set; }
        public int ValveChannel { get; set; }
        public string YOffsetMeters { get; set; }
        public string XOffsetMeters { get; set; }
        public string SprayWidthMeters { get; set; }
        public string ActivationOverlapPercent { get; set; }
        public string DeactivationOverlapPercent { get; set; }
        public string ActivationDelayOverrideMs { get; set; }
        public string DeactivationDelayOverrideMs { get; set; }
        public string HoseLengthMeters { get; set; }
        public bool Enabled { get; set; }

        public BoomProfileWrapper(BoomProfile b)
        {
            BoomIdLabel = b.BoomId + 1;
            Name = b.Name;
            ValveChannel = b.ValveChannel;
            YOffsetMeters = b.YOffsetMeters.ToString("F2");
            XOffsetMeters = b.XOffsetMeters.ToString("F2");
            SprayWidthMeters = b.SprayWidthMeters.ToString("F2");
            ActivationOverlapPercent = b.ActivationOverlapPercent.ToString("F0");
            DeactivationOverlapPercent = b.DeactivationOverlapPercent.ToString("F0");
            ActivationDelayOverrideMs = b.ActivationDelayOverrideMs.ToString("F0");
            DeactivationDelayOverrideMs = b.DeactivationDelayOverrideMs.ToString("F0");
            HoseLengthMeters = b.HoseLengthMeters.ToString("F2");
            Enabled = b.Enabled;
        }

        public BoomProfile ToProfile()
        {
            return new BoomProfile
            {
                BoomId = BoomIdLabel - 1,
                Name = Name,
                ValveChannel = ValveChannel,
                YOffsetMeters = double.TryParse(YOffsetMeters, out var y) ? y : 0,
                XOffsetMeters = double.TryParse(XOffsetMeters, out var x) ? x : 0,
                SprayWidthMeters = double.TryParse(SprayWidthMeters, out var w) ? w : 0.25,
                ActivationOverlapPercent = double.TryParse(ActivationOverlapPercent, out var aop) ? aop : 70,
                DeactivationOverlapPercent = double.TryParse(DeactivationOverlapPercent, out var dop) ? dop : 30,
                ActivationDelayOverrideMs = int.TryParse(ActivationDelayOverrideMs, out var actMs) ? actMs : -1,
                DeactivationDelayOverrideMs = int.TryParse(DeactivationDelayOverrideMs, out var deactMs) ? deactMs : -1,
                HoseLengthMeters = double.TryParse(HoseLengthMeters, out var hl) ? hl : 0,
                Enabled = Enabled
            };
        }
    }
}
