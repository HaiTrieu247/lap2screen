using System.Threading;
using System.Windows;
using MacExtend.Themes;

namespace MacExtend
{
    public partial class App : Application
    {
        private Mutex _singleInstance;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _singleInstance = new Mutex(true, @"Local\MacExtend.SingleInstance", out var first);
            if (!first)
            {
                Shutdown();
                return;
            }
            AppPaths.EnsureDirectories();
            ThemeManager.Initialize(this);
        }
    }
}
