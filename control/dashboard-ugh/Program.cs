// Instance-based pattern (recommended)
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

IApplication app = Application.Create().Init();
Window window = new() { Title = "My App" };
app.Run(window);
window.Dispose();
app.Dispose();

// With using statement for automatic disposal
using (IApplication app = Application.Create().Init())
{
    Window window = new() { Title = "My App" };
    app.Run(window);
    window.Dispose();
} // app.Dispose() called automatically

// Access from within a view
public class MyView : View
{
    public void DoWork()
    {
        App?.Driver.Move(0, 0);
        App?.TopRunnableView?.SetNeedsDraw();
    }
}