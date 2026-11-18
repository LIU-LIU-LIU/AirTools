using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using AirTools.Tools.Clipboard.Services;

namespace AirTools.Tools.Clipboard;

/// <summary>
/// Dock-stack style shelf: a small corner button at the bottom-right of the work area, with recent
/// texts fanned out above it along an arch that leans back to the right screen edge. Idle cards are
/// compact pills; the hovered card magnifies (neighbours a little) and stays anchored under the cursor.
/// Fully transparent pixels stay click-through, and the window never takes focus.
/// </summary>
public sealed class RackWindow : Window
{
    private const double WindowWidth = 440, Edge = 10, ButtonSize = 44, Gap = 8, Bulge = 64;
    private const double IdleW = 156, IdleH = 32, NearW = 200, NearH = 36, FarW = 164, HoverW = 292, HoverMaxH = 176;
    private const int Ms = 190;
    private static readonly string[] Rainbow = { "#FF7A6B", "#FFB454", "#F5D04C", "#6FCF8E", "#5AB0F0", "#A98BF0" };

    private readonly RecentTextRack _rack;
    private readonly Func<RackEntry, Task<string?>> _paste;
    private readonly Action _settings, _history;
    private readonly Canvas _canvas = new();
    private readonly Rectangle _pad = new() { Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Visibility = Visibility.Collapsed };
    private readonly Dictionary<Guid, Card> _cards = new();
    private readonly Card _emptyCard;
    private readonly Border _button;
    private readonly TextBlock _badge = new();
    private readonly Border _badgeHost;
    private readonly DispatcherTimer _leave = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly ContextMenu _menu;
    private RotateTransform _glyphRotate = new();
    private Card? _hover;
    private double _hoverAnchor;
    private bool _refreshPending, _busy, _dark = true;
    private Palette _p = Palette.Dark;
    // Where the corner button sits (centre, screen DIPs). Null anchor = bottom-right of the work area.
    private double? _anchorX, _anchorY;
    private double _cx, _cy;
    private bool _up = true, _right = true;           // fan direction: opens upward / leans away from the right edge
    private bool _pressed, _dragging;
    private Point _dragStart; private double _dragStartX, _dragStartY;

    public bool IsCollapsed { get; private set; }
    public event Action<bool>? CollapsedChanged;
    /// <summary>The user chose "hide" from the context menu.</summary>
    public event Action? HideRequested;
    /// <summary>A short message for the host to show (e.g. the pin limit was reached).</summary>
    public event Action<string>? Notice;
    /// <summary>The user dragged the rack (or reset it: both values null).</summary>
    public event Action<double?, double?>? AnchorChanged;
    /// <summary>Returns the paste shortcut shown for slot N (0 = newest), or empty.</summary>
    public Func<int, string>? SlotHint { get; set; }

    public RackWindow(RecentTextRack rack, Func<RackEntry, Task<string?>> paste, Action settings, Action history, bool collapsed = false)
    {
        _rack = rack; _paste = paste; _settings = settings; _history = history; IsCollapsed = collapsed;
        Title = "AirTools · 晾衣架"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true; Focusable = false;
        Width = WindowWidth; Height = 600;
        FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        Content = _canvas;
        _canvas.Children.Add(_pad);
        _pad.MouseMove += (_, e) => TrackPad(e.GetPosition(_canvas).Y);
        _pad.MouseLeave += (_, _) => _leave.Start();

        _menu = BuildMenu(null);
        _emptyCard = MakeCard(null);
        _badgeHost = new Border { MinWidth = 18, Height = 18, CornerRadius = new CornerRadius(9), Padding = new Thickness(5, 0, 5, 0), Child = _badge, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, -4, 0) };
        _badge.FontSize = 10.5; _badge.FontWeight = FontWeights.SemiBold; _badge.VerticalAlignment = VerticalAlignment.Center; _badge.HorizontalAlignment = HorizontalAlignment.Center;
        _button = MakeButton();
        _canvas.Children.Add(_button);

