using dashboard;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;

// Override the default configuration for the application to use the Amber Phosphor theme
ConfigurationManager.RuntimeConfig = """{ "Theme": "Amber Phosphor" }""";
ConfigurationManager.Enable(ConfigLocations.All);

Application.Init();

var dashboardWindow = new Dashboard();
dashboardWindow.AppendLog("Application started");
dashboardWindow.AppendLog("Processing data...");

Application.Run(dashboardWindow);

Application.Shutdown();
