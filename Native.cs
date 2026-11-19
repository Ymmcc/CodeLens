using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace LocalCodeLens
{
    internal static class Native
    {
        internal const int HotkeyMessage = 0x0312;
        internal const int HotkeyId = 1;
        internal const string HotkeyLabel = "Ctrl+Alt+Q";
        private const uint ModifierControlAltNoRepeat = 0x4003;
        private const uint KeyQ = 0x51;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

        internal static bool RegisterExplainHotkey(IntPtr window)
        {
            return RegisterHotKey(window, HotkeyId, ModifierControlAltNoRepeat, KeyQ);
        }

        [DllImport("user32.dll")]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr window, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out PixelPoint point);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(PixelPoint point, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out PixelRect bounds);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        internal static Point Cursor()
        {
            PixelPoint point;
            GetCursorPos(out point);
            return new Point(point.X, point.Y);
        }

        internal static void PositionPopup(IntPtr window, Window popup, Rect anchor, Point pointer)
        {
            double anchorX = Math.Max(anchor.Left, Math.Min(pointer.X, anchor.Right));
            double anchorY = Math.Max(anchor.Top, Math.Min(pointer.Y, anchor.Bottom));
            var monitor = MonitorFromPoint(new PixelPoint { X = (int)anchorX, Y = (int)anchorY }, 2);
            PixelRect area;
            double scale;
            if (!ReadWorkArea(monitor, out area, out scale))
                return;

            int gap = (int)(12 * scale);
            popup.Width = Math.Min(popup.Width, (area.Right - area.Left - 2 * gap) / scale);
            popup.Height = Math.Min(popup.Height, (area.Bottom - area.Top - 2 * gap) / scale);
            int width = (int)(popup.Width * scale);
            int height = (int)(popup.Height * scale);
            int x = Math.Max(area.Left + gap, Math.Min((int)anchorX, area.Right - width - gap));
            int below = (int)Math.Ceiling(anchor.Bottom) + gap;
            int above = (int)Math.Floor(anchor.Top) - height - gap;
            int y = below + height <= area.Bottom - gap ? below : above;
            int right = (int)Math.Ceiling(anchor.Right) + gap;
            int left = (int)Math.Floor(anchor.Left) - width - gap;
            if (right + width <= area.Right - gap)
            {
                x = right;
                y = (int)anchor.Top;
            }
            else if (below + height > area.Bottom - gap && above < area.Top + gap && left >= area.Left + gap)
            {
                x = left;
                y = (int)anchor.Top;
            }
            y = Math.Max(area.Top + gap, Math.Min(y, area.Bottom - height - gap));
            // Move in screen pixels, including monitors to the left of the primary display.
            SetWindowPos(window, IntPtr.Zero, x, y, width, height, 0x0014);
            int round = 2;
            DwmSetWindowAttribute(window, 33, ref round, sizeof(int));
        }

        internal static double HeightLimit(IntPtr window, double preferredHeight)
        {
            PixelRect area;
            double scale;
            if (!ReadWorkArea(MonitorFromWindow(window, 2), out area, out scale))
                return preferredHeight;
            return Math.Min(preferredHeight, (area.Bottom - area.Top) / scale - 24);
        }

        internal static void KeepInWorkArea(IntPtr window)
        {
            PixelRect area, bounds;
            double scale;
            if (!GetWindowRect(window, out bounds) ||
                !ReadWorkArea(MonitorFromWindow(window, 2), out area, out scale))
                return;
            int gap = (int)(12 * scale);
            int x = Math.Max(area.Left + gap, Math.Min(bounds.Left, area.Right - (bounds.Right - bounds.Left) - gap));
            int y = Math.Max(area.Top + gap, Math.Min(bounds.Top, area.Bottom - (bounds.Bottom - bounds.Top) - gap));
            if (x != bounds.Left || y != bounds.Top)
                SetWindowPos(window, IntPtr.Zero, x, y, 0, 0, 0x0015);
        }

        private static bool ReadWorkArea(IntPtr monitor, out PixelRect area, out double scale)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            bool found = GetMonitorInfo(monitor, ref info);
            area = info.Work;
            uint dpiX, dpiY;
            scale = found && GetDpiForMonitor(monitor, 0, out dpiX, out dpiY) == 0 ? dpiX / 96.0 : 1;
            return found;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PixelPoint { internal int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PixelRect { internal int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo { internal int Size; internal PixelRect Monitor, Work; internal int Flags; }
    }
}
