using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LocalCodeLens
{
    internal sealed class Popup
    {
        private const double InitialHeight = 300;
        private const double ExpandedHeight = 430;
        private const int PreviewLength = 520;
        private readonly Point pointer;
        private readonly DispatcherTimer resizeTimer;
        private readonly FrameworkElement header;
        private readonly FrameworkElement footer;
        private readonly ScrollViewer scroll;
        private readonly StackPanel transcript;
        private readonly TextBlock notice;
        private readonly TextBlock status;
        private readonly TextBlock feedback;
        private readonly FrameworkElement composer;
        private readonly TextBox questionInput;
        private readonly Button send;
        private readonly Button dismiss;
        private readonly ToggleButton sourceToggle;
        private readonly FrameworkElement sourcePreview;
        private readonly TextBox sourceText;
        private readonly TextBlock previewLimit;
        private string selected;
        private Rect anchor;
        private IntPtr handle;
        private CancellationTokenSource request;
        private Ollama.Message previousQuestion;
        private Ollama.Message previousAnswer;
        private int followUpsLeft;
        private double targetHeight = double.NaN;
        private bool closed;
        private bool followLatest;
        private bool dragging;
        private bool manuallyMoved;

        internal Window View { get; private set; }
        internal bool HasConversation { get { return selected != null; } }

        internal Popup(Point pointer, Rect? anchor)
        {
            using (var xaml = Assembly.GetExecutingAssembly().GetManifestResourceStream("Popup.xaml"))
                View = (Window)XamlReader.Load(xaml);
            View.Height = InitialHeight;

            this.pointer = pointer;
            this.anchor = anchor ?? new Rect(pointer, new Size(0, 0));
            header = (FrameworkElement)View.FindName("Header");
            footer = (FrameworkElement)View.FindName("Footer");
            scroll = (ScrollViewer)View.FindName("TranscriptScroll");
            transcript = (StackPanel)View.FindName("Transcript");
            notice = (TextBlock)View.FindName("Notice");
            status = (TextBlock)View.FindName("Status");
            feedback = (TextBlock)View.FindName("Feedback");
            composer = (FrameworkElement)View.FindName("Composer");
            questionInput = (TextBox)View.FindName("Question");
            send = (Button)View.FindName("Send");
            dismiss = (Button)View.FindName("Dismiss");
            sourceToggle = (ToggleButton)View.FindName("SourceToggle");
            sourcePreview = (FrameworkElement)View.FindName("SourcePreview");
            sourceText = (TextBox)View.FindName("SourceText");
            previewLimit = (TextBlock)View.FindName("PreviewLimit");
            resizeTimer = new DispatcherTimer(DispatcherPriority.Render, View.Dispatcher) {
                Interval = TimeSpan.FromMilliseconds(40)
            };
            resizeTimer.Tick += (sender, args) =>
            {
                resizeTimer.Stop();
                ResizeToContent();
            };

            View.Loaded += (sender, args) =>
            {
                handle = new WindowInteropHelper(View).Handle;
                Native.PositionPopup(handle, View, this.anchor, pointer);
                FadeIn(View, 0.16);
                QueueResize();
            };
            View.SizeChanged += (sender, args) =>
            {
                if (handle != IntPtr.Zero && !dragging)
                    Native.KeepInWorkArea(handle);
                if (args.WidthChanged)
                    QueueResize();
            };
            footer.SizeChanged += (sender, args) => QueueResize();
            ((Border)View.FindName("TitleBar")).MouseLeftButtonDown += (sender, args) =>
            {
                if (args.ButtonState != MouseButtonState.Pressed)
                    return;
                args.Handled = true;
                dragging = true;
                manuallyMoved = true;
                try { View.DragMove(); }
                finally { dragging = false; }
                if (!closed)
                {
                    Native.KeepInWorkArea(handle);
                    QueueResize();
                }
            };
            View.Closed += (sender, args) =>
            {
                closed = true;
                resizeTimer.Stop();
                if (request != null)
                    request.Cancel();
            };
            View.PreviewKeyDown += (sender, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    args.Handled = true;
                    View.Close();
                }
            };
            dismiss.Click += (sender, args) => View.Close();
            sourceToggle.Checked += (sender, args) =>
            {
                followLatest = false;
                sourcePreview.Visibility = Visibility.Visible;
                FadeIn(sourcePreview, 0.17);
                scroll.ScrollToTop();
                QueueResize();
            };
            sourceToggle.Unchecked += (sender, args) =>
            {
                sourcePreview.Visibility = Visibility.Collapsed;
                QueueResize();
            };
            send.Click += async (sender, args) => await Submit();
            questionInput.TextChanged += (sender, args) => UpdateComposer();
            questionInput.PreviewKeyDown += async (sender, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    args.Handled = true;
                    await Submit();
                }
            };
            scroll.ScrollChanged += (sender, args) =>
            {
                if (args.ExtentHeightChange == 0)
                    return;
                QueueResize();
                if (followLatest && args.ExtentHeightChange > 0 &&
                    args.VerticalOffset + args.ViewportHeight >= args.ExtentHeight - args.ExtentHeightChange - 2)
                {
                    scroll.ScrollToEnd();
                }
            };
        }

        internal void ShowReading()
        {
            if (HasConversation)
                ShowFeedback("正在读取选区…", false);
            else
                notice.Text = "正在读取选区…";
        }

        internal async Task ShowSelection(Selection selection, bool takeFocus)
        {
            selected = selection.Text;
            anchor = selection.Anchor;
            if (handle != IntPtr.Zero && !manuallyMoved)
                Native.PositionPopup(handle, View, anchor, pointer);

            notice.Text = selection.Error ?? "正在准备本机模型…";
            if (takeFocus)
            {
                View.Activate();
                dismiss.Focus();
            }
            if (!HasConversation)
                return;

            int length = Math.Min(PreviewLength, selected.Length);
            if (length < selected.Length && char.IsHighSurrogate(selected[length - 1]))
                length--;
            sourceText.Text = selected.Substring(0, length);
            if (length < selected.Length)
            {
                previewLimit.Text = "预览仅显示选区开头";
                previewLimit.Visibility = Visibility.Visible;
            }
            sourceToggle.Visibility = Visibility.Visible;
            await RunTurn(null);
        }

        internal void ShowFeedback(string message, bool isError = true)
        {
            feedback.Text = message;
            feedback.Foreground = (Brush)View.FindResource(isError ? "AlertInk" : "SubtleInk");
            feedback.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void QueueResize()
        {
            if (!closed && !resizeTimer.IsEnabled)
                resizeTimer.Start();
        }

        private void ResizeToContent()
        {
            if (closed || !View.IsLoaded)
                return;
            double content = header.ActualHeight + footer.ActualHeight + scroll.ExtentHeight +
                scroll.Padding.Top + scroll.Padding.Bottom + 3;
            double limit = Native.HeightLimit(handle, ExpandedHeight);
            double height = Math.Min(limit, Math.Max(InitialHeight, Math.Ceiling(content)));
            if (Math.Abs(height - targetHeight) < 2)
                return;
            targetHeight = height;
            double from = View.ActualHeight;
            View.BeginAnimation(FrameworkElement.HeightProperty, null);
            View.Height = height;
            if (SystemParameters.ClientAreaAnimation && Math.Abs(from - height) >= 2)
                View.BeginAnimation(FrameworkElement.HeightProperty, Ease(from, height, 0.20));
        }

        private static void FadeIn(UIElement element, double seconds)
        {
            element.Opacity = 1;
            if (SystemParameters.ClientAreaAnimation)
                element.BeginAnimation(UIElement.OpacityProperty, Ease(0.15, 1, seconds));
        }

        private static DoubleAnimation Ease(double from, double to, double seconds)
        {
            return new DoubleAnimation(from, to, new Duration(TimeSpan.FromSeconds(seconds))) {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
        }

        private async Task Submit()
        {
            if (!send.IsEnabled)
                return;
            await RunTurn(questionInput.Text.Trim());
            if (!closed && questionInput.IsEnabled && View.IsActive)
                questionInput.Focus();
        }

        private async Task RunTurn(string question)
        {
            bool followUp = question != null;
            var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            request = cancel;
            UpdateComposer();
            ShowFeedback(null);

            Ollama.Message nextQuestion = null;
            Border questionRow = null;
            if (followUp)
            {
                nextQuestion = new Ollama.Message("user", question);
                questionInput.Clear();
                questionRow = new Border { Style = (Style)View.FindResource("QuestionRow") };
                questionRow.Child = CreateMessage(question, "你的问题", "QuestionText");
                transcript.Children.Add(questionRow);
                followLatest = true;
                scroll.ScrollToEnd();
            }

            var answer = CreateMessage("", followUp ? "追问回复" : "代码解释结果", "AnswerText");
            answer.Visibility = Visibility.Collapsed;
            transcript.Children.Add(answer);

            try
            {
                var messages = Ollama.CreatePrompt(selected, nextQuestion, previousQuestion, previousAnswer);
                int promptTokens = await Ollama.Reply(messages,
                    message => { if (!closed) status.Text = message; },
                    text =>
                    {
                        if (closed)
                            return;
                        if (answer.Visibility != Visibility.Visible)
                        {
                            notice.Text = "";
                            notice.Visibility = Visibility.Collapsed;
                            answer.Visibility = Visibility.Visible;
                        }
                        answer.AppendText(text);
                    }, cancel.Token);
                if (closed)
                    return;

                if (followUp)
                {
                    previousQuestion = nextQuestion;
                    previousAnswer = new Ollama.Message("assistant", answer.Text);
                    followUpsLeft--;
                    if (followUpsLeft == 0)
                        composer.Visibility = Visibility.Collapsed;
                }
                else
                {
                    followUpsLeft = Ollama.FollowUpLimit(promptTokens, selected);
                    composer.Visibility = Visibility.Visible;
                }
                status.Text = followUpsLeft > 0 ? "最多还可追问 " + followUpsLeft + " 次" : "重新选中代码可继续";
            }
            catch (Exception error)
            {
                if (closed)
                    return;
                notice.Visibility = Visibility.Collapsed;
                if (followUp)
                {
                    transcript.Children.Remove(questionRow);
                    transcript.Children.Remove(answer);
                    questionInput.Text = question;
                }
                status.Text = followUp ? "追问未完成" : "解释未完成";
                ShowFeedback(ErrorMessage(error, followUp, cancel.IsCancellationRequested));
            }
            finally
            {
                request = null;
                cancel.Dispose();
                if (!closed)
                {
                    UpdateComposer();
                    if (!followUp && questionInput.IsEnabled && View.IsActive && dismiss.IsKeyboardFocused)
                        questionInput.Focus();
                }
            }
        }

        private TextBox CreateMessage(string text, string label, string style)
        {
            var block = new TextBox { Text = text, Style = (Style)View.FindResource(style) };
            AutomationProperties.SetName(block, label);
            return block;
        }

        private void UpdateComposer()
        {
            questionInput.IsEnabled = request == null && followUpsLeft > 0;
            send.IsEnabled = questionInput.IsEnabled && !string.IsNullOrWhiteSpace(questionInput.Text);
        }

        private static string ErrorMessage(Exception error, bool followUp, bool timedOut)
        {
            if (timedOut)
                return followUp ? "本机模型响应超时，请重新发送这个问题。" : "本机模型响应超时，请缩小选区后重试。";
            if (error is InvalidOperationException)
                return error.Message;
            if (error is HttpRequestException || error is IOException)
                return "与本机 Ollama 的连接中断，请重试。";
            return "生成回答时遇到错误，请重试。";
        }
    }
}
