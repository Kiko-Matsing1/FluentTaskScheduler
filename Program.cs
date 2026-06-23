using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace FluentTaskScheduler
{
    public static class Program
    {
        [DllImport("Microsoft.ui.xaml.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern void XamlCheckProcessRequirements();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr AddDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);

        [STAThread]
        static void Main(string[] args)
        {
            // Set the DLL search path to include the application directory.
            // This is critical for ARM64 and self-contained builds where native DLLs
            // are loaded locally from the application folder.
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            AddDllDirectory(appDir);
            SetDefaultDllDirectories(0x00001000); // LOAD_LIBRARY_SEARCH_DEFAULT_DIRS

            // Initialize ComWrappers as early as possible for WinRT support.
            // This MUST be done before any WinRT types are accessed.
            WinRT.ComWrappersSupport.InitializeComWrappers();

            // VeloPack: Handle install/uninstall/update hooks before anything else.
            // In a machine-wide install (C:\Program Files), non-admin users don't have write access,
            // which causes Velopack to crash with UnauthorizedAccessException when it tries to 
            // manage the 'packages' directory. We skip Velopack for non-admins in protected folders.
            if (HasWriteAccessToAppDir())
            {
                try
                {
                    VelopackApp.Build().Run();
                }
                catch (Exception)
                {
                    // Catch-all for any other Velopack initialization issues
                }
            }

            try
            {
                XamlCheckProcessRequirements();
            }
            catch (Exception ex)
            {
                // Swapped to Console to prevent Sonar S108 empty-block warnings during Release builds
                Console.WriteLine($"XamlCheckProcessRequirements failed: {ex.Message}");
            }

            // Framework Bootstrapper omitted: The project runs in Self-Contained mode,
            // meaning native WinAppSDK 2.2 runtime files are loaded directly via local DLL directory hooks.
            Application.Start((p) =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }

        private static bool HasWriteAccessToAppDir()
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string testPath = System.IO.Path.Combine(appDir, ".velopack_write_test");
                System.IO.File.WriteAllText(testPath, "test");
                System.IO.File.Delete(testPath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}