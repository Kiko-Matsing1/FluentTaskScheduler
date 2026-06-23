#pragma warning disable S2696 

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Text;
using System.Linq;
using SS = global::FluentTaskScheduler.Services.SettingsService;

namespace FluentTaskScheduler
{
    public partial class App : Application
    {
        private sealed class WindowRecord
        {
            public string Name { get; }
            public Window Win { get; }
            public bool IsHidden { get; set; }
            public WindowRecord(string name, Window win) { Name = name; Win = win; }
        }

        private static readonly List<WindowRecord> _windows = new();
        private static int _windowCounter = 0;
        private static System.Threading.Mutex? _instanceMutex;
        private static System.Threading.EventWaitHandle? _showInstanceEvent;

        public static Window? m_window => _windows.Count > 0 ? _windows[0].Win : null;
        public static Window? MainWindow => m_window;

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr hInst, IntPtr lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint IMAGE_ICON = 1;
        private const uint LR_DEFAULTSIZE = 0x00000040;
        private const uint LR_SHARED = 0x00008000;
        private const uint WM_SETICON = 0x0080;
        private static readonly IntPtr ICON_SMALL = IntPtr.Zero;
        private static readonly IntPtr ICON_BIG = new IntPtr(1);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        public App()
        {
            Services.LocalizationService.Initialize();
            Services.LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;

            this.InitializeComponent();

#pragma warning disable CS8622
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
#pragma warning restore CS8622
            this.UnhandledException += App_UnhandledException;
        }

