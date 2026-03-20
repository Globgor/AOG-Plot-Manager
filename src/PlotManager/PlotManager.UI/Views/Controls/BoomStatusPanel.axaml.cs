using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;
using System;

namespace PlotManager.UI.Views.Controls;

public partial class BoomStatusPanel : UserControl
{
    private ushort _valveMask;
    private string _activeProduct = "—";
    private string _targetRate = "—";

    public BoomStatusPanel()
    {
        InitializeComponent();
    }

    public void UpdateValveMask(ushort mask)
    {
        if (_valveMask == mask) return;
        _valveMask = mask;
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);
    }

    public void UpdateProduct(string? product, string? rate)
    {
        string newProduct = product ?? "—";
        string newRate = rate ?? "—";
        if (_activeProduct == newProduct && _targetRate == newRate) return;
        
        _activeProduct = newProduct;
        _targetRate = newRate;
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.Custom(new BoomDrawOp(Bounds, _valveMask, _activeProduct, _targetRate));
    }

    private class BoomDrawOp : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly ushort _valveMask;
        private readonly string _activeProduct;
        private readonly string _targetRate;

        private const float LedSize = 22f;
        private const float LedSpacing = 6f;

        public BoomDrawOp(Rect bounds, ushort valveMask, string product, string rate)
        {
            _bounds = bounds;
            _valveMask = valveMask;
            _activeProduct = product;
            _targetRate = rate;
        }

        public Rect Bounds => _bounds;
        public void Dispose() { }
        public bool Equals(ICustomDrawOperation? other) => false;
        public bool HitTest(Point p) => false;

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null) return;
            using var lease = leaseFeature.Lease();
            SKCanvas canvas = lease.SkCanvas;

            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, (float)_bounds.Width, (float)_bounds.Height));

            using var ledOnFill = new SKPaint { Color = new SKColor(76, 175, 80), Style = SKPaintStyle.Fill, IsAntialias = true };
            using var ledOffFill = new SKPaint { Color = new SKColor(60, 60, 60), Style = SKPaintStyle.Fill, IsAntialias = true };
            using var ledGlow = new SKPaint { Color = new SKColor(76, 175, 80, 40), Style = SKPaintStyle.Fill, IsAntialias = true };
            using var ledBorder = new SKPaint { Color = new SKColor(100, 100, 100), Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };

            using var numFont = new SKPaint { Color = new SKColor(140, 140, 140), TextSize = 9, Typeface = SKTypeface.FromFamilyName("Segoe UI"), IsAntialias = true };
            using var prodFont = new SKPaint { Color = new SKColor(0, 188, 212), TextSize = 14, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), IsAntialias = true };
            using var rateFont = new SKPaint { Color = new SKColor(220, 220, 220), TextSize = 12, Typeface = SKTypeface.FromFamilyName("Segoe UI"), IsAntialias = true };

            float totalLedsWidth = 14f * LedSize + 13f * LedSpacing;
            float startX = Math.Max(8f, ((float)_bounds.Width / 2f) - (totalLedsWidth / 2f) - 60f); // Shifted a bit left to make room for text
            float ledY = 8f;
            float radius = LedSize / 2f;

            for (int i = 0; i < 14; i++)
            {
                float x = startX + i * (LedSize + LedSpacing);
                float centerX = x + radius;
                float centerY = ledY + radius;
                bool isOn = (_valveMask & (1 << i)) != 0;

                if (isOn)
                {
                    canvas.DrawCircle(centerX, centerY, radius + 3f, ledGlow);
                    canvas.DrawCircle(centerX, centerY, radius, ledOnFill);
                }
                else
                {
                    canvas.DrawCircle(centerX, centerY, radius, ledOffFill);
                }
                
                canvas.DrawCircle(centerX, centerY, radius, ledBorder);

                string label = (i + 1).ToString();
                float lw = numFont.MeasureText(label);
                numFont.GetFontMetrics(out SKFontMetrics m);
                canvas.DrawText(label, centerX - lw / 2, ledY + LedSize + 10 - m.Descent, numFont);
            }

            float labelX = startX + totalLedsWidth + 20f;
            prodFont.GetFontMetrics(out SKFontMetrics pm);
            canvas.DrawText(_activeProduct, labelX, ledY + 12 - pm.Ascent, prodFont);
            
            rateFont.GetFontMetrics(out SKFontMetrics rm);
            canvas.DrawText(_targetRate, labelX, ledY + 32 - rm.Ascent, rateFont);

            canvas.Restore();
        }
    }
}
