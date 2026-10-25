using System.ComponentModel;
using System.Diagnostics;

internal static class GameProcessLifetime
{
    // Observe only this installation; another local test copy is a different player.
    public static async Task Watch(string directory, CancellationTokenSource lifetime)
    {
        string executable = Path.GetFullPath(Path.Combine(directory, "CelebrityPoker.exe"));
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                Process? game = null;
                foreach (var candidate in Process.GetProcessesByName("CelebrityPoker"))
                {
                    try
                    {
                        if (string.Equals(candidate.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                        { game = candidate; break; }
                    }
                    catch (Exception e) when (e is Win32Exception or InvalidOperationException) { }
                    candidate.Dispose();
                }
                if (game is not null)
                {
                    using (game) await game.WaitForExitAsync(lifetime.Token);
                    lifetime.Cancel(); return;
                }
                await Task.Delay(500, lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
}
