namespace ChessCoach;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.ThreadException+=(_,e)=>MessageBox.Show(e.Exception.Message,"棋伴",MessageBoxButtons.OK,MessageBoxIcon.Error);
        Application.Run(new MainForm(args.Contains("--demo")));
    }
}
