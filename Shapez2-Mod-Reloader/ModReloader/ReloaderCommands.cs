using System;
using System.Collections.Generic;
using ShapezShifter.Hijack;
using UnityEngine;
using ILogger = Core.Logging.ILogger;

/// <summary>
/// Console commands, registered directly as an <see cref="IConsoleRewirer"/> so they keep a
/// short prefix rather than being named after the assembly.
///
/// Includes the clipboard commands, which exist because there is no way to select text in
/// the in-game console - so any command that prints something worth keeping is unreadable
/// outside the game. <c>mrl.run</c> runs another command and captures what it printed.
/// </summary>
public class ReloaderCommands : IConsoleRewirer
{
    private const string Prefix = "mrl.";

    private readonly ILogger Logger;
    private readonly ModRegistry Registry;
    private readonly Reloader Reloader;
    private readonly Seeder Seeder;

    /// The last output captured by mrl.run, so mrl.copy can put it back on the clipboard.
    private string LastCaptured = string.Empty;

    public ReloaderCommands(ILogger logger, ModRegistry registry, Reloader reloader, Seeder seeder)
    {
        Logger = logger;
        Registry = registry;
        Reloader = reloader;
        Seeder = seeder;
    }

    public void RegisterCommands(IDebugConsole console)
    {
        Register(console, "list", null, context => Emit(context, Registry.Describe()));

        Register(console, "reload", new DebugConsole.StringOption("mod"),
            context => Emit(context, Reloader.Reload(context.GetString(0))));

        // Runs at startup too; this is for when a staged build appears mid-session.
        Register(console, "seed", null, context => Emit(context, Seeder.Seed()));

        // Run another command, print what it printed, and put it on the clipboard.
        Register(console, "run", new DebugConsole.StringOption("command"), context =>
        {
            string command = context.GetString(0);
            List<string> captured = new List<string>();

            try
            {
                console.ParseAndExecute(command, line => captured.Add(line));
            }
            catch (Exception exception)
            {
                Logger.Exception?.LogException(exception);
                captured.Add("\"" + command + "\" threw - see the log.");
            }

            LastCaptured = string.Join(Environment.NewLine, captured.ToArray());
            Emit(context, captured);

            if (TrySetClipboard(LastCaptured, out string problem))
            {
                context.Output?.Invoke("[" + captured.Count + " lines copied to the clipboard]");
            }
            else
            {
                context.Output?.Invoke("[clipboard unavailable: " + problem + " - the lines are in Player.log]");
            }
        });

        Register(console, "copy", null, context =>
        {
            if (string.IsNullOrEmpty(LastCaptured))
            {
                context.Output?.Invoke("Nothing captured yet. Run a command through mrl.run first.");
                return;
            }

            context.Output?.Invoke(TrySetClipboard(LastCaptured, out string problem)
                ? "Copied the last captured output to the clipboard."
                : "Clipboard unavailable: " + problem);
        });

        // The console has no paste either, so this runs whatever is on the clipboard -
        // which is also the way to run a command that needs several arguments.
        Register(console, "paste", null, context =>
        {
            string command;
            try
            {
                command = GUIUtility.systemCopyBuffer;
            }
            catch (Exception exception)
            {
                Logger.Exception?.LogException(exception);
                context.Output?.Invoke("Could not read the clipboard - see the log.");
                return;
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                context.Output?.Invoke("The clipboard is empty.");
                return;
            }

            command = command.Trim();
            context.Output?.Invoke("> " + command);

            try
            {
                console.ParseAndExecute(command, line => Emit(context, line));
            }
            catch (Exception exception)
            {
                Logger.Exception?.LogException(exception);
                context.Output?.Invoke("That command threw - see the log.");
            }
        });
    }

    /// <summary>
    /// Prints to the console and mirrors to the log, so output survives even when the
    /// clipboard does not work and the console cannot be scrolled back.
    /// </summary>
    private void Emit(DebugConsole.CommandContext context, IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            Emit(context, line);
        }
    }

    private void Emit(DebugConsole.CommandContext context, string line)
    {
        context.Output?.Invoke(line);
        Logger.Info?.Log(line);
    }

    private static bool TrySetClipboard(string text, out string problem)
    {
        problem = null;

        try
        {
            GUIUtility.systemCopyBuffer = text;
            return true;
        }
        catch (Exception exception)
        {
            problem = exception.GetType().Name;
            return false;
        }
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
