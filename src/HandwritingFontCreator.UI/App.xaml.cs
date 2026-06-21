using System.Windows;
using System.Windows.Threading;

namespace HandwritingFontCreator.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string logPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
            "crash.log");

        DispatcherUnhandledException += (_, ex) =>
        {
            System.IO.File.WriteAllText(logPath, "DISPATCHER:\n" + ex.Exception);
            MessageBox.Show(ex.Exception.ToString(), "Unhandled exception");
            ex.Handled = true;
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            System.IO.File.AppendAllText(logPath, "TASK:\n" + ex.Exception);
            ex.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            System.IO.File.AppendAllText(logPath, "DOMAIN:\n" + ex.ExceptionObject);
        };
    }
}
