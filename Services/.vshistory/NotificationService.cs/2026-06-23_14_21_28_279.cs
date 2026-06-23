using System;
using Microsoft.Windows.AppNotifications;          // Swapped namespace
using Microsoft.Windows.AppNotifications.Builder;  // Added for AppNotificationBuilder

namespace FluentTaskScheduler.Services
{
    public static class NotificationService
    {
        public static void ShowTaskStarted(string taskName)
        {
            if (!SettingsService.ShowNotifications) return;

            var notification = new AppNotificationBuilder()
                .AddText($"Task Started: {taskName}")
                .AddText("The task has been triggered manually.")
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }

        public static void ShowTaskError(string taskName, string error)
        {
            if (!SettingsService.ShowNotifications) return;

            var notification = new AppNotificationBuilder()
                .AddText($"Task Failed: {taskName}")
                .AddText(error)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }

        public static void ShowUpcomingTask(string taskName, int minutesUntilRun)
        {
            if (!SettingsService.ShowNotifications || !SettingsService.EnableUpcomingReminders) return;

            string timeLabel = minutesUntilRun <= 1 ? "less than a minute" : $"{minutesUntilRun} minutes";

            var notification = new AppNotificationBuilder()
                .AddArgument("action", "show")
                .AddText($"Upcoming Task: {taskName}")
                .AddText($"Scheduled to run in {timeLabel}.")
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }

        private static bool _trayNotificationShown = false;

        public static void ShowMinimizedToTray()
        {
            if (_trayNotificationShown) return;
            _trayNotificationShown = true;

            var notification = new AppNotificationBuilder()
                .AddArgument("action", "show")
                .AddText("FluentTaskScheduler is still running")
                .AddText("The app has been minimized to the system tray. Click to restore, or double-click the tray icon.")
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }
    }
}