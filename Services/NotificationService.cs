using System;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace FluentTaskScheduler.Services
{
    public static class NotificationService
    {
        // Fixed CodeRabbit Finding: Wraps window shell notification dispatches inside a safe try-catch wrapper
        private static void TryShow(AppNotification notification)
        {
            try
            {
                AppNotificationManager.Default.Show(notification);
            }
            catch (Exception ex)
            {
                LogService.Error($"Notification dispatch failed: {ex.Message}");
            }
        }

        public static void ShowTaskStarted(string taskName)
        {
            if (!SettingsService.ShowNotifications) return;

            var notification = new AppNotificationBuilder()
                .AddText($"Task Started: {taskName}")
                .AddText("The task has been triggered manually.")
                .BuildNotification();

            TryShow(notification);
        }

        public static void ShowTaskError(string taskName, string error)
        {
            if (!SettingsService.ShowNotifications) return;

            var notification = new AppNotificationBuilder()
                .AddText($"Task Failed: {taskName}")
                .AddText(error)
                .BuildNotification();

            TryShow(notification);
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

            TryShow(notification);
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

            TryShow(notification);
        }
    }
}