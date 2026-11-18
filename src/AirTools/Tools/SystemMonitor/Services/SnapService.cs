using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace AirTools.Tools.SystemMonitor.Services
{
    /// <summary>
    /// Magnetic snapping for a floating bar. Pure geometry (no WPF windows) so it can be tested.
    /// Candidates: the 4 corners, the 4 edge mid-points and the centre of the work area, all inset by a margin.
    /// A bar snaps per axis when it comes within <see cref="Threshold"/> of a candidate line.
    /// </summary>
    public static class SnapService
    {
        public const double Threshold = 22, Margin = 8;

        public readonly record struct Result(double Left, double Top, bool SnappedX, bool SnappedY, string? Anchor);

        /// <summary>Anchor names that map onto <c>MonitorSettings.Position</c> corners.</summary>
        public static string? CornerName(bool left, bool right, bool top, bool bottom)
            => (left, right, top, bottom) switch
            {
                (true, _, true, _) => "TopLeft", (_, true, true, _) => "TopRight",
                (true, _, _, true) => "BottomLeft", (_, true, _, true) => "BottomRight", _ => null,
            };

        public static Result Snap(double left, double top, double width, double height, Rect area, bool enabled)
        {
            if (!enabled) return new(left, top, false, false, null);
            // Vertical lines the bar's left / centre / right edge may stick to, and horizontal lines for top / middle / bottom.
            var xs = new (double Target, double Offset, string Name)[]
            {
                (area.Left + Margin, 0, "left"), (area.Left + area.Width / 2, -width / 2, "center"), (area.Right - Margin, -width, "right"),
            };
            var ys = new (double Target, double Offset, string Name)[]
            {
                (area.Top + Margin, 0, "top"), (area.Top + area.Height / 2, -height / 2, "middle"), (area.Bottom - Margin, -height, "bottom"),
            };
            var x = xs.Select(c => (Pos: c.Target + c.Offset, c.Name)).OrderBy(c => Math.Abs(c.Pos - left)).First();
            var y = ys.Select(c => (Pos: c.Target + c.Offset, c.Name)).OrderBy(c => Math.Abs(c.Pos - top)).First();
            bool sx = Math.Abs(x.Pos - left) <= Threshold, sy = Math.Abs(y.Pos - top) <= Threshold;
            string? anchor = sx && sy ? x.Name + "-" + y.Name : null;
            return new(sx ? x.Pos : left, sy ? y.Pos : top, sx, sy, anchor);
        }

        /// <summary>Corner names used by <c>MonitorSettings.Position</c> for an anchor like "left-top".</summary>
        public static string? CornerSetting(string? anchor) => anchor switch
        {
            "left-top" => "TopLeft", "right-top" => "TopRight", "left-bottom" => "BottomLeft", "right-bottom" => "BottomRight", _ => null,
        };
    }
}
