using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ClaudeAlerta;

internal static class Program
{
    internal const string MutexName = @"Local\ClaudeAlerta.Desktop.v1";
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains(HookInstaller.Marker))
        {
            try
            {
                // Never return a decision or print content into Claude's hook protocol.
                var read = Task.Run(() =>
                {
                    var buffer = new char[4096]; var text = new StringBuilder(); int n;
                    using var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (text.Length + n > 2_000_000) throw new InvalidDataException("Evento muito grande.");
                        text.Append(buffer, 0, n);
                    }
                    return Signal.Parse(text.ToString());
                });
                if (read.Wait(2500))
                {
                    read.Result.Enqueue();
                    if (!Mutex.TryOpenExisting(MutexName, out var running))
                        Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
                        { ArgumentList = { "--tray" }, UseShellExecute = false, CreateNoWindow = true });
                    running?.Dispose();
                }
            }
            catch { /* Alert failures must never block Claude or expose the input. */ }
            return 0;
        }
        if (args.Contains("--self-test")) return SelfTest.Run(args.Last());
        if (args.Contains("--ui-check")) return UiCheck.Run(args.Last());
        if (args.Contains("--shutdown"))
        {
            Storage.Atomic(Path.Combine(Storage.Root, "exit.request"), DateTimeOffset.UtcNow.ToString("O"));
            return 0;
        }
        if (args.Contains("--install-hooks") || args.Contains("--remove-hooks"))
        {
            try
            {
                HookInstaller.Install(args.Contains("--remove-hooks"));
                return 0;
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(Storage.Root);
                File.WriteAllText(Path.Combine(Storage.Root, "installation-error.txt"), ex.Message);
                return 1;
            }
        }
        using var mutex = new Mutex(true, MutexName, out bool owns);
        if (!owns)
        {
            if (!args.Contains("--tray")) Storage.Atomic(Path.Combine(Storage.Root, "show.request"), DateTimeOffset.UtcNow.ToString("O"));
            return 0;
        }
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        var dashboard = new Dashboard(args.Contains("--tray"));
        app.Run(dashboard);
        return 0;
    }
}
