using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace CareerCompanion.App;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TouchlineCareerCompanion", "crash.log");

    public App()
    {
        // A background save scan, a watcher callback, or a stray binding failure must never take the
        // whole career companion down. Everything is logged and, where possible, shown once and survived.
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Record(e.ExceptionObject as Exception, "domain");
        TaskScheduler.UnobservedTaskException += (_, e) => { Record(e.Exception, "task"); e.SetObserved(); };
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Record(e.Exception, "ui");
        e.Handled = true;
        MessageBox.Show($"Touchline hit an unexpected problem and recovered.\n\n{e.Exception.Message}",
            "Touchline", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static void Record(Exception? exception, string source)
    {
        if (exception is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:O} [{source}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
