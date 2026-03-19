using Avalonia.Controls;
using System.IO;

namespace PlotManager.UI.Views;

public partial class LogViewerWindow : Window
{
    public LogViewerWindow(string logFilePath)
    {
        InitializeComponent();
        Title = $"📝 Log — {Path.GetFileName(logFilePath)}";
    }
}
