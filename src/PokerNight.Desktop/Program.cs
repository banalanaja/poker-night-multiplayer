namespace PokerNight.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Application.OpenForms.OfType<MainForm>().FirstOrDefault()?.RecordUiFailure(e.Exception);
        Application.Run(new MainForm());
    }
}
