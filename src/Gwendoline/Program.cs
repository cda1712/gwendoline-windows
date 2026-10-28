using System.Diagnostics;
using Gwendoline.Core;

namespace Gwendoline;

internal static class Program
{
    private const string AppMutex = @"Local\Gwendoline.App";
    private const string SupervisorMutex = @"Local\Gwendoline.Supervisor";
    /// <summary>Set by <c>Gwendoline.exe --quit</c>: the install scripts stop Gwendoline cleanly with it.</summary>
    private const string QuitEvent = @"Local\Gwendoline.Quit";

    /// <summary>
    /// <c>Gwendoline.exe</c> starts the supervisor, which runs the tray app as <c>Gwendoline.exe --app</c>
    /// and starts it again after a crash. <c>--quit</c> stops a running Gwendoline, <c>--restore</c>
    /// gives the lid actions back when Gwendoline is not running. <c>--claude</c> is the Claude Code hook.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--quit"))
        {
            return EventWaitHandle.TryOpenExisting(QuitEvent, out var quit) && quit.Set() ? 0 : 1;
        }
        if (args.Contains("--restore"))
        {
            return LidSleep.Restore() ? 0 : 1;
        }
        if (args.Contains("--claude"))
        {
            return ClaudeBridge.RunHook();
        }
        return args.Contains("--app") ? RunApp() : Supervise();
    }

    /// <summary>
    /// Runs the tray app and starts it again if it crashes. Every launch starts off, so a crash
    /// gives sleep back within seconds. After 5 crashes in a minute it gives up, sleep given back.
    /// </summary>
    private static int Supervise()
    {
        using var single = new Mutex(true, SupervisorMutex, out var first);
        if (!first)
        {
            return 0;
        }
        var crashes = new Queue<DateTime>();
        while (true)
        {
            using (var app = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--app") { UseShellExecute = false })!)
            {
                app.WaitForExit();
                if (app.ExitCode == 0)
                {
                    return 0;
                }
            }
            var now = DateTime.UtcNow;
            crashes.Enqueue(now);
            while (crashes.Peek() < now.AddMinutes(-1))
            {
                crashes.Dequeue();
            }
            if (crashes.Count >= 5)
            {
                LidSleep.Restore();
                return 1;
            }
            Thread.Sleep(2000);
        }
    }

    private static int RunApp()
    {
        // One copy at a time. Windows drops the mutex however the process ends, crash included.
        using var single = new Mutex(true, AppMutex, out var first);
        if (!first)
        {
            return 0;
        }
        // A crash ends the process with an error code, so the supervisor starts it again.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        // Telegram's awaits come back to the UI thread, where all of Gwendoline's state lives.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        using var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEvent);
        using var app = new TrayApp();
        var ui = SynchronizationContext.Current!;
        ThreadPool.RegisterWaitForSingleObject(quit, (_, _) => ui.Post(_ => app.Quit(CloseReason.Stopped), null), null, Timeout.Infinite, executeOnlyOnce: true);
        ClaudeBridge.Serve(ui, app.HandleClaude);
        Application.Run(app);
        return 0;
    }
}
