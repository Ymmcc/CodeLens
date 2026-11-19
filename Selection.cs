using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LocalCodeLens
{
    internal sealed class Selection
    {
        internal const int MaxLength = 7000;
        internal string Text;
        internal string Error;
        internal Rect Anchor;

        internal static Selection Failure(Point pointer, string error)
        {
            return new Selection { Anchor = new Rect(pointer, new Size(0, 0)), Error = error };
        }

        internal static Selection Read(IntPtr foreground, Point pointer)
        {
            var result = new Selection { Anchor = new Rect(pointer, new Size(0, 0)) };
            if (foreground == IntPtr.Zero || foreground != Native.GetForegroundWindow())
            {
                result.Error = "前台窗口已切换，请重新选中代码。";
                return result;
            }

            var visited = new HashSet<AutomationElement>();
            try
            {
                // A browser can keep keyboard focus on the page root even after a mouse selection.
                var candidates = new[] {
                    AutomationElement.FocusedElement,
                    AutomationElement.FromPoint(pointer)
                };
                var root = AutomationElement.FromHandle(foreground);
                bool supportsSelection = false;
                foreach (var candidate in candidates)
                {
                    var element = candidate;
                    while (element != null && BelongsTo(element, foreground, root))
                    {
                        if (visited.Add(element))
                        {
                            if (element.Current.IsPassword)
                            {
                                result.Error = "密码输入框中的内容无法读取。";
                                return result;
                            }

                            object pattern;
                            if (element.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
                            {
                                var provider = (TextPattern)pattern;
                                if (provider.SupportedTextSelection != SupportedTextSelection.None)
                                {
                                    supportsSelection = true;
                                    ReadRanges(result, provider.GetSelection(), pointer);
                                    if (result.Text != null || result.Error != null)
                                        return result;
                                }
                            }
                        }

                        if (element.Equals(root))
                            break;
                        element = TreeWalker.RawViewWalker.GetParent(element);
                    }
                }

                result.Error = supportsSelection
                    ? "没有读到选中的文本。请选中一段代码，再按 " + Native.HotkeyLabel + "。"
                    : "当前应用没有提供可读取的选区。可在浏览器或支持文本选区的编辑器中打开代码。";
            }
            catch (ElementNotAvailableException)
            {
                result.Error = "原来的选区已失效，请重新选中代码。";
            }
            catch (InvalidOperationException)
            {
                result.Error = "当前应用无法读取选区，请重新选中后再试。";
            }
            catch (COMException)
            {
                result.Error = "当前应用无法读取选区，请重新选中后再试。";
            }
            catch (UnauthorizedAccessException)
            {
                result.Error = "当前应用的选区无法访问。请在普通权限的窗口中打开代码。";
            }
            return result;
        }

        private static bool BelongsTo(AutomationElement element, IntPtr foreground, AutomationElement root)
        {
            var current = element;
            while (current != null)
            {
                if (current.Equals(root))
                    return true;
                var handle = new IntPtr(current.Current.NativeWindowHandle);
                if (handle != IntPtr.Zero)
                    return Native.GetAncestor(handle, 2) == foreground;
                current = TreeWalker.RawViewWalker.GetParent(current);
            }
            return false;
        }

        private static void ReadRanges(Selection result, TextPatternRange[] ranges, Point pointer)
        {
            if (ranges == null)
                return;

            var pieces = new List<string>();
            int length = 0;
            double bestDistance = double.PositiveInfinity;
            foreach (var range in ranges)
            {
                int separator = pieces.Count == 0 ? 0 : 1;
                int available = MaxLength - length - separator;
                var text = range.GetText(Math.Max(1, available + 1));
                if (text.Length > Math.Max(0, available))
                {
                    result.Error = "选区超过 7,000 个字符。请缩小选区后再试。";
                    break;
                }
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                length += separator + text.Length;
                pieces.Add(text);
                foreach (var rect in range.GetBoundingRectangles())
                {
                    if (rect.IsEmpty || rect.Height <= 0)
                        continue;
                    double dx = Math.Max(0, Math.Max(rect.Left - pointer.X, pointer.X - rect.Right));
                    double dy = Math.Max(0, Math.Max(rect.Top - pointer.Y, pointer.Y - rect.Bottom));
                    double distance = dx * dx + dy * dy;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        result.Anchor = rect;
                    }
                }
            }
            if (pieces.Count > 0 && result.Error == null)
                result.Text = string.Join("\n", pieces);
        }
    }
}
