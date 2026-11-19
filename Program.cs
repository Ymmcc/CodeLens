using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace LocalCodeLens
{
    internal sealed class Program
    {
        private Popup popup;
        private bool capturing;
        private Task<Selection> selectionRead;
        private static readonly TimeSpan SelectionTimeout = TimeSpan.FromSeconds(4);

        [STAThread]
        private static void Main()
        {
            bool firstInstance;
            using (var instance = new Mutex(true, @"Local\LocalCodeLens", out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show("工具已在系统托盘运行。选中代码后按 " + Native.HotkeyLabel + "。", "代码解释");
                    return;
                }
                new Program().Run();
            }
        }

        private void Run()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using (var sink = new HwndSource(new HwndSourceParameters("LocalCodeLens Hotkey") {
                WindowStyle = unchecked((int)0x80000000)
            }))
            using (var menu = new Forms.ContextMenuStrip())
            using (var tray = new Forms.NotifyIcon())
            {
                if (!Native.RegisterExplainHotkey(sink.Handle))
                {
                    MessageBox.Show("无法注册 " + Native.HotkeyLabel + "。请关闭占用该快捷键的程序，再启动工具。",
                        "代码解释", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    sink.AddHook(OnMessage);
                    menu.Items.Add("选中代码后按 " + Native.HotkeyLabel).Enabled = false;
                    menu.Items.Add("退出", null, (sender, args) => app.Shutdown());
                    tray.Icon = System.Drawing.SystemIcons.Information;
                    tray.Text = "代码解释 · " + Native.HotkeyLabel;
                    tray.ContextMenuStrip = menu;
                    tray.Visible = true;
                    app.Run();
                }
                finally
                {
                    Native.UnregisterHotKey(sink.Handle, Native.HotkeyId);
                }
            }
        }

        private IntPtr OnMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == Native.HotkeyMessage && wParam.ToInt32() == Native.HotkeyId)
            {
                handled = true;
                if (popup != null && popup.View.IsActive)
                    popup.View.Close();
                else if (!capturing)
                    ReadAndShow();
            }
            return IntPtr.Zero;
        }

        private async void ReadAndShow()
        {
            var foreground = Native.GetForegroundWindow();
            var pointer = Native.Cursor();
            var pending = popup ?? OpenPopup(pointer);
            pending.ShowReading();
            Selection selection;
            capturing = true;
            try
            {
                if (selectionRead != null && !selectionRead.IsCompleted)
                {
                    selection = Selection.Failure(pointer, "当前应用仍在响应选区读取。稍后再按 " + Native.HotkeyLabel + "。");
                }
                else
                {
                    var reading = Task.Run(() => Selection.Read(foreground, pointer));
                    selectionRead = reading;
                    selection = await Task.WhenAny(reading, Task.Delay(SelectionTimeout)) == reading
                        ? await reading
                        : Selection.Failure(pointer, "当前应用读取选区超时。请重新选中代码后再试。");
                }
            }
            catch (Exception)
            {
                selection = Selection.Failure(pointer, "当前应用无法读取选区，请重新选中后再试。");
            }
            finally
            {
                capturing = false;
            }
            if (popup != pending)
                return;

            bool sameForeground = foreground == Native.GetForegroundWindow();
            if (!sameForeground)
                selection = Selection.Failure(pointer, "前台窗口已切换，请重新选中代码。");

            if (pending.HasConversation)
            {
                if (selection.Text == null)
                {
                    pending.ShowFeedback(selection.Error);
                    if (sameForeground)
                        pending.View.Activate();
                    return;
                }
                pending.View.Close();
                pending = OpenPopup(pointer, selection.Anchor);
            }
            await pending.ShowSelection(selection, sameForeground);
        }

        private Popup OpenPopup(Point pointer, Rect? anchor = null)
        {
            var next = new Popup(pointer, anchor);
            next.View.Closed += (sender, args) =>
            {
                if (popup == next)
                    popup = null;
            };
            popup = next;
            next.View.Show();
            return next;
        }
    }
}
