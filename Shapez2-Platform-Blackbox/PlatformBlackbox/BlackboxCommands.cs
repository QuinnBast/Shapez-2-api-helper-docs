using System;
using System.Collections.Generic;
using System.IO;
using Core.Collections;
using Game.Core.Blueprint.Exporter;
using Game.Core.Blueprint;
using ShapezShifter.Hijack;
using ShapezShifter.Kit;
using ILogger = Core.Logging.ILogger;

/// <summary>
/// The mod's commands. Registered directly as an <see cref="IConsoleRewirer"/> so they keep
/// a short prefix instead of being named after the assembly.
/// </summary>
public class BlackboxCommands : IConsoleRewirer
{
    private const string Prefix = "pbx.";

    private readonly ILogger Logger;
    private readonly SessionServices Session;

    public BlackboxCommands(ILogger logger, SessionServices session)
    {
        Logger = logger;
        Session = session;
    }

    public void RegisterCommands(IDebugConsole console)
    {
        Register(console, "analyze", null, Analyze);
        Register(console, "ports", null, ListPorts);
        Register(console, "capture", new DebugConsole.StringOption("name"), Capture);
        Register(console, "export", new DebugConsole.StringOption("file"), Export);
    }

    /// <summary>What the selection looks like from outside: size, and what crosses the boundary.</summary>
    private void Analyze(DebugConsole.CommandContext context)
    {
        if (!TrySelection(context, out IMapModel map, out ISelection<IslandModel> selection))
        {
            return;
        }

        context.Output?.Invoke(SelectionAnalysis.Of(map, selection).Describe());
    }

    /// <summary>Every boundary-crossing port, so a wrong count can be traced to a platform.</summary>
    private void ListPorts(DebugConsole.CommandContext context)
    {
        if (!TrySelection(context, out IMapModel map, out ISelection<IslandModel> selection))
        {
            return;
        }

        SelectionAnalysis analysis = SelectionAnalysis.Of(map, selection);
        if (analysis.ExternalPorts.Count == 0)
        {
            context.Output?.Invoke("No ports cross the boundary - this selection is self-contained.");
            return;
        }

        foreach (SelectionAnalysis.Port port in analysis.ExternalPorts)
        {
            context.Output?.Invoke("  " + port);
        }
    }

    /// <summary>Saves the selection into the player's blueprint library.</summary>
    private void Capture(DebugConsole.CommandContext context)
    {
        if (!TrySelection(context, out IMapModel map, out ISelection<IslandModel> selection))
        {
            return;
        }

        if (!Session.TryGetLibrary(out IBlueprintLibrary library))
        {
            context.Output?.Invoke("The blueprint library is not available yet - load a save first.");
            return;
        }

        string name = context.GetString(0);
        if (string.IsNullOrWhiteSpace(name))
        {
            context.Output?.Invoke("Give it a name: " + Prefix + "capture my-factory");
            return;
        }

        if (!TryBuildBlueprint(context, selection, out AnnotatedIslandBlueprint annotated))
        {
            return;
        }

        if (library.TrySaveEntry(name, annotated, library.RootEntry, allowOverride: true, version: 5))
        {
            SelectionAnalysis analysis = SelectionAnalysis.Of(map, selection);
            context.Output?.Invoke("Saved \"" + name + "\" to the blueprint library: "
                + analysis.IslandCount + " platforms, " + analysis.BuildingCount + " buildings, "
                + analysis.ExternalPorts.Count + " ports crossing the boundary.");
        }
        else
        {
            context.Output?.Invoke("The library refused to save it - the name may already be taken.");
        }
    }

    /// <summary>Writes the shareable blueprint string to a file next to the save games.</summary>
    private void Export(DebugConsole.CommandContext context)
    {
        if (!TrySelection(context, out IMapModel map, out ISelection<IslandModel> selection))
        {
            return;
        }

        if (!Session.TryGetExporter(out IBlueprintExporter exporter))
        {
            context.Output?.Invoke("The blueprint exporter is not available yet - load a save first.");
            return;
        }

        if (!TryBuildBlueprint(context, selection, out AnnotatedIslandBlueprint annotated))
        {
            return;
        }

        try
        {
            string exported = exporter.Export(annotated);
            string path = Path.Combine(GameEnvironment.DataPath, "blackbox");
            Directory.CreateDirectory(path);

            string file = Path.Combine(path, SanitiseFileName(context.GetString(0)) + ".txt");
            File.WriteAllText(file, exported);

            context.Output?.Invoke("Wrote " + exported.Length + " characters to " + file);
        }
        catch (Exception exception)
        {
            Logger.Exception?.LogException(exception);
            context.Output?.Invoke("Export failed - see the log.");
        }
    }

    private bool TryBuildBlueprint(DebugConsole.CommandContext context,
        ISelection<IslandModel> selection, out AnnotatedIslandBlueprint annotated)
    {
        annotated = null;

        try
        {
            IslandBlueprint blueprint = IslandBlueprint.FromSelection(
                selection, GameHelper.Core.DataSerializers);
            annotated = AnnotatedIslandBlueprint.WithoutMeta(blueprint);
            return true;
        }
        catch (Exception exception)
        {
            // Blueprints refuse some selections outright - overlapping entries, or content
            // the current game mode does not define.
            Logger.Exception?.LogException(exception);
            context.Output?.Invoke("Could not build a blueprint from this selection: " + exception.Message);
            return false;
        }
    }

    private static bool TrySelection(DebugConsole.CommandContext context,
        out IMapModel map, out ISelection<IslandModel> selection)
    {
        map = null;
        selection = null;

        Player player = GameHelper.Core?.LocalPlayer;
        map = player?.CurrentMap;
        if (map == null)
        {
            context.Output?.Invoke("No map loaded.");
            return false;
        }

        selection = player.InteractionState.IslandSelection;
        if (selection.Count == 0)
        {
            context.Output?.Invoke("Select one or more platforms first (space view, drag a selection).");
            return false;
        }

        return true;
    }

    private static string SanitiseFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "blackbox";
        }

        List<char> kept = new List<char>(name.Length);
        char[] invalid = Path.GetInvalidFileNameChars();

        foreach (char c in name)
        {
            kept.Add(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        return new string(kept.ToArray());
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
