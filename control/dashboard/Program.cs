using Terminal.Gui.App;
using Terminal.Gui.Configuration;
using Terminal.Gui.ViewBase;

// Override the default configuration for the application to use the Amber Phosphor theme
ConfigurationManager.RuntimeConfig = """{ "Theme": "Amber Phosphor" }""";
ConfigurationManager.Enable(ConfigLocations.All);

Application.Init();

Application.Run<Dashboard>();

// Dispose the app to clean up and enable Console.WriteLine below
Application.Shutdown();
