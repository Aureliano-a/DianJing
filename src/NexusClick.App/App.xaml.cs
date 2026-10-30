using System.Windows;
namespace NexusClick.App;
public partial class App : Application
{
    public void StartMainWindow(object sender, StartupEventArgs e)
    {
        var window = new MainWindow(); MainWindow = window; window.Show();
    }
}
