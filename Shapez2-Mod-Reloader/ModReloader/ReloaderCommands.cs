using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Modding;
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
    private readonly SourceLinks Links;

    /// The last output captured by mrl.run, so mrl.copy can put it back on the clipboard.
    private string LastCaptured = string.Empty;

    public ReloaderCommands(ILogger logger, ModRegistry registry, Reloader reloader, SourceLinks links)
    {
        Logger = logger;
        Registry = registry;
        Reloader = reloader;
        Links = links;
    }

    public void RegisterCommands(IDebugConsole console)
    {
        Register(console, "list", null, context => Emit(context, Registry.Describe()));

        Register(console, "reload", new DebugConsole.StringOption("mod"),
            context => Emit(context, Reloader.Reload(context.GetString(0))));

        // Point the reloader at where a mod's fresh build lands. The path comes from the
        // clipboard because a Windows path with spaces cannot survive command tokenising -
        // copy it in Explorer, then run this.
        Register(console, "link", new DebugConsole.StringOption("mod"), context =>
        {
            string mod = context.GetString(0);

            if (!Registry.TryFind(mod, out ResolvedMod resolved, out ExecutableMod _, out string problem))
            {
                Emit(context, problem);
                return;
            }

            string path;
            try
            {
                path = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim().Trim('"');
            }
            catch (Exception exception)
            {
                Logger.Exception?.LogException(exception);
                Emit(context, "Could not read the clipboard - see the log.");
                return;
            }

            if (path.Length == 0)
            {
                Emit(context, "Copy the build folder path first, then run this again.");
                Emit(context, "Usually the project's obj/Debug - a normal build writes there even");
                Emit(context, "when the copy into the mods folder fails.");
                return;
            }

            if (!Directory.Exists(path))
            {
                Emit(context, "No such folder: " + path);
                return;
            }

            string folder = ModFolderName(resolved);
            string[] assemblies = resolved.Metadata.Assemblies ?? new string[0];
            bool found = false;

            foreach (string assembly in assemblies)
            {
                if (File.Exists(Path.Combine(path, assembly)))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Emit(context, "That folder has none of " + folder + "'s assemblies in it - "
                    + "expected one of: " + string.Join(", ", assemblies));
                return;
            }

            Links.Set(folder, path);
            Emit(context, "Linked " + folder + " -> " + path);
            Emit(context, "Saved to " + Links.ConfigPath);
        });

        Register(console, "links", null, context =>
        {
            int count = 0;

            foreach (KeyValuePair<string, string> link in Links.All)
            {
                Emit(context, "  " + link.Key + " -> " + link.Value);
                count++;
            }

            if (count == 0)
            {
                Emit(context, "No links yet. Copy a build folder path, then: mrl.link <mod>");
            }
        });

        Register(console, "unlink", new DebugConsole.StringOption("mod"), context =>
        {
            string mod = context.GetString(0);
            Emit(context, Links.Remove(mod)
                ? "Unlinked " + mod
                : "No link for \"" + mod + "\" - names come from mrl.links");
        });

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

    private static string ModFolderName(ResolvedMod resolved)
    {
        string directory = resolved.Descriptor.DirectoryPath ?? string.Empty;
        return Path.GetFileName(
            directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
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
