using dashboard.Views;

namespace dashboard.Extensions;

public static class DashboardExtensions
{
    public static void AppendLog(this Dashboard dashboard, string message)
    {
        dashboard.LogViewer.AddMessage(message);
    }
}
