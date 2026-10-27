using System;
using System.Threading;
using System.Windows;

namespace AirTools
{
    public partial class App : Application
    {
        private static Mutex? _mutex;
        private bool _ownsMutex;
        private Services.AppHost? _host;
        public void ShowLauncherSettings() => _host?.ShowSettings();

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            string mutexName = "Local\\AirTools_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Services.LauncherSettings.DataDirectory.ToUpperInvariant())));
            _mutex = new Mutex(true, mutexName, out bool isNew);
            _ownsMutex = isNew;

            if (!isNew)
            {
                MessageBox.Show("Air Tools 已在运行中。\n\n请查看系统托盘图标。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
            Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
            _host = new Services.AppHost();
            if (Array.IndexOf(e.Args, "--search") >= 0) Dispatcher.BeginInvoke(_host.ShowSearch, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (Array.IndexOf(e.Args, "--settings") >= 0) Dispatcher.BeginInvoke(_host.ShowSettings, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _host?.Dispose();
            if (_ownsMutex) _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
