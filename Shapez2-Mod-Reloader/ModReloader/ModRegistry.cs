using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Core.Modding;
using ILogger = Core.Logging.ILogger;

/// <summary>
/// Reaches the game's mod loader and describes what it has loaded.
///
/// `GameModdingFramework` is bound in the game session's dependency container, and it holds
/// the `ModLoader`. Both the field and the loader's lists are private, which the publicizer
/// makes reachable.
/// </summary>
public class ModRegistry
{
    private readonly ILogger Logger;

    private GameSessionOrchestrator Orchestrator;
    private ModLoader Loader;

    public ModRegistry(ILogger logger)
    {
        Logger = logger;
    }

    public void Capture(GameSessionOrchestrator orchestrator)
    {
        if (!ReferenceEquals(Orchestrator, orchestrator))
        {
            Orchestrator = orchestrator;
            Loader = null;
        }
    }

    public bool TryGetLoader(out ModLoader loader)
    {
        if (Loader == null && Orchestrator != null)
        {
            try
            {
                Loader = Orchestrator.DependencyContainer
                    .Resolve<GameModdingFramework>()
                    .ModLoader;
            }
            catch (Exception exception)
            {
                Logger.Exception?.LogException(exception);
            }
        }

        loader = Loader;
        return loader != null;
    }

    /// <summary>One line per loaded mod: what it is, and where its assembly came from.</summary>
    public IEnumerable<string> Describe()
    {
        if (!TryGetLoader(out ModLoader loader))
        {
            return new[] { "The mod loader is not reachable yet - load a save first." };
        }

        List<string> lines = new List<string>();

        foreach (ExecutableMod mod in loader.ExecutableMods)
        {
            Type entry = mod.EntryPoint.GetType();
            string assembly;

            try
            {
                assembly = Path.GetFileName(entry.Assembly.Location);
            }
            catch (Exception)
            {
                assembly = "<unknown>";
            }

            lines.Add("  " + mod.Metadata.Version + "  " + entry.Name.PadRight(28) + assembly);
        }

        lines.Insert(0, loader.ExecutableMods.Count + " mods loaded");
        return lines;
    }

    /// <summary>
    /// Finds a loaded mod whose title, entry point type or directory matches, case
    /// insensitively. Returns false when the name is ambiguous, so a typo cannot reload
    /// something unintended.
    /// </summary>
    public bool TryFind(string name, out ResolvedMod resolved, out ExecutableMod executable, out string problem)
    {
        resolved = default(ResolvedMod);
        executable = default(ExecutableMod);
        problem = null;

        if (!TryGetLoader(out ModLoader loader))
        {
            problem = "The mod loader is not reachable yet - load a save first.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            problem = "Name a mod. Try mrl.list.";
            return false;
        }

        List<ResolvedMod> matches = new List<ResolvedMod>();

        foreach (ResolvedMod candidate in loader.ResolvedMods)
        {
            if (Matches(candidate, name))
            {
                matches.Add(candidate);
            }
        }

        if (matches.Count == 0)
        {
            problem = "No loaded mod matches \"" + name + "\". Try mrl.list.";
            return false;
        }

        if (matches.Count > 1)
        {
            problem = "\"" + name + "\" matches " + matches.Count + " mods - be more specific.";
            return false;
        }

        resolved = matches[0];

        foreach (ExecutableMod mod in loader.ExecutableMods)
        {
            if (ReferenceEquals(mod.Metadata, resolved.Metadata))
            {
                executable = mod;
                return true;
            }
        }

        problem = "\"" + name + "\" resolved but has no running entry point.";
        return false;
    }

    private static bool Matches(ResolvedMod candidate, string name)
    {
        string title = candidate.Descriptor.ModTitle ?? string.Empty;
        string directory = candidate.Descriptor.DirectoryPath ?? string.Empty;

        return title.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0
            || Path.GetFileName(directory).IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Replaces a mod's running entry point in the loader's own list.</summary>
    public void Replace(ExecutableMod old, IMod entryPoint)
    {
        if (!TryGetLoader(out ModLoader loader))
        {
            return;
        }

        List<ExecutableMod> mods = loader._ExecutableMods;

        for (int i = 0; i < mods.Count; i++)
        {
            if (ReferenceEquals(mods[i].EntryPoint, old.EntryPoint))
            {
                mods[i] = new ExecutableMod(old.Metadata, entryPoint);
                return;
            }
        }
    }
}
