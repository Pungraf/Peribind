using System.Windows.Forms;

namespace PeribindLauncher;

internal static class Program
{
    [STAThread]
    private static async Task Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        LauncherEngine engine;
        try
        {
            engine = await LauncherEngine.CreateAsync(AppContext.BaseDirectory);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Peribind Launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        Application.Run(new LauncherForm(engine));
    }
}
