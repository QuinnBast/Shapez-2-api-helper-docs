using System;
using ShapezShifter.Hijack;
using ILogger = Core.Logging.ILogger;

/// <summary>
/// Console commands, registered directly as an <see cref="IConsoleRewirer"/> so they keep a
/// short prefix rather than being named after the assembly.
/// </summary>
public class ReloaderCommands : IConsoleRewirer
{
    private const string Prefix = "mrl.";

    private readonly ILogger Logger;
    private readonly ModRegistry Registry;
    private readonly Reloader Reloader;

    public ReloaderCommands(ILogger logger, ModRegistry registry, Reloader reloader)
    {
        Logger = logger;
        Registry = registry;
        Reloader = reloader;
    }

    public void RegisterCommands(IDebugConsole console)
    {
        Register(console, "list", null, context =>
        {
            foreach (string line in Registry.Describe())
            {
                context.Output?.Invoke(line);
            }
        });

        Register(console, "reload", new DebugConsole.StringOption("mod"), context =>
        {
            foreach (string line in Reloader.Reload(context.GetString(0)))
            {
                context.Output?.Invoke(line);
            }
        });
    }

    /// One command failing to register must not take the rest of them down with it.
    private void Register(IDebugConsole console, string id, DebugConsole.ConsoleOption option,
        Action<DebugConsole.CommandContext> handler)
    {
        try
        {
            if (option == null)
            {
                console.Register(Prefix + id, handler);
            }
            else
            {
                console.Register(Prefix + id, option, handler);
            }
        }
        catch (Exception exception)
        {
            Logger.Exception?.LogException(exception);
        }
    }
}
