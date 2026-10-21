using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TmfTrackConverter.Core;

namespace TmfTrackConverter.App;

public sealed class SchematicView : Grid
{
    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene),
        typeof(TrackScene),
        typeof(SchematicView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSceneChanged));

    private readonly Border _tipHost;
    private readonly TextBlock _tipText;
    private int? _hoverX;
    private int? _hoverZ;

    public TrackScene? Scene
    {
        get => (TrackScene?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public SchematicView()
    {
        Background = new SolidColorBrush(Color.FromRgb(22, 23, 28));
        ClipToBounds = true;
        Focusable = true;
        SnapsToDevicePixels = true;

        _tipText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(244, 241, 234)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap
        };
        _tipHost = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(27, 28, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(85, 87, 106)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = _tipText
        };
        Children.Add(_tipHost);

        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Background, null, new Rect(RenderSize));
        if (!TryGetLayout(out var size, out var pad, out var scale, out var originX, out var originY))
        {
            return;
        }

        dc.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(34, 36, 46)),
            new Pen(new SolidColorBrush(Color.FromRgb(70, 72, 86)), 1),
            new Rect(originX, originY, size.X * scale, size.Z * scale));

        foreach (var instance in Scene!.Instances)
        {
            if (instance.BlockName == "Environment")
            {
                continue;
            }

            var color = ColorFromName(instance.BlockName);
            var height = Math.Clamp(instance.Coord.Y / Math.Max(1.0, size.Y), 0, 1);
            color.R = (byte)Math.Clamp(color.R + (int)(height * 40), 0, 255);
            color.G = (byte)Math.Clamp(color.G + (int)(height * 20), 0, 255);

            var cells = instance.OccupiedCells.Count > 0
                ? instance.OccupiedCells
                : [instance.Coord];
            foreach (var cell in cells)
            {
                var x = originX + cell.X * scale;
                var y = originY + cell.Z * scale;
                dc.DrawRectangle(
                    new SolidColorBrush(color),
                    null,
                    new Rect(x, y, Math.Max(1, scale - 0.4), Math.Max(1, scale - 0.4)));
            }
        }

        if (_hoverX is int hx && _hoverZ is int hz && hx >= 0 && hz >= 0 && hx < size.X && hz < size.Z)
        {
            dc.DrawRectangle(
                null,
                new Pen(new SolidColorBrush(Color.FromRgb(232, 163, 23)), 1),
                new Rect(originX + hx * scale, originY + hz * scale, Math.Max(1, scale), Math.Max(1, scale)));
        }

        var label = $"{size.X} × {size.Z}  ·  {Scene.Instances.Count} placed";
        var text = new FormattedText(
            label,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            12,
            new SolidColorBrush(Color.FromRgb(168, 163, 154)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(pad, ActualHeight - pad - text.Height));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (!TryMapCell(pos, out var cellX, out var cellZ))
        {
            ClearHover();
            return;
        }

        if (_hoverX != cellX || _hoverZ != cellZ)
        {
            _hoverX = cellX;
            _hoverZ = cellZ;
            InvalidateVisual();
        }

        var lines = CollectCellLines(cellX, cellZ);
        if (lines.Count == 0)
        {
            _tipHost.Visibility = Visibility.Collapsed;
            return;
        }

        _tipText.Text = string.Join(Environment.NewLine, lines);
        _tipHost.Visibility = Visibility.Visible;
        _tipHost.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tipW = _tipHost.DesiredSize.Width;
        var tipH = _tipHost.DesiredSize.Height;
        var left = pos.X + 14;
        var top = pos.Y + 16;
        if (left + tipW > ActualWidth - 8)
        {
            left = Math.Max(8, pos.X - tipW - 10);
        }

        if (top + tipH > ActualHeight - 8)
        {
            top = Math.Max(8, pos.Y - tipH - 10);
        }

        _tipHost.Margin = new Thickness(left, top, 0, 0);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        ClearHover();
    }

    private void ClearHover()
    {
        if (_hoverX is null && _hoverZ is null && _tipHost.Visibility == Visibility.Collapsed)
        {
            return;
        }

        _hoverX = null;
        _hoverZ = null;
        _tipHost.Visibility = Visibility.Collapsed;
        InvalidateVisual();
    }

    private List<string> CollectCellLines(int cellX, int cellZ)
    {
        var lines = new List<string>();
        if (Scene is null)
        {
            return lines;
        }

        foreach (var instance in Scene.Instances)
        {
            if (instance.BlockName == "Environment")
            {
                continue;
            }

            var occupies = instance.OccupiedCells.Count > 0
                ? instance.OccupiedCells.Any(cell => cell.X == cellX && cell.Z == cellZ)
                : instance.Coord.X == cellX && instance.Coord.Z == cellZ;
            if (!occupies)
            {
                continue;
            }

            var layer = instance.IsGround ? "ground" : "air";
            lines.Add($"{instance.BlockName}  Y={instance.Coord.Y}  {instance.Direction}  {layer}");
        }

        return lines;
    }

    private bool TryMapCell(Point point, out int cellX, out int cellZ)
    {
        cellX = 0;
        cellZ = 0;
        if (!TryGetLayout(out var size, out _, out var scale, out var originX, out var originY))
        {
            return false;
        }

        var x = (point.X - originX) / scale;
        var z = (point.Y - originY) / scale;
        if (x < 0 || z < 0 || x >= size.X || z >= size.Z)
        {
            return false;
        }

        cellX = (int)x;
        cellZ = (int)z;
        return true;
    }

    private bool TryGetLayout(out GBX.NET.Int3 size, out double pad, out double scale, out double originX, out double originY)
    {
        pad = 16.0;
        scale = 1;
        originX = pad;
        originY = pad;
        size = default;
        if (Scene is null || Scene.Map.Size.X <= 0 || Scene.Map.Size.Z <= 0)
        {
            return false;
        }

        size = Scene.Map.Size;
        var availW = Math.Max(1, ActualWidth - pad * 2);
        var availH = Math.Max(1, ActualHeight - pad * 2);
        scale = Math.Min(availW / size.X, availH / size.Z);
        originX = pad + (availW - size.X * scale) / 2;
        originY = pad + (availH - size.Z * scale) / 2;
        return true;
    }

    private static void OnSceneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SchematicView view)
        {
            view.ClearHover();
        }
    }

    private static Color ColorFromName(string name)
    {
        var hash = name.GetHashCode();
        var r = (byte)(80 + (hash & 0x7F));
        var g = (byte)(70 + ((hash >> 8) & 0x7F));
        var b = (byte)(60 + ((hash >> 16) & 0x7F));
        return Color.FromRgb(r, g, b);
    }
}
