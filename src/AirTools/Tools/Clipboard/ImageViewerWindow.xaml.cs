using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AirTools.Tools.Clipboard
{
    public partial class ImageViewerWindow : Window
    {
        private double _scale = 1.0;
        private double _translateX = 0;
        private double _translateY = 0;
        private const double MinScale = 0.05;
        private const double MaxScale = 20.0;
        private bool _isDragging;
        private Point _dragStart;
        private double _dragStartTx;
        private double _dragStartTy;

        public ImageViewerWindow(BitmapSource image)
        {
            InitializeComponent();
            ImgPreview.Source = image;
            Loaded += (_, _) => FitToWindow();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            SyncThemeFromOwner();
        }

        private void SyncThemeFromOwner()
        {
            if (Owner == null) return;
            foreach (var key in new[] { "BgDark", "BgCard", "BgCardHover", "AccentColor", "TextPrimary", "TextSecondary", "TextDim", "BorderColor" })
            {
                if (Owner.Resources.Contains(key))
                    Resources[key] = Owner.Resources[key];
            }
            if (Owner.Resources.Contains("TextPrimary") && Owner.Resources["TextPrimary"] is SolidColorBrush brush)
            {
                var isDark = brush.Color.R + brush.Color.G + brush.Color.B < 384;
                Resources["CanvasBg"] = new SolidColorBrush(isDark ? Color.FromRgb(0xF0, 0xF0, 0xF0) : Color.FromRgb(0x08, 0x08, 0x0C));
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
        private void BtnZoomIn_Click(object sender, RoutedEventArgs e) => ZoomToCenter(1.25);
        private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => ZoomToCenter(1.0 / 1.25);
        private void BtnReset_Click(object sender, RoutedEventArgs e) => ResetView();
        private void BtnFitWindow_Click(object sender, RoutedEventArgs e) => FitToWindow();

        private void CanvasHost_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            var pos = e.GetPosition(CanvasHost);
            var factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) factor = e.Delta > 0 ? 1.4 : 1.0 / 1.4;
            ZoomAroundPoint(factor, pos);
        }

        private void CanvasHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _dragStart = e.GetPosition(this);
            _dragStartTx = _translateX;
            _dragStartTy = _translateY;
            CanvasHost.Cursor = Cursors.Hand;
            CanvasHost.CaptureMouse();
            e.Handled = true;
        }

        private void CanvasHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            CanvasHost.Cursor = Cursors.Arrow;
            CanvasHost.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void CanvasHost_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            var current = e.GetPosition(this);
            _translateX = _dragStartTx + (current.X - _dragStart.X);
            _translateY = _dragStartTy + (current.Y - _dragStart.Y);
            ApplyTransform();
        }

        private void CanvasHost_SizeChanged(object sender, SizeChangedEventArgs e) => CenterImage();

        private void ZoomAroundPoint(double factor, Point viewportPoint)
        {
            var newScale = Math.Clamp(_scale * factor, MinScale, MaxScale);
            if (Math.Abs(newScale - _scale) < 1e-9) return;
            var imageX = (viewportPoint.X - _translateX) / _scale;
            var imageY = (viewportPoint.Y - _translateY) / _scale;
            _scale = newScale;
            _translateX = viewportPoint.X - imageX * _scale;
            _translateY = viewportPoint.Y - imageY * _scale;
            ApplyTransform();
        }

        private void ZoomToCenter(double factor)
        {
            var center = new Point(CanvasHost.ActualWidth / 2, CanvasHost.ActualHeight / 2);
            ZoomAroundPoint(factor, center);
        }

        private void ResetView() { _scale = 1.0; CenterImage(); }

        private void FitToWindow()
        {
            if (ImgPreview.Source is not BitmapSource bmp) return;
            var cw = CanvasHost.ActualWidth;
            var ch = CanvasHost.ActualHeight;
            if (cw <= 0 || ch <= 0) return;
            var scaleX = cw / bmp.PixelWidth;
            var scaleY = ch / bmp.PixelHeight;
            _scale = Math.Min(scaleX, scaleY) * 0.92;
            _scale = Math.Clamp(_scale, MinScale, MaxScale);
            CenterImage();
        }

        private void CenterImage()
        {
            if (ImgPreview.Source is not BitmapSource bmp) return;
            var cw = CanvasHost.ActualWidth;
            var ch = CanvasHost.ActualHeight;
            if (cw <= 0 || ch <= 0) return;
            var imgW = bmp.PixelWidth * _scale;
            var imgH = bmp.PixelHeight * _scale;
            _translateX = (cw - imgW) / 2;
            _translateY = (ch - imgH) / 2;
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            var matrix = new Matrix();
            matrix.Scale(_scale, _scale);
            matrix.OffsetX = _translateX;
            matrix.OffsetY = _translateY;
            ImageTransform.Matrix = matrix;
            TxtZoom.Text = $"{_scale * 100:0}%";
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape: Close(); e.Handled = true; break;
                case Key.Add or Key.OemPlus: ZoomToCenter(1.25); e.Handled = true; break;
                case Key.Subtract or Key.OemMinus: ZoomToCenter(1.0 / 1.25); e.Handled = true; break;
                case Key.D0 or Key.NumPad0:
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { ResetView(); e.Handled = true; }
                    break;
            }
        }
    }
}
