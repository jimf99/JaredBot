//using Terminal.Gui;
//using Terminal.Gui.ViewBase;
//using Terminal.Gui.Views;

//namespace dashboad;

//public class MenuBarView : View
//{
//    public MenuBar Menu { get; }

//    public MenuBarView()
//    {
//        // Use the new layout system
//        LayoutStyle = LayoutStyle.Computed;

//        // A MenuBar is always 1 row tall
//        Height = 1;
//        Width = Dim.Fill();

//        Menu = new MenuBar(new MenuBarItem[]
//        {
//            new MenuBarItem("_File", new MenuItem[]
//            {
//                new MenuItem("_New", "Create a new file", () => {}),
//                new MenuItem("_Open", "Open a file", () => {}),
//                new MenuItem("_Save", "Save the file", () => {}),
//                new MenuItem("_Quit", "Quit the application", () => Application.RequestStop())
//            }),

//            new MenuBarItem("_Edit", new MenuItem[]
//            {
//                new MenuItem("_Cut", "Cut selection", () => {}),
//                new MenuItem("_Copy", "Copy selection", () => {}),
//                new MenuItem("_Paste", "Paste clipboard", () => {})
//            }),

//            new MenuBarItem("_Help", new MenuItem[]
//            {
//                new MenuItem("_About", "About this app", () =>
//                {
//                    MessageBox.Query("About", "Terminal.Gui v2 MenuBarView Example", "OK");
//                })
//            })
//        });

//        Add(Menu);
//    }
//}