        private static void LocalizationService_LanguageChanged(object? sender, EventArgs e)
        {
            foreach (var rec in _windows)
            {
                try
                {
                    rec.Win.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (rec.Win.Content is Frame rootFrame)
                        {
                            if (rootFrame.Content is MainPage mainPage)
                                mainPage.RefreshLocalizedUi();
                            else
                                rootFrame.Navigate(typeof(MainPage));
                        }
                        rec.Win.Title = GetWindowTitle(rec.Name);
                    });
                }
                catch
                {
                    // Intentionally swallowed fallback
                }
            }
        }

        private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            LogCrash(e.ExceptionObject as Exception, "AppDomain.UnhandledException");
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            LogCrash(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            LogCrash(e.Exception, "Xaml.UnhandledException");
            e.Handled = true;
        }

        public void LogCrash(Exception? ex, string source)
        {
            string errorMessage = $"[{DateTime.Now}] [{source}] Error: {ex?.Message}\r\nStack Trace: {ex?.StackTrace ?? "No stack"}\r\n\r\n";
            Services.LogService.WriteCrash(ex, source);

            if (m_window != null)
            {
                m_window.DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        var reb = new RichEditBox
                        {
                            IsReadOnly = true,
                            AcceptsReturn = true,
                            Width = 500,
                            Height = 300,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            Margin = new Thickness(0, 10, 0, 10),
                            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas")
                        };
                        reb.Document.SetText(TextSetOptions.None, errorMessage);

                        var dialog = new ContentDialog
                        {
                            Title = Services.LocalizationService.GetString("Dialog.Crash.Title", "Unhandled Exception"),
                            Content = reb,
                            PrimaryButtonText = Services.LocalizationService.GetString("Dialog.Crash.Copy", "Copy to Clipboard"),
                            CloseButtonText = Services.LocalizationService.GetString("Dialog.Common.Close", "Close"),
                            XamlRoot = m_window.Content?.XamlRoot,
                            RequestedTheme = SS.Theme
                        };

                        dialog.PrimaryButtonClick += (s, args) =>
                        {
                            var dataPackage = new DataPackage();
                            dataPackage.SetText(errorMessage);
                            Clipboard.SetContent(dataPackage);
                        };

                        await dialog.ShowAsync();
                    }
                    catch
                    {
                        // Explicitly block dialogue rendering exceptions from loop crashing
                    }
                });
            }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var cmdArgs = Environment.GetCommandLineArgs();

            if (cmdArgs.Length > 1)
            {
                HandleCommandLineMode(cmdArgs);
                return;
            }

            InitializeGuiMode();
        }

        private static void HandleCommandLineMode(string[] args)
        {
            AttachConsole(ATTACH_PARENT_PROCESS);

            string command = args[1].ToLowerInvariant();
            string? param = args.Length > 2 ? args[2] : null;

            var service = new global::FluentTaskScheduler.Services.TaskServiceWrapper();

            try
            {
                switch (command)
                {
                    case "--list":
                        ExecuteListCommand(service);
                        break;
                    case "--run":
                        if (!string.IsNullOrEmpty(param)) ExecuteRunCommand(service, param);
                        break;
                    case "--enable":
                        if (!string.IsNullOrEmpty(param)) ExecuteEnableCommand(service, param);
                        break;
                    case "--disable":
                        if (!string.IsNullOrEmpty(param)) ExecuteDisableCommand(service, param);
                        break;
                    case "--export-history":
                        if (!string.IsNullOrEmpty(param)) ExecuteExportHistoryCommand(service, param, args);
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.Out.Flush();
                Environment.Exit(1);
                return;
            }

            Console.Out.Flush();
            Environment.Exit(0);
        }

        private static void ExecuteListCommand(global::FluentTaskScheduler.Services.TaskServiceWrapper service)
        {
            var tasks = service.GetAllTasks();
            var simpleList = new System.Collections.Generic.List<object>();
            foreach (var t in tasks)
            {
                simpleList.Add(new
                {
                    Name = t.Name,
                    Path = t.Path,
                    State = t.State,
                    LastRun = t.LastRunTime,
                    NextRun = t.NextRunTime
                });
            }
            string json = System.Text.Json.JsonSerializer.Serialize(simpleList, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
        }

        private static void ExecuteRunCommand(global::FluentTaskScheduler.Services.TaskServiceWrapper service, string param)
        {
            Console.WriteLine($"Running task: {param}");
            service.RunTask(param);
            Console.WriteLine("Task started.");
        }

        private static void ExecuteEnableCommand(global::FluentTaskScheduler.Services.TaskServiceWrapper service, string param)
        {
            Console.WriteLine($"Enabling task: {param}");
            service.EnableTask(param);
            Console.WriteLine("Task enabled.");
        }

        private static void ExecuteDisableCommand(global::FluentTaskScheduler.Services.TaskServiceWrapper service, string param)
        {
            Console.WriteLine($"Disabling task: {param}");
            service.DisableTask(param);
            Console.WriteLine("Task disabled.");
        }

        private static void ExecuteExportHistoryCommand(global::FluentTaskScheduler.Services.TaskServiceWrapper service, string param, string[] args)
        {
            string output = args.Length > 4 && args[3] == "--output" ? args[4] : "history.csv";
            Console.WriteLine($"Exporting history for {param} to {output}...");

            var history = service.GetTaskHistory(param);
            if (history != null && history.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Time,EventId,Result,User,ExitCode,Message");
                foreach (var h in history)
                {
                    sb.AppendLine($"\"{SanitizeCsvCell(h.Time.ToString())}\",{h.EventId},\"{SanitizeCsvCell(h.Result)}\",\"{SanitizeCsvCell(h.User)}\",{h.ExitCode},\"{SanitizeCsvCell(h.Message).Replace("\"", "\"\"")}\"");
                }
                System.IO.File.WriteAllText(output, sb.ToString());
                Console.WriteLine("Export complete.");
            }
            else
            {
                Console.WriteLine("No history found or task does not exist.");
            }
        }

        private static string SanitizeCsvCell(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            char firstChar = value[0];
            if (firstChar == '=' || firstChar == '+' || firstChar == '-' || firstChar == '@')
            {
                return "'" + value;
            }
            return value;
        }

        private void InitializeGuiMode()
        {
            _instanceMutex = new System.Threading.Mutex(true, "FluentTaskScheduler_Instance", out bool isFirstInstance);
            if (!isFirstInstance)
            {
                try
                {
                    var ev = System.Threading.EventWaitHandle.OpenExisting("FluentTaskScheduler_Show");
                    ev.Set();
                }
                catch
                {
                    // Fallback handles this context safely
                }
                Environment.Exit(0);
                return;
            }

            _showInstanceEvent = new System.Threading.EventWaitHandle(
                false, System.Threading.EventResetMode.AutoReset, "FluentTaskScheduler_Show");
            System.Threading.Tasks.Task.Run(() =>
            {
                while (true)
                {
                    _showInstanceEvent.WaitOne();
                    var win = _windows.Count > 0 ? _windows[0].Win : null;
                    win?.DispatcherQueue.TryEnqueue(() => { win.AppWindow.Show(); win.Activate(); });
                }
            });

            CreateAndRegisterWindow();

            var trayHwnd = WinRT.Interop.WindowNative.GetWindowHandle(_windows[0].Win);
            Services.TrayIconService.Initialize(trayHwnd);

            Services.TrayIconService.GetHiddenWindows = () =>
            {
                var list = new List<(string, Action, Action)>();
                foreach (var rec in _windows)
                {
                    if (!rec.IsHidden) continue;
                    var r = rec;
                    list.Add((
                        r.Name,
                        () => r.Win.DispatcherQueue.TryEnqueue(() => { r.Win.AppWindow.Show(); r.Win.Activate(); r.IsHidden = false; }),
                        () => r.Win.DispatcherQueue.TryEnqueue(() => { r.IsHidden = false; r.Win.Close(); })
                    ));
                }
                return list;
            };

            Services.TrayIconService.NewWindowRequested += () =>
                _windows[0].Win.DispatcherQueue.TryEnqueue(CreateAndRegisterWindow);

            Services.TrayIconService.ExitRequested += () => Environment.Exit(0);
            Services.TrayIconService.UpdateVisibility();

            Services.LogService.Info("Application started");
            Services.ReminderService.Start();

            _ = CheckForVeloPackUpdateAsync();

            _windows[0].Win.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                ApplySmoothScrolling(SS.SmoothScrolling);
            });

            GC.KeepAlive(_instanceMutex);
        }

        /// <summary>
        /// Global, cross-thread restoration entry point requested by CodeRabbit review logs.
        /// </summary>
        public static void RestoreMainWindow()
        {
            var dispatcherQueue = m_window?.DispatcherQueue;
            dispatcherQueue?.TryEnqueue(() =>
            {
                var win = _windows.FindLast(r => r.IsHidden)?.Win ?? m_window;
                if (win != null)
                {
                    win.AppWindow.Show();
                    win.Activate();
                }
            });
        }

        private void CreateAndRegisterWindow()
        {
            _windowCounter++;
            string name = _windowCounter == 1 ? "Window 1" : $"Window {_windowCounter}";

            var win = new Window();
            win.Title = GetWindowTitle(name);

            var rec = new WindowRecord(name, win);
            _windows.Add(rec);

            ConfigureWindowIcon(win);
            ConfigureWindowSizeAndSizingEvents(win, rec);

            Frame rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;
            win.Content = rootFrame;

            win.ExtendsContentIntoTitleBar = true;

            ApplyThemeToWindow(win);
            rootFrame.Navigate(typeof(MainPage));

            ConfigureWindowClosing(win, rec);

            win.Activate();
        }

        private static void ConfigureWindowIcon(Window win)
        {
            try
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "AppIcon.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    win.AppWindow.SetIcon(iconPath);
                }
                else
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(win);
                    if (hwnd != IntPtr.Zero)
                    {
                        IntPtr hModule = GetModuleHandle(null);
                        IntPtr hIcon = LoadImage(hModule, new IntPtr(32512), IMAGE_ICON, 0, 0, LR_DEFAULTSIZE | LR_SHARED);
                        if (hIcon != IntPtr.Zero) { SendMessage(hwnd, WM_SETICON, ICON_SMALL, hIcon); SendMessage(hwnd, WM_SETICON, ICON_BIG, hIcon); }
                    }
                }
            }
            catch
            {
                // Fallback handles missing icons safely
            }
        }

        private void ConfigureWindowSizeAndSizingEvents(Window win, WindowRecord rec)
        {
            int offset = (_windowCounter - 1) * 30;
            win.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = SS.WindowWidth + offset, Height = SS.WindowHeight + offset });

            if (_windowCounter == 1)
            {
                win.AppWindow.Changed += (s, e) =>
                {
                    if (e.DidSizeChange && !rec.IsHidden)
                    {
                        SS.WindowWidth = s.Size.Width;
                        SS.WindowHeight = s.Size.Height;
                    }
                };
            }
        }

        private static void ConfigureWindowClosing(Window win, WindowRecord rec)
        {
            win.AppWindow.Closing += (sender, args) =>
            {
                if (SS.EnableTrayIcon)
                {
                    args.Cancel = true;
                    rec.IsHidden = true;
                    sender.Hide();
                    Services.NotificationService.ShowMinimizedToTray();
                }
                else
                {
                    _windows.Remove(rec);
                }
            };
        }

        private static string GetWindowTitle(string windowName)
        {
            string appTitle = Services.LocalizationService.GetString("App.WindowTitle", "FluentTaskScheduler");
            if (string.Equals(windowName, "Window 1", StringComparison.OrdinalIgnoreCase))
                return appTitle;

            return $"{appTitle} — {windowName}";
        }

        private void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            // Extension callback interface hook setup
        }

        public static void ApplySmoothScrolling(bool enable)
        {
            var scrollViewers = _windows
                .Select(rec => rec.Win)
                .Where(win => win?.Content != null)
                .SelectMany(win => FindDescendants<ScrollViewer>(win.Content));

            foreach (var sv in scrollViewers)
            {
                sv.IsScrollInertiaEnabled = enable;
            }
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                    yield return match;
                foreach (var descendant in FindDescendants<T>(child))
                    yield return descendant;
            }
        }

        private Microsoft.UI.Xaml.Media.SystemBackdrop? _backdrop;

        private void ApplyThemeToWindow(Window win)
        {
            if (win?.Content is Control root)
            {
                root.RequestedTheme = SS.Theme;
                win.SystemBackdrop = null;

                Application.Current.Resources["TaskCardBackground"] = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
                Application.Current.Resources["TaskCardBorder"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

                if (SS.IsOledMode && SS.Theme == ElementTheme.Dark)
                {
                    var black = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black);
                    root.Background = black;
                    SetNavigationViewBackgrounds(black);
                }
                else if (SS.IsMicaEnabled && Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
                {
                    var transparent = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    root.Background = transparent;
                    if (_backdrop == null) _backdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
                    win.SystemBackdrop = _backdrop;
                    SetNavigationViewBackgrounds(transparent);
                }
                else
                {
                    _backdrop = null;
                    bool isDark = root.ActualTheme == ElementTheme.Dark;
                    var bg = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        isDark ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
                               : Windows.UI.Color.FromArgb(255, 243, 243, 243));
                    root.Background = bg;
                    SetNavigationViewBackgrounds(bg);
                }

                UpdateTitleBarTheme(win, root.ActualTheme);
            }
        }

        private static void UpdateTitleBarTheme(Window win, ElementTheme theme)
        {
            var appWindow = win.AppWindow;
            if (appWindow == null) return;

            if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = appWindow.TitleBar;
                bool isDark = theme == ElementTheme.Dark;

                if (isDark)
                {
                    titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                    titleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;
                    titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(25, 255, 255, 255);
                    titleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
                    titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(40, 255, 255, 255);
                    titleBar.ButtonPressedForegroundColor = Microsoft.UI.Colors.White;
                    titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                    titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(100, 255, 255, 255);
                }
                else
                {
                    titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                    titleBar.ButtonForegroundColor = Microsoft.UI.Colors.Black;
                    titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(25, 0, 0, 0);
                    titleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.Black;
                    titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(40, 0, 0, 0);
                    titleBar.ButtonPressedForegroundColor = Microsoft.UI.Colors.Black;
                    titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                    titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(100, 0, 0, 0);
                }
            }
        }

        private static void SetNavigationViewBackgrounds(Microsoft.UI.Xaml.Media.Brush brush)
        {
            Application.Current.Resources["NavigationViewContentBackground"] = brush;
            Application.Current.Resources["NavigationViewExpandedPaneBackground"] = brush;
            Application.Current.Resources["NavigationViewTopPaneBackground"] = brush;
        }

        public void ApplyTheme(ElementTheme theme)
        {
            foreach (var rec in _windows)
                ApplyThemeToWindow(rec.Win);
        }

        private async System.Threading.Tasks.Task CheckForVeloPackUpdateAsync()
        {
            try
            {
                var result = await Services.VeloPackUpdateService.CheckAndDownloadAsync();
                if (result.Status != Services.VeloPackUpdateService.UpdateResultStatus.UpdateReady || result.Info == null || m_window == null) return;

                m_window.DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        var dialog = new ContentDialog
                        {
                            Title = Services.LocalizationService.GetString("Dialog.UpdateAvailable.Title", "Update Available"),
                            Content = string.Format(
                                Services.LocalizationService.GetString(
                                    "Dialog.UpdateAvailable.ContentFormat",
                                    "Version {0} has been downloaded and is ready to install.\nRestart now to apply the update?"),
                                result.NewVersion),
                            PrimaryButtonText = Services.LocalizationService.GetString("Dialog.UpdateAvailable.RestartNow", "Restart Now"),
                            CloseButtonText = Services.LocalizationService.GetString("Dialog.Common.Later", "Later"),
                            XamlRoot = m_window.Content?.XamlRoot,
                            RequestedTheme = SS.Theme
                        };

                        var dialogResult = await dialog.ShowAsync();
                        if (dialogResult == ContentDialogResult.Primary)
                        {
                            Services.VeloPackUpdateService.ApplyAndRestart(result.Info);
                        }
                    }
                    catch (Exception ex)
                    {
                        Services.LogService.Info($"[AutoUpdate] Could not show update dialog: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Services.LogService.Info($"[AutoUpdate] Background update check failed: {ex.Message}");
            }
        }

        private static void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new InvalidOperationException("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}