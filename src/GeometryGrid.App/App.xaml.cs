using System.Configuration;
using System.Data;
using System.Windows;

namespace GeometryGrid.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    public App()
    {
        DispatcherUnhandledException += (_, args) =>
            CrashLogger.TryWrite(args.Exception, "WPF DispatcherUnhandledException", terminating: true);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception
                            ?? new Exception($"Non-Exception fatal object: {args.ExceptionObject}");
            CrashLogger.TryWrite(exception, "AppDomain.UnhandledException", args.IsTerminating);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
            CrashLogger.TryWrite(args.Exception, "TaskScheduler.UnobservedTaskException", terminating: false);
    }
}

