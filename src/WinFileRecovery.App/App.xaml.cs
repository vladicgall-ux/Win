using System.IO;
using System.Windows;
using WinFileRecovery.Core.Logging;

namespace WinFileRecovery.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinFileRecovery", "logs", "operations.log");
        OperationLogger.Configure(logPath);
    }
}
