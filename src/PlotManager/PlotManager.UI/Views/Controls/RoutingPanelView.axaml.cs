using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using PlotManager.Core.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PlotManager.UI.Views.Controls;

public class RoutingRow
{
    public string Product { get; }
    public List<int> AvailableSections { get; }
    public int? SelectedSection { get; set; }

    public RoutingRow(string product, IEnumerable<int> sections)
    {
        Product = product;
        AvailableSections = sections.ToList();
        SelectedSection = null;
    }
}

public partial class RoutingPanelView : UserControl
{
    public HardwareRouting? CurrentRouting { get; private set; }
    public bool IsValid => CurrentRouting != null;
    public event EventHandler? RoutingChanged;

    public TrialMap? TrialMap { get; private set; }
    public ObservableCollection<RoutingRow> Rows { get; } = new();

    public RoutingPanelView()
    {
        InitializeComponent();
        GridRouting.ItemsSource = Rows;
    }

    private Window? GetParentWindow() => this.GetVisualRoot() as Window;

    public void SetRouting(HardwareRouting routing)
    {
        CurrentRouting = routing;
        RoutingChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetTrialMap(TrialMap trialMap)
    {
        TrialMap = trialMap;
        Rows.Clear();
        
        var sections = Enumerable.Range(1, HardwareRouting.TotalSections).ToList();

        foreach (string product in trialMap.Products.OrderBy(p => p))
        {
            Rows.Add(new RoutingRow(product, sections));
        }

        BtnSave.IsEnabled = true;
        LblStatus.Text = $"{trialMap.Products.Count} продуктів потребують призначення";
        LblStatus.Foreground = new SolidColorBrush(Color.Parse("#FFA726")); // AccentOrange
    }

    private async void OnSaveRouting(object? sender, RoutedEventArgs e)
    {
        var owner = GetParentWindow();
        if (TrialMap == null) return;

        var productToSections = new Dictionary<string, List<int>>();
        var sectionToProduct = new Dictionary<int, string>();
        var errors = new List<string>();

        foreach (var row in Rows)
        {
            if (string.IsNullOrEmpty(row.Product)) continue;

            if (row.SelectedSection == null)
            {
                errors.Add($"Продукт '{row.Product}' не має призначеної секції.");
                continue;
            }

            int section = row.SelectedSection.Value;
            int sectionIdx = section - 1;

            if (sectionToProduct.ContainsKey(sectionIdx))
            {
                errors.Add($"Секція {section} призначена і '{sectionToProduct[sectionIdx]}', і '{row.Product}'.");
                continue;
            }

            productToSections[row.Product] = new List<int> { sectionIdx };
            sectionToProduct[sectionIdx] = row.Product;
        }

        if (errors.Count > 0)
        {
            if (owner != null)
                await MessageBoxHelper.ShowWarning(owner, "Помилки маршрутизації:\n\n" + string.Join("\n", errors.Select(err => $"• {err}")));
            return;
        }

        CurrentRouting = new HardwareRouting
        {
            ProductToSections = productToSections,
            SectionToProduct = sectionToProduct
        };

        var validationErrors = CurrentRouting.Validate(TrialMap);
        if (validationErrors.Count > 0)
        {
            if (owner != null)
                await MessageBoxHelper.ShowWarning(owner, "Не всі продукти призначені:\n\n" + string.Join("\n", validationErrors.Select(err => $"• {err}")));
            return;
        }

        LblStatus.Text = $"✅ Маршрутизація збережена — {sectionToProduct.Count} секцій призначено";
        LblStatus.Foreground = new SolidColorBrush(Color.Parse("#4CAF50")); // AccentGreen

        RoutingChanged?.Invoke(this, EventArgs.Empty);
    }
}
