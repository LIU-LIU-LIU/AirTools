using System;
using System.Threading;
using System.Windows;

namespace AirTools
{
    public partial class App : Application
    {
        private static Mutex? _mutex;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            const string mutexName = "Global\\AirTools_SingleInstance";
            _mutex = new Mutex(true, mutexName, out bool isNew);

            if (!isNew)
            {
                MessageBox.Show("Air Tools 已在运行中。\n\n请查看系统托盘图标。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            var main = new MainWindow();
            main.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
