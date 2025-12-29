using dashboard.Views;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;

namespace dashboard;

public static class Program
{
    public static void Main()
    {
        // Configure application theme
        ConfigurationManager.RuntimeConfig = """{ "Theme": "Amber Phosphor" }""";
        ConfigurationManager.Enable(ConfigLocations.All);

        Application.Init();

        var dashboardWindow = new Dashboard();
        dashboardWindow.LogViewer.AddMessage("Application started");
        dashboardWindow.LogViewer.AddMessage("Processing data...");

        Application.Run(dashboardWindow);

        Application.Shutdown();
    }
}
