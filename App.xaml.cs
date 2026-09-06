using System;
using System.Threading.Tasks;
using System.Windows;

namespace AuralDesk;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var settings = AppSettings.Load();
        Lang.Apply(Lang.Resolve(settings), this);
        base.OnStartup(e);
    }

    public App()
    {
        // 未处理异常一律写入日志，方便在别的电脑上排查
        DispatcherUnhandledException += (_, e) =>
            AppLog.Write("未处理界面异常: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Write("未处理进程异常: " + (e.ExceptionObject as Exception)?.ToString());
        TaskScheduler.UnobservedTaskException += (_, e) =>
            AppLog.Write("未观察任务异常: " + e.Exception);
    }
}
