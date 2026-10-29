using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using GanttIt.Core.Layout;
using GanttIt.Revit.Services;

namespace GanttIt.Revit.UI.Preview;

/// <summary>
/// The read-only chart preview (§2.2, §6): draws a <see cref="ChartLayout"/>
/// with <see cref="PreviewRenderer"/>, scaled to fit. Used at full size in the
/// editor and small on home-screen cards.
/// </summary>
public sealed class PreviewCanvas : FrameworkElement
{
    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(ChartLayout), typeof(PreviewCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MetricsProperty = DependencyProperty.Register(
        nameof(Metrics), typeof(IReadOnlyDictionary<TextStyleKey, TextStyleMetrics>), typeof(PreviewCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsThumbnailProperty = DependencyProperty.Register(
        nameof(IsThumbnail), typeof(bool), typeof(PreviewCanvas),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty InsetProperty = DependencyProperty.Register(
        nameof(Inset), typeof(double), typeof(PreviewCanvas),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public ChartLayout? Layout
    {
        get => (ChartLayout?)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public IReadOnlyDictionary<TextStyleKey, TextStyleMetrics>? Metrics
    {
        get => (IReadOnlyDictionary<TextStyleKey, TextStyleMetrics>?)GetValue(MetricsProperty);
        set => SetValue(MetricsProperty, value);
    }

    public bool IsThumbnail
    {
        get => (bool)GetValue(IsThumbnailProperty);
        set => SetValue(IsThumbnailProperty, value);
    }

    /// <summary>Margin between the control's edge and the drawn chart, in DIPs.</summary>
    public double Inset
    {
        get => (double)GetValue(InsetProperty);
        set => SetValue(InsetProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Hit-testable background so the control has a visible extent.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (Layout is not { } layout || Metrics is not { } metrics) return;

        var inset = Math.Max(0, Inset);
        var target = new Rect(inset, inset,
            Math.Max(0, RenderSize.Width - 2 * inset), Math.Max(0, RenderSize.Height - 2 * inset));
        var dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        try
        {
            PreviewRenderer.Draw(dc, layout, metrics, target, dip, IsThumbnail);
        }
        catch (Exception)
        {
            // A preview must never break the window (OnRender runs on every layout pass); leave it blank.
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PreviewCanvasAutomationPeer(this);

    /// <summary>Exposes the preview to UI Automation as an image, so a VM driver can locate and capture it.</summary>
    private sealed class PreviewCanvasAutomationPeer(PreviewCanvas owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override string GetClassNameCore() => nameof(PreviewCanvas);
    }
}
