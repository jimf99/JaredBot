using Terminal.Gui.App;
using Terminal.Gui.Views;
// To see this output on the screen it must be done after shutdown,
// which restores the previous screen.
//Console.WriteLine($@"Username: {ExampleWindow.UserName}");

// Defines a top-level window with border and title
public sealed class Dashboard : Window
{
    public static string UserName { get; set; }
    public MenuBarv2 MenuBarV2 { get; set; }

    public Dashboard()
    {
        Title = $"Dashboard App ({Application.QuitKey} to quit)";

        this.MenuBarV2 = new MenuBarv2(new MenuBarItemv2[]
        {
            new MenuBarItemv2("_File", new MenuItemv2[]
            {
                new MenuItemv2("_New", "Create a new file", () => {}),
                new MenuItemv2("_Open", "Open a file", () => {}),
                new MenuItemv2("_Save", "Save the file", () => {}),
                new MenuItemv2("_Quit", "Quit the application", () => Application.RequestStop())
            }),
            new MenuBarItemv2("_Edit", new MenuItemv2[]
            {
                new MenuItemv2("_Cut", "Cut selection", () => {}),
                new MenuItemv2("_Copy", "Copy selection", () => {}),
                new MenuItemv2("_Paste", "Paste clipboard", () => {})
            }),
            new MenuBarItemv2("_Help", new MenuItemv2[]
            {
                new MenuItemv2("_About", "About this app", () =>
                {
                    MessageBox.Query("About", "Terminal.Gui v2 Dashboard Example", "OK");
                })
            })
        });

        //// Create input components and labels
        //var usernameLabel = new Label { Text = "Username:" };

        //var userNameText = new TextField
        //{
        //    // Position text field adjacent to the label
        //    X = Pos.Right(usernameLabel) + 1,

        //    // Fill remaining horizontal space
        //    Width = Dim.Fill()
        //};

        //var passwordLabel = new Label
        //{
        //    Text = "Password:",
        //    X = Pos.Left(usernameLabel),
        //    Y = Pos.Bottom(usernameLabel) + 1
        //};

        //var passwordText = new TextField
        //{
        //    Secret = true,

        //    // align with the text box above
        //    X = Pos.Left(userNameText),
        //    Y = Pos.Top(passwordLabel),
        //    Width = Dim.Fill()
        //};

        //// Create login button
        //var btnLogin = new Button
        //{
        //    Text = "Login",
        //    Y = Pos.Bottom(passwordLabel) + 1,

        //    // center the login button horizontally
        //    X = Pos.Center(),
        //    IsDefault = true
        //};

        //// When login button is clicked display a message popup
        //btnLogin.Accepting += (s, e) =>
        //{
        //    if (userNameText.Text == "admin" && passwordText.Text == "password")
        //    {
        //        MessageBox.Query(Application.Top.Title, "Logging In", "Login Successful", "Ok");
        //        UserName = userNameText.Text;
        //        Application.RequestStop();
        //    }
        //    else
        //    {
        //        MessageBox.ErrorQuery(Application.Top.Title, "Logging In", "Incorrect username or password", "Ok");
        //    }

        //    // When Accepting is handled, set e.Handled to true to prevent further processing.
        //    e.Handled = true;
        //};

        //// Add the views to the Window
        //Add(usernameLabel, userNameText, passwordLabel, passwordText, btnLogin);

        Add(this.MenuBarV2);
    }
}
