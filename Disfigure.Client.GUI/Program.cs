#region

using Qml.Net;
using Qml.Net.Runtimes;

#endregion

namespace Disfigure.Client.GUI
{
    public class Program
    {
        private static void Main(string[] args)
        {
            RuntimeManager.DiscoverOrDownloadSuitableQtRuntime();

            QQuickStyle.SetStyle("Material");

            using QGuiApplication? application = new QGuiApplication(args);
            using QQmlApplicationEngine? qmlEngine = new QQmlApplicationEngine();


            qmlEngine.Load("Main.qml");

            application.Exec();
        }
    }
}