        _leave.Tick += (_, _) => { _leave.Stop(); if (!IsMouseOver) SetHover(null); };
        SourceInitialized += (_, _) => NonActivatingWindow.Apply(this);
        ((INotifyCollectionChanged)_rack.Items).CollectionChanged += RackChanged;
        _rack.PinsChanged += ScheduleRebuild;
        Closed += (_, _) => { ((INotifyCollectionChanged)_rack.Items).CollectionChanged -= RackChanged; _rack.PinsChanged -= ScheduleRebuild; };
        ApplyTheme(true);
        Rebuild(animate: false);
    }

    // ---------- placement ----------
    public void ShowDocked()
    {
        if (!IsVisible) Show();
        Rebuild(animate: false); // create cards for anything added since construction (restored pins)
        Dock();
        Layout(animate: false);
        if (!IsCollapsed) PlayFanOut();
    }

    /// <summary>Sets where the corner button sits (centre, screen DIPs); null = bottom-right corner. Call <see cref="Dock"/> to apply.</summary>
    public void SetAnchor(double? x, double? y)
    {
        bool ok = x is double ax && y is double ay && double.IsFinite(ax) && double.IsFinite(ay);
        _anchorX = ok ? x : null; _anchorY = ok ? y : null;
    }

    public void ResetPosition() { SetAnchor(null, null); Dock(); AnchorChanged?.Invoke(null, null); }

    private double ButtonTopY => _up ? Height - Edge - ButtonSize : Edge;

    /// <summary>Work area (DIPs) of the screen holding the given point; the primary work area for the default corner.</summary>
    private Rect WorkAreaFor(double? x, double? y)
    {
        if (x is not double px || y is not double py) return SystemParameters.WorkArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)(px * dpi.DpiScaleX), (int)(py * dpi.DpiScaleY))).WorkingArea;
        return new Rect(wa.Left / dpi.DpiScaleX, wa.Top / dpi.DpiScaleY, wa.Width / dpi.DpiScaleX, wa.Height / dpi.DpiScaleY);
    }

    /// <summary>
    /// Positions the (transparent) window so the corner button sits at the anchor. The fan opens away from the
    /// nearest vertical edge and leans into the screen, like a Dock stack: bottom-right → up and left,
    /// top-left → down and right, and so on.
    /// </summary>
    public void Dock()
    {
        var area = WorkAreaFor(_anchorX, _anchorY); double half = ButtonSize / 2;
        double cx = Math.Clamp(_anchorX ?? area.Right - Edge - half, area.Left + Edge + half, area.Right - Edge - half);
        double cy = Math.Clamp(_anchorY ?? area.Bottom - Edge - half, area.Top + Edge + half, area.Bottom - Edge - half);
        _cx = cx; _cy = cy;
        _up = cy > area.Top + area.Height / 2; _right = cx > area.Left + area.Width / 2;
        double room = _up ? cy + half + Edge - area.Top : area.Bottom - (cy - half - Edge);
        Height = Math.Max(220, Math.Min(room, 720));
        Left = _right ? cx + half + Edge - Width : cx - half - Edge;
        Top = _up ? cy + half + Edge - Height : cy - half - Edge;
        Canvas.SetLeft(_button, _right ? Width - Edge - ButtonSize : Edge); Canvas.SetTop(_button, ButtonTopY);
        if (IsVisible) Layout(animate: false);
    }

    /// <summary>Which way the fan currently opens (for tests).</summary>
    public (bool Up, bool Right) FanDirection => (_up, _right);

    private double ClampAnchor(double center) => _up ? Math.Clamp(center, 40, Height - Edge - ButtonSize - 40) : Math.Clamp(center, Edge + ButtonSize + 40, Height - 40);

    public void ApplyTheme(bool dark)
    {
        _dark = dark; _p = dark ? Palette.Dark : Palette.Light;
        foreach (var card in AllCards()) Paint(card);
        _button.Background = _p.Surface; _button.BorderBrush = _p.Stroke;
        ((TextBlock)((Grid)_button.Child).Children[0]).Foreground = _p.Text;
        _badgeHost.Background = _p.Accent; _badge.Foreground = Brushes.White;
    }

    /// <summary>Magnifies slot N as if hovered (tests / screenshots); null restores the idle fan.</summary>
    public void PreviewHover(int? index)
    {
        Card? card = index is int i && i >= 0 && i < _rack.Items.Count ? _cards[_rack.Items[i].Id] : null;
        if (card != null) _hoverAnchor = ClampAnchor(card.Center);
        _hover = card; Layout(animate: false);
    }

    /// <summary>Screen point (device pixels) at the centre of slot N — used by interaction tests.</summary>
    public Point? SlotScreenPoint(int index)
    {
        if (index < 0 || index >= _rack.Items.Count || !_cards.TryGetValue(_rack.Items[index].Id, out var card)) return null;
        return card.Root.PointToScreen(new Point(card.Root.ActualWidth / 2, card.Root.ActualHeight / 2));
    }

    // ---------- model sync ----------
    private void RackChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleRebuild();
    private void ScheduleRebuild()
    {
        if (_refreshPending) return;
        _refreshPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () => { _refreshPending = false; Rebuild(animate: true); });
    }

    private void Rebuild(bool animate)
    {
        foreach (var id in _cards.Keys.Where(id => _rack.Items.All(x => x.Id != id)).ToArray())
        { _canvas.Children.Remove(_cards[id].Root); if (_hover == _cards[id]) _hover = null; _cards.Remove(id); }
        foreach (var item in _rack.Items)
        {
            if (_cards.ContainsKey(item.Id)) continue;
            var card = MakeCard(item);
            _cards.Add(item.Id, card);
            if (animate && !IsCollapsed) card.Fresh = true;
        }
        for (int i = 0; i < _rack.Items.Count; i++) { var card = _cards[_rack.Items[i].Id]; card.ColorIndex = i; Paint(card); }
        _badge.Text = _rack.Items.Count.ToString();
        _badgeHost.Visibility = _rack.Items.Count > 0 && IsCollapsed ? Visibility.Visible : Visibility.Collapsed;
        Layout(animate);
    }

    private IEnumerable<Card> AllCards() => _cards.Values.Append(_emptyCard);

    /// <summary>Order away from the button: pinned first, then newest. An empty rack shows one hint card.</summary>
    private List<Card> Column()
    {
        // Items added since the last Rebuild have no card yet (e.g. pins restored at startup): skip, never throw.
        var list = _rack.Items.Where(x => _cards.ContainsKey(x.Id)).Select(x => _cards[x.Id]).ToList();
        // Full history is opened from the right-click menu / hotkey, not hung on the rack.
        if (list.Count == 0) list.Add(_emptyCard);
        return list;
    }

    // ---------- layout ----------
    private void Layout(bool animate)
    {
        var column = Column();
        foreach (var hidden in new[] { _emptyCard }.Where(c => !column.Contains(c))) { hidden.Root.Visibility = Visibility.Collapsed; hidden.Root.IsHitTestVisible = false; }
        int hoverIndex = _hover == null ? -1 : column.IndexOf(_hover);
        if (hoverIndex < 0) _hover = null;

        var sizes = column.Select((card, i) => TargetSize(card, i, hoverIndex)).ToArray();
        double buttonTop = ButtonTopY, edgeX = _right ? Width - Edge : Edge;
        // Cards stack away from the button: upward when the button is in the lower half of the screen, else downward.
        double dir = _up ? -1 : 1, start = _up ? buttonTop - 12 : buttonTop + ButtonSize + 12;
        var centers = new double[column.Count];
        double y = start;
        for (int i = 0; i < column.Count; i++) { centers[i] = y + dir * sizes[i].H / 2; y += dir * (sizes[i].H + Gap); }
        // Idle (unmagnified) centres are the stable reference: anchoring to them, not to the previous
        // hover layout, is what keeps repeated up/down mouse moves from drifting the stack.
        var idleCenters = new double[column.Count];
        y = start;
        for (int i = 0; i < column.Count; i++) { idleCenters[i] = y + dir * IdleH / 2; y += dir * (IdleH + Gap); }
        if (hoverIndex >= 0)
        {
            // Keep the magnified card at its idle position, but keep the whole stack clear of the
            // button (near side) and inside the window (far side).
            double shift = idleCenters[hoverIndex] - centers[hoverIndex];
            double Low(double s) => centers.Select((c, i) => c + s - sizes[i].H / 2).Min();
            double High(double s) => centers.Select((c, i) => c + s + sizes[i].H / 2).Max();
            if (_up)
            {
                if (High(shift) > buttonTop - 4) shift -= High(shift) - (buttonTop - 4);
                if (Low(shift) < 6) shift += 6 - Low(shift);
            }
            else
            {
                if (Low(shift) < buttonTop + ButtonSize + 4) shift += buttonTop + ButtonSize + 4 - Low(shift);
                if (High(shift) > Height - 6) shift -= High(shift) - (Height - 6);
            }
            for (int i = 0; i < centers.Length; i++) centers[i] += shift;
        }
        double span = Math.Max(5, column.Count) * (IdleH + Gap);
        double unionTop = double.MaxValue, unionLeft = double.MaxValue, unionBottom = double.MinValue, unionRight = double.MinValue;
        // Tilt follows the arch; mirroring either axis flips its sign.
        double tilt = _right == _up ? -7 : 7;
        for (int i = 0; i < column.Count; i++)
        {
            var card = column[i]; var (w, h) = sizes[i];
            double t = Math.Clamp((centers[i] - start) * dir / span, 0, 1);
            double bow = Bulge * Math.Sin(Math.PI * t);
            double x = _right ? edgeX - bow : edgeX + bow + w;   // x - w is always the card's left edge
            double angle = i == hoverIndex ? 0 : tilt * Math.Cos(Math.PI * t);
            card.Root.RenderTransformOrigin = new Point(_right ? 1 : 0, .5);
            card.Root.Visibility = Visibility.Visible;
            card.Root.IsHitTestVisible = !IsCollapsed && card.Removing == false;
            bool expanded = i == hoverIndex;
            card.Text.TextWrapping = expanded ? TextWrapping.Wrap : TextWrapping.NoWrap;
            card.Text.Text = expanded ? card.Full : card.OneLine;
            card.Text.FontSize = expanded ? 13 : 12;
            card.Text.VerticalAlignment = expanded ? VerticalAlignment.Top : VerticalAlignment.Center;
            card.Text.Margin = expanded ? new Thickness(0, 10, 0, 6) : new Thickness(0);
            card.Dot.VerticalAlignment = expanded ? VerticalAlignment.Top : VerticalAlignment.Center;
            if (expanded) card.Dot.Margin = new Thickness(0, card.Dot is Ellipse ? 16 : 13, 9, 0); else card.Dot.Margin = new Thickness(0, 0, 9, 0);
            card.Footer.Visibility = expanded && card.Kind == CardKind.Item ? Visibility.Visible : Visibility.Collapsed;
            card.Actions.Visibility = expanded && card.Kind == CardKind.Item ? Visibility.Visible : Visibility.Collapsed;
            card.PinMark.Visibility = !expanded && card.Entry?.Pinned == true ? Visibility.Visible : Visibility.Collapsed;
            double dim = card.Kind == CardKind.Empty ? .72 : hoverIndex < 0 ? .9 : 1;
            bool fresh = card.Fresh; card.Fresh = false;
            if (fresh)
            {
                // New clipboard text: pop in from the corner button.
                Set(card.Root, Canvas.LeftProperty, x - w); Set(card.Root, Canvas.TopProperty, centers[i] - h / 2);
                Set(card.Root, WidthProperty, w); Set(card.Root, HeightProperty, h); Set(card.Rotate, RotateTransform.AngleProperty, angle);
                Set(card.Scale, ScaleTransform.ScaleXProperty, .4); Set(card.Scale, ScaleTransform.ScaleYProperty, .4);
                Set(card.Shift, TranslateTransform.YProperty, buttonTop + ButtonSize / 2 - centers[i]); Set(card.Root, OpacityProperty, 0);
                var spring = new BackEase { Amplitude = .35, EasingMode = EasingMode.EaseOut };
                To(card.Scale, ScaleTransform.ScaleXProperty, 1, 360, spring); To(card.Scale, ScaleTransform.ScaleYProperty, 1, 360, spring);
                To(card.Shift, TranslateTransform.YProperty, 0, 360, spring); To(card.Root, OpacityProperty, dim, 220);
            }
            else if (animate && !IsCollapsed)
            {
                To(card.Root, Canvas.LeftProperty, x - w, Ms); To(card.Root, Canvas.TopProperty, centers[i] - h / 2, Ms);
                To(card.Root, WidthProperty, w, Ms); To(card.Root, HeightProperty, h, Ms);
                To(card.Rotate, RotateTransform.AngleProperty, angle, Ms);
                if (!IsCollapsed && !card.Removing) To(card.Root, OpacityProperty, dim, Ms);
            }
            else
            {
                Set(card.Root, Canvas.LeftProperty, x - w); Set(card.Root, Canvas.TopProperty, centers[i] - h / 2);
                Set(card.Root, WidthProperty, w); Set(card.Root, HeightProperty, h); Set(card.Rotate, RotateTransform.AngleProperty, angle);
                if (IsCollapsed) CollapsedPose(card, centers[i], buttonTop); else { Set(card.Root, OpacityProperty, dim); Set(card.Scale, ScaleTransform.ScaleXProperty, 1); Set(card.Scale, ScaleTransform.ScaleYProperty, 1); Set(card.Shift, TranslateTransform.YProperty, 0); }
            }
            card.Center = centers[i];
            Panel.SetZIndex(card.Root, i == hoverIndex ? 100 : 10 + Math.Max(0, 10 - Math.Abs(i - Math.Max(hoverIndex, 0))));
            unionTop = Math.Min(unionTop, centers[i] - h / 2); unionLeft = Math.Min(unionLeft, x - w);
            unionBottom = Math.Max(unionBottom, centers[i] + h / 2); unionRight = Math.Max(unionRight, x);
        }
        // Transparent pixels are click-through; while hovering, a near-invisible pad bridges the gaps between cards.
        _pad.Visibility = hoverIndex >= 0 ? Visibility.Visible : Visibility.Collapsed;
        double padLeft = _right ? Math.Max(0, unionLeft - 18) : 0, padRight = _right ? Width : Math.Min(Width, unionRight + 18);
        double padTop = _up ? Math.Max(0, unionTop - 14) : buttonTop + ButtonSize, padBottom = _up ? buttonTop : Math.Min(Height, unionBottom + 14);
        Canvas.SetLeft(_pad, padLeft); Canvas.SetTop(_pad, padTop);
        _pad.Width = Math.Max(0, padRight - padLeft); _pad.Height = Math.Max(0, padBottom - padTop);
    }

    private (double W, double H) TargetSize(Card card, int index, int hoverIndex)
    {
        if (hoverIndex < 0) return (card.Kind == CardKind.Empty ? 196 : IdleW, IdleH);
        int distance = Math.Abs(index - hoverIndex);
        if (distance == 0) return card.Kind == CardKind.Item ? (HoverW, MeasureExpanded(card)) : (220, 40);
        if (distance == 1) return (NearW, NearH);
        return (FarW, IdleH);
    }

    private double MeasureExpanded(Card card)
    {
        var probe = new TextBlock { Text = card.Full, FontSize = 13, LineHeight = 19, TextWrapping = TextWrapping.Wrap, FontFamily = FontFamily };
        probe.Measure(new Size(HoverW - 22 - 17 - 52, double.PositiveInfinity));
        return Math.Clamp(probe.DesiredSize.Height + 16 + 8 + 15 + 2, 64, HoverMaxH);
    }

    // ---------- hover ----------
    private void SetHover(Card? card)
    {
        if (card == _hover || IsCollapsed) return;
        if (card != null) _hoverAnchor = ClampAnchor(card.Center);
        _hover = card;
        Layout(animate: true);
    }

    private void TrackPad(double y)
    {
        // Between cards the pad keeps the fan open; only switch when the pointer is really inside another card.
        foreach (var card in Column())
        {
            double top = Canvas.GetTop(card.Root), h = card.Root.ActualHeight;
            if (y >= top && y <= top + h && card != _hover) { SetHover(card); return; }
        }
    }

    // ---------- collapse / fan ----------
    private void ToggleCollapsed()
    {
        IsCollapsed = !IsCollapsed; _hover = null;
        _badgeHost.Visibility = _rack.Items.Count > 0 && IsCollapsed ? Visibility.Visible : Visibility.Collapsed;
        CollapsedChanged?.Invoke(IsCollapsed);
        To(_glyphRotate, RotateTransform.AngleProperty, IsCollapsed ? -90 : 0, 260, new BackEase { Amplitude = .4, EasingMode = EasingMode.EaseOut });
        if (IsCollapsed) PlayFold(); else { Layout(animate: false); foreach (var c in Column()) CollapsedPose(c, c.Center, ButtonTopY); PlayFanOut(); }
    }

    private void CollapsedPose(Card card, double center, double buttonTop)
    {
        Set(card.Scale, ScaleTransform.ScaleXProperty, .25); Set(card.Scale, ScaleTransform.ScaleYProperty, .25);
        Set(card.Shift, TranslateTransform.YProperty, buttonTop + ButtonSize / 2 - center); Set(card.Root, OpacityProperty, 0);
        card.Root.IsHitTestVisible = false;
    }

    private void PlayFanOut()
    {
        var column = Column();
        for (int i = 0; i < column.Count; i++)
        {
            var card = column[i]; var delay = TimeSpan.FromMilliseconds(i * 32);
            var spring = new BackEase { Amplitude = .3, EasingMode = EasingMode.EaseOut };
            To(card.Scale, ScaleTransform.ScaleXProperty, 1, 340, spring, delay); To(card.Scale, ScaleTransform.ScaleYProperty, 1, 340, spring, delay);
            To(card.Shift, TranslateTransform.YProperty, 0, 340, spring, delay);
            To(card.Root, OpacityProperty, card.Kind == CardKind.Empty ? .72 : .9, 220, null, delay);
            card.Root.IsHitTestVisible = true;
        }
    }

    private void PlayFold()
    {
        var column = Column(); double buttonTop = ButtonTopY;
        _pad.Visibility = Visibility.Collapsed;
        for (int i = 0; i < column.Count; i++)
        {
            var card = column[i]; var delay = TimeSpan.FromMilliseconds((column.Count - 1 - i) * 22);
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            card.Root.IsHitTestVisible = false;
            To(card.Scale, ScaleTransform.ScaleXProperty, .25, 220, ease, delay); To(card.Scale, ScaleTransform.ScaleYProperty, .25, 220, ease, delay);
            To(card.Shift, TranslateTransform.YProperty, buttonTop + ButtonSize / 2 - card.Center, 220, ease, delay);
            To(card.Root, OpacityProperty, 0, 200, ease, delay);
        }
    }

    /// <summary>Re-reads slot shortcuts after settings change.</summary>
    public void RefreshHints() { foreach (var card in AllCards()) Paint(card); }

    // ---------- actions ----------
    private async Task PasteAsync(Card card)
    {
        if (_busy || card.Entry == null) return;
        _busy = true;
        Pulse(card);
        try
        {
            var error = await _paste(card.Entry);
            Flash(card, error == null ? "✓ 已粘贴" : error, error == null ? _p.Accent : _p.Danger);
        }
        catch (Exception ex) { Flash(card, "粘贴失败：" + ex.Message, _p.Danger); }
        finally { _busy = false; }
    }

    private static void Pulse(Card card)
    {
        var squeeze = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(260) };
        squeeze.KeyFrames.Add(new EasingDoubleKeyFrame(.94, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
        squeeze.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), new BackEase { Amplitude = .5, EasingMode = EasingMode.EaseOut }));
        card.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, squeeze); card.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, squeeze);
    }

    private void Flash(Card card, string message, Brush color)
    {
        card.Status.Text = message; card.Status.Foreground = color;
        card.Hint.Visibility = Visibility.Collapsed; card.Status.Visibility = Visibility.Visible;
        card.Root.BorderBrush = color;
        var restore = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(color == _p.Danger ? 2600 : 1000) };
        restore.Tick += (_, _) => { restore.Stop(); card.Status.Visibility = Visibility.Collapsed; card.Hint.Visibility = Visibility.Visible; card.Root.BorderBrush = _p.Stroke; };
        restore.Start();
    }

    private void TogglePin(Card card)
    {
        if (card.Entry == null) return;
        bool wantPin = !card.Entry.Pinned;
        if (_rack.SetPinned(card.Entry.Id, wantPin) || !wantPin) return;
        string message = $"最多固定 {Math.Max(0, _rack.Capacity - 1)} 条（至少留一个位置给新复制的内容），请先取消其他固定";
        Flash(card, message, _p.Danger); Notice?.Invoke(message);
    }

    private void TakeDown(Card card)
    {
        if (card.Entry == null || card.Removing) return;
        card.Removing = true; card.Root.IsHitTestVisible = false;
        if (_hover == card) _hover = null;
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var slide = new DoubleAnimation(0, _right ? 70 : -70, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
        slide.Completed += (_, _) => _rack.Remove(card.Entry.Id);
        card.Shift.BeginAnimation(TranslateTransform.XProperty, slide);
        card.Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
    }

    // ---------- visuals ----------
    private enum CardKind { Item, Empty }

    private sealed class Card
    {
        public required Border Root; public required TextBlock Text, Hint, Status, PinMark; public required FrameworkElement Footer, Actions, Dot; public required Button PinButton, Close;
        public required ScaleTransform Scale; public required RotateTransform Rotate; public required TranslateTransform Shift;
        public RackEntry? Entry; public CardKind Kind; public string Full = "", OneLine = "";
        public int ColorIndex; public double Center; public bool Fresh, Removing;
    }

    private Card MakeCard(RackEntry? entry)
    {
        var kind = entry != null ? CardKind.Item : CardKind.Empty;
        var scale = new ScaleTransform(1, 1); var rotate = new RotateTransform(0); var shift = new TranslateTransform();
        var root = new Border
        {
            CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), Padding = new Thickness(12, 0, 8, 0), ClipToBounds = false,
            Cursor = Cursors.Hand, ContextMenu = _menu, ToolTip = kind == CardKind.Empty ? "打开剪切板历史" : null, RenderTransformOrigin = new Point(1, .5), Width = IdleW, Height = IdleH,
            RenderTransform = new TransformGroup { Children = { scale, rotate, shift } },
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Direction = 270, Opacity = .22, Color = Colors.Black },
            SnapsToDevicePixels = true,
        };
        var grid = new Grid { ClipToBounds = true };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Child = grid;

        FrameworkElement dot = kind == CardKind.Item
            ? new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center }
            : new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 13, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(dot);

        var full = entry == null ? "" : entry.Preview.Trim();
        var oneLine = entry == null ? "" : Regex.Replace(full, @"\s+", " ");
        if (kind == CardKind.Empty) full = oneLine = "复制文字后会晾在这里";
        var text = new TextBlock { Text = oneLine, FontSize = 12, LineHeight = 19, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 7, 0, 7) };
        Grid.SetColumn(text, 1); grid.Children.Add(text);

        var footer = new Grid { Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
        var hint = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        var status = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed, FontWeight = FontWeights.SemiBold };
        footer.Children.Add(hint); footer.Children.Add(status);
        Grid.SetRow(footer, 1); Grid.SetColumn(footer, 1); grid.Children.Add(footer);

        // Hover actions: pin / unpin and take down. Hidden until the card is magnified.
        var pinButton = MiniButton("\uE718", "固定（不会被新复制挤掉）");
        var close = MiniButton("\uE711", "取下（历史中仍保留）");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, 6, 0, 0), Visibility = Visibility.Collapsed };
        actions.Children.Add(pinButton); actions.Children.Add(close);
        Grid.SetColumn(actions, 2); Grid.SetRowSpan(actions, 2); grid.Children.Add(actions);
        // Small pin mark on a pinned card while it is not magnified.
        var pinMark = new TextBlock { Text = "\uE718", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        Grid.SetColumn(pinMark, 2); Grid.SetRowSpan(pinMark, 2); grid.Children.Add(pinMark);

        var card = new Card { Root = root, Text = text, Hint = hint, Status = status, Footer = footer, Actions = actions, PinMark = pinMark, PinButton = pinButton, Close = close, Dot = dot, Scale = scale, Rotate = rotate, Shift = shift, Entry = entry, Kind = kind, Full = full, OneLine = oneLine };
        close.Click += (_, e) => { e.Handled = true; TakeDown(card); };
        pinButton.Click += (_, e) => { e.Handled = true; TogglePin(card); };
        if (kind != CardKind.Empty) root.MouseEnter += (_, _) => { _leave.Stop(); SetHover(card); };
        root.MouseLeave += (_, _) => _leave.Start();
        root.MouseLeftButtonUp += async (_, e) =>
        {
            e.Handled = true;
            if (kind != CardKind.Item) _history();
            else await PasteAsync(card);
        };
        if (kind == CardKind.Item) root.ContextMenu = BuildMenu(card);
        Paint(card);
        _canvas.Children.Add(root);
        return card;
    }

    private void Paint(Card card)
    {
        card.Root.Background = _p.Surface; card.Root.BorderBrush = _p.Stroke;
        card.Text.Foreground = card.Kind == CardKind.Empty ? _p.Secondary : _p.Text;
        card.Hint.Foreground = _p.Secondary;
        if (card.Dot is Ellipse dot) dot.Fill = Brush(Rainbow[card.ColorIndex % Rainbow.Length]);
        if (card.Dot is TextBlock glyph) glyph.Foreground = _p.Secondary;
        ((DropShadowEffect)card.Root.Effect).Opacity = _dark ? .38 : .16;
        card.Close.Foreground = _p.Secondary;
        bool pinned = card.Entry?.Pinned == true;
        card.PinMark.Foreground = _p.Accent;
        card.PinButton.Foreground = pinned ? _p.Accent : _p.Secondary;
        card.PinButton.ToolTip = pinned ? "取消固定" : "固定（不会被新复制挤掉）";
        if (card.Kind == CardKind.Item)
        {
            var shortcut = SlotHint?.Invoke(card.ColorIndex) ?? "";
            card.Hint.Text = (pinned ? "已固定 · " : "") + (shortcut.Length > 0 ? $"点击粘贴 · {shortcut}" : "点击粘贴");
        }
    }

    private Border MakeButton()
    {
        var scale = new ScaleTransform(1, 1);
        var glyph = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(.5, .5), RenderTransform = _glyphRotate };
        var grid = new Grid(); grid.Children.Add(glyph); grid.Children.Add(_badgeHost);
        var button = new Border
        {
            Width = ButtonSize, Height = ButtonSize, CornerRadius = new CornerRadius(ButtonSize / 2), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = grid,
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = scale, ToolTip = "晾衣架 · 单击收起 / 展开 · 拖动换位置 · 右键更多", ContextMenu = _menu,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = .28, Color = Colors.Black },
        };
        Panel.SetZIndex(button, 200);
        button.MouseEnter += (_, _) => { To(scale, ScaleTransform.ScaleXProperty, 1.1, 160); To(scale, ScaleTransform.ScaleYProperty, 1.1, 160); };
        button.MouseLeave += (_, _) => { To(scale, ScaleTransform.ScaleXProperty, 1, 200); To(scale, ScaleTransform.ScaleYProperty, 1, 200); };
        // Press = maybe a click, maybe a drag: it only becomes a drag after moving a few pixels.
        button.MouseLeftButtonDown += (_, e) =>
        {
            _pressed = true; _dragging = false; _dragStart = button.PointToScreen(e.GetPosition(button)); _dragStartX = _cx; _dragStartY = _cy;
            button.CaptureMouse(); e.Handled = true;
        };
        button.MouseMove += (_, e) =>
        {
            if (!_pressed || e.LeftButton != MouseButtonState.Pressed) return;
            var p = button.PointToScreen(e.GetPosition(button)); var dpi = VisualTreeHelper.GetDpi(this);
            double dx = (p.X - _dragStart.X) / dpi.DpiScaleX, dy = (p.Y - _dragStart.Y) / dpi.DpiScaleY;
            if (!_dragging && Math.Abs(dx) + Math.Abs(dy) < 4) return;
            _dragging = true; _hover = null; _leave.Stop();
            SetAnchor(_dragStartX + dx, _dragStartY + dy); Dock();
        };
        button.MouseLeftButtonUp += (_, e) =>
        {
            if (!_pressed) return;
            _pressed = false; button.ReleaseMouseCapture(); e.Handled = true;
            if (_dragging) { _dragging = false; AnchorChanged?.Invoke(_cx, _cy); }
            else ToggleCollapsed();
        };
        button.LostMouseCapture += (_, _) => { if (_dragging) { _dragging = false; AnchorChanged?.Invoke(_cx, _cy); } _pressed = false; };
        if (IsCollapsed) _glyphRotate.Angle = -90;
        return button;
    }

    private ContextMenu BuildMenu(Card? card)
    {
        MenuItem Item(string header, string glyph, Action action)
        {
            var item = new MenuItem { Header = header, Icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons") } };
            item.Click += (_, _) => action(); return item;
        }
        var menu = new ContextMenu();
        if (card?.Entry is RackEntry entry)
        {
            var pin = new MenuItem { Icon = new TextBlock { Text = "\uE718", FontFamily = new FontFamily("Segoe Fluent Icons") } };
            pin.Click += (_, _) => TogglePin(card);
            menu.Opened += (_, _) => pin.Header = entry.Pinned ? "取消固定" : "固定（不会被新复制挤掉）";
            menu.Items.Add(pin);
            menu.Items.Add(Item("取下（历史中仍保留）", "\uE711", () => TakeDown(card)));
            menu.Items.Add(new Separator());
        }
        var collapse = new MenuItem { Icon = new TextBlock { Text = "\uE70D", FontFamily = new FontFamily("Segoe Fluent Icons") } };
        collapse.Click += (_, _) => ToggleCollapsed();
        menu.Opened += (_, _) => collapse.Header = IsCollapsed ? "展开晾衣架" : "收起晾衣架";
        menu.Items.Add(collapse);
        menu.Items.Add(Item("打开剪切板历史", "\uE81C", _history));
        menu.Items.Add(Item("设置…", "\uE713", _settings));
        menu.Items.Add(Item("恢复到右下角", "\uE80F", ResetPosition));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("隐藏晾衣架", "\uED1A", () => HideRequested?.Invoke()));
        return menu;
    }

    private static Button MiniButton(string glyph, string tooltip) => new()
    {
        Content = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 10, ToolTip = tooltip, Width = 22, Height = 22, Padding = new Thickness(0),
        Focusable = false, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
        Template = MiniTemplate,
    };

    private static readonly ControlTemplate MiniTemplate = BuildMiniTemplate();
    private static ControlTemplate BuildMiniTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)), "Bd"));
        template.Triggers.Add(hover);
        return template;
    }

    // ---------- animation helpers ----------
    private static void To(IAnimatable target, DependencyProperty property, double to, int ms, IEasingFunction? ease = null, TimeSpan? delay = null)
    {
        // An unset Canvas.Left/Top (or Width/Height) is NaN and cannot be animated: WPF throws on the render
        // tick and the whole app dies. Snap such values into place instead.
        if (target is DependencyObject element && element.GetValue(property) is double current && double.IsNaN(current))
        { target.BeginAnimation(property, null); element.SetValue(property, to); return; }
        target.BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut }, BeginTime = delay ?? TimeSpan.Zero });
    }

    private static void Set(IAnimatable target, DependencyProperty property, double value)
    { target.BeginAnimation(property, null); ((DependencyObject)target).SetValue(property, value); }

    private static SolidColorBrush Brush(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }

    private sealed record Palette(Brush Surface, Brush Stroke, Brush Text, Brush Secondary, Brush Accent, Brush Danger)
    {
        public static readonly Palette Dark = new(Brush("#F02B2B2F"), Brush("#26FFFFFF"), Brush("#F3F3F3"), Brush("#A8A8AE"), Brush("#6CB4FF"), Brush("#FF8A80"));
        public static readonly Palette Light = new(Brush("#F7FCFCFD"), Brush("#1F000000"), Brush("#1B1B1F"), Brush("#5F6168"), Brush("#0F6CBD"), Brush("#C42B1C"));
    }
}
