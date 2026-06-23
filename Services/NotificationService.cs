using System;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;

namespace FluentTaskScheduler.Services
{
    public static class NotificationService
    {
        /// <summary>
        /// Dispatches native OS toast notifications safely without requiring registry-altering COM setups.
        /// </summary>
        private static void TryShowNativeToast(string title, string body)
        {
            try
            {
                // Construct a native ToastGeneric XML schema payload
                string xml = $"<toast><visual><binding template='ToastGeneric'>" +
                             $"<text>{System.Security.SecurityElement.Escape(title)}</text>" +
                             $"<text>{System.Security.SecurityElement.Escape(body)}</text>" +
                             $"</binding></visual></toast>";

                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(xml);

                var toast = new ToastNotification(xmlDoc);

                // Wire an inline process activation listener to safely capture clicks while alive
                toast.Activated += (sender, args) =>
                {
                    App.RestoreMainWindow();
                };

                // Deliver directly to the built-in Windows Notification Action Center
                ToastNotificationManager.CreateToastNotifier("FluentTaskScheduler").Show(toast);
            }
            catch (Exception ex)
            {
                LogService.Error($"Native toast notification subsystem failed: {ex.Message}");
            }
        }

        public static void ShowTaskStarted(string taskName)
        {
            if (!SettingsService.ShowNotifications) return;
            TryShowNativeToast($"Task Started: {taskName}", "The task has been triggered manually.");
        }

        public static void ShowTaskError(string taskName, string error)
        {
            if (!SettingsService.ShowNotifications) return;
            TryShowNativeToast($"Task Failed: {taskName}", error);
        }

        public static void ShowUpcomingTask(string taskName, int minutesUntilRun)
        {
            if (!SettingsService.ShowNotifications || !SettingsService.EnableUpcomingReminders) return;

            string timeLabel = minutesUntilRun <= 1 ? "less than a minute" : $"{minutesUntilRun} minutes";
            TryShowNativeToast($"Upcoming Task: {taskName}", $"Scheduled to run in {timeLabel}.");
        }

        private static bool _trayNotificationShown = false;

        public static void ShowMinimizedToTray()
        {
            if (_trayNotificationShown) return;
            _trayNotificationShown = true;

            TryShowNativeToast(
                "FluentTaskScheduler is still running",
                "The app has been minimized to the system tray. Click to restore, or double-click the tray icon."
            );
        }
    }
}