using Avalonia.Controls;
using System.IO;
using System.Threading.Tasks;

namespace PlotManager.UI.Views;

/// <summary>
/// Helper to show message dialogs with Avalonia (replaces WinForms MessageBox.Show).
/// Uses simple dialog windows to avoid complex MsBox API surface.
/// </summary>
public static class MessageBoxHelper
{
    public static async Task ShowError(Window owner, string message)
    {
        var dlg = new SimpleMessageWindow("❌ Помилка", message, isConfirm: false);
        await dlg.ShowDialog(owner);
    }

    public static async Task ShowInfo(Window owner, string message)
    {
        var dlg = new SimpleMessageWindow("ℹ Інформація", message, isConfirm: false);
        await dlg.ShowDialog(owner);
    }

    public static async Task<bool> ShowConfirm(Window owner, string message)
    {
        var dlg = new SimpleMessageWindow("Підтвердження", message, isConfirm: true);
        return await dlg.ShowDialog<bool>(owner);
    }

    public static async Task ShowWarning(Window owner, string message)
    {
        var dlg = new SimpleMessageWindow("⚠ Попередження", message, isConfirm: false);
        await dlg.ShowDialog(owner);
    }
}

/// <summary>Minimal Avalonia dialog window replacing MsBox.Avalonia.</summary>
public class SimpleMessageWindow : Window
{
    private readonly bool _isConfirm;
    private bool _result;

    public SimpleMessageWindow(string title, string message, bool isConfirm)
    {
        _isConfirm = isConfirm;
        Title = title;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var stack = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 16 };

        stack.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 360,
        });

        var btnRow = new WrapPanel { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, ItemWidth = double.NaN };
        if (isConfirm)
        {
            var btnYes = new Button { Content = "Так", Width = 80, Margin = new Avalonia.Thickness(0,0,8,0) };
            btnYes.Click += (_, _) => { _result = true; Close(); };
            btnRow.Children.Add(btnYes);
        }
        var btnOk = new Button { Content = isConfirm ? "Ні" : "OK", Width = 80 };
        btnOk.Click += (_, _) => Close();
        btnRow.Children.Add(btnOk);

        stack.Children.Add(btnRow);
        Content = stack;
    }

    public new async Task<bool> ShowDialog<T>(Window owner)
    {
        await ShowDialog(owner);
        return _result;
    }
}
