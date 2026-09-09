using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Core.Dependency;
using Game.Core.Modding;
using ILogger = Core.Logging.ILogger;
using PrefixedLogger = Core.Logging.PrefixedLogger;

/// <summary>
/// Swaps a mod's running code for a freshly built assembly, without restarting the game.
///
/// The awkward part is that an assembly loaded into Unity's Mono runtime can never be
/// unloaded, and `Assembly.LoadFrom` on a path that is already loaded hands back the old
/// assembly rather than reading the file again. So each reload copies the DLL to a new
/// path under a unique name, which the runtime treats as a genuinely different assembly.
///
/// The consequences are unavoidable rather than incidental, and worth knowing:
///
/// - **Every reload leaks an assembly.** Fine for a development loop, never for shipping.
/// - **Old and new types are different types.** Anything the game still holds from the old
///   assembly keeps the old code alive.
/// - **A mod is only as reloadable as its Dispose.** Whatever it registered and did not
///   undo - a HUD element, an event subscription, a lane hook - survives the reload and
///   then exists twice.
/// </summary>
public class Reloader
{
    private readonly ILogger Logger;
    private readonly ModRegistry Registry;

    /// One shadow directory per session, cleared on the first reload.
    private readonly string ShadowRoot;
    private int Generation;

    public Reloader(ILogger logger, ModRegistry registry)
    {
        Logger = logger;
        Registry = registry;
        ShadowRoot = Path.Combine(Path.GetTempPath(), "spz2-mod-reloader");
    }

    public IEnumerable<string> Reload(string name)
    {
        List<string> report = new List<string>();

        if (!Registry.TryFind(name, out ResolvedMod resolved, out ExecutableMod executable, out string problem))
        {
            report.Add(problem);
            return report;
        }

        Type oldEntry = executable.EntryPoint.GetType();

        if (oldEntry.Assembly == typeof(Reloader).Assembly)
        {
            report.Add("Refusing to reload the reloader - that would dispose the code doing the reloading.");
            return report;
        }

        string[] assemblies = resolved.Metadata.Assemblies ?? Array.Empty<string>();
        string directory = ResolveSource(resolved, report);

        if (assemblies.Length == 0)
        {
            report.Add("That mod declares no assemblies to reload.");
            return report;
        }

        Generation++;
        report.Add("Reloading " + resolved.Descriptor.ModTitle + " (generation " + Generation + ")");

        // 1. Let the old instance undo whatever it did. Everything it fails to undo will
        //    now exist twice, which is the usual cause of a confusing reload.
        try
        {
            executable.EntryPoint.Dispose();
            report.Add("  disposed the old entry point");
        }
        catch (Exception exception)
        {
            Logger.Exception?.LogException(exception);
            report.Add("  the old entry point threw while disposing - see the log; continuing");
        }

        // 2. Copy to a path the runtime has not seen, so the new bytes are actually read.
        string shadow;
        try
        {
            shadow = Path.Combine(ShadowRoot, Generation.ToString());
            Directory.CreateDirectory(shadow);

            foreach (string file in Directory.GetFiles(directory))
            {
                File.Copy(file, Path.Combine(shadow, Path.GetFileName(file)), overwrite: true);
            }

            report.Add("  copied " + assemblies.Length + " assembly file(s) to a fresh path");
        }
        catch (Exception exception)
        {
            Logger.Exception?.LogException(exception);
            report.Add("  could not shadow-copy the mod - see the log. The old instance is now disposed.");
            return report;
        }

        // 3. Load and find the single IMod, the same way the game does.
        Type bootstrap;
        try
        {
            List<Type> types = new List<Type>();

            foreach (string assemblyName in assemblies)
            {
                Assembly loaded = Assembly.LoadFrom(Path.Combine(shadow, assemblyName));
                types.AddRange(LoadableTypes(loaded));
            }

            List<Type> entries = types
                .Where(type => typeof(IMod).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
                .ToList();

            if (entries.Count != 1)
            {
                report.Add("  expected exactly one IMod implementation, found " + entries.Count);
                return report;
            }

            bootstrap = entries[0];
        }
        catch (Exception exception)
        {
            Logger.Exception?.LogException(exception);
            report.Add("  could not load the rebuilt assembly - see the log.");
            return report;
        }

        // 4. Construct it exactly as ModLoader does: a container with ILogger bound.
        try
        {
            string prefix = resolved.Descriptor.ModId + "[" + resolved.Metadata.Version + "]";

            using (DependencyContainer container = new DependencyContainer())
            {
                container.Bind<Core.Logging.ILogger>().To(new PrefixedLogger(Logger, prefix));
                IMod instance = (IMod)container.Create(bootstrap);

                Registry.Replace(executable, instance);
                report.Add("  constructed " + bootstrap.Name + " from the new assembly");
            }
        }
        catch (Exception exception)
        {
            Logger.Exception?.LogException(exception);
            report.Add("  the new entry point threw while constructing - see the log. That mod is now not running.");
            return report;
        }

        report.Add("Reloaded. Anything the old instance did not undo in Dispose now exists twice.");
        return report;
    }

    /// <summary>
    /// Where to reload from.
    ///
    /// The copy in the mods folder is memory-mapped the moment the game loads it, so a
    /// build can never overwrite it while the game runs - which would leave the reloader
    /// with nothing new to read. The way out is to build somewhere else: a "mods-dev"
    /// folder beside the mods folder, which mod discovery does not scan (it only
    /// enumerates immediate subdirectories of "mods"), so a staged build is never loaded
    /// as a second mod.
    /// </summary>
    private string ResolveSource(ResolvedMod resolved, List<string> report)
    {
        string installed = resolved.Descriptor.DirectoryPath;
        string folder = Path.GetFileName(
            installed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        string staged = Path.Combine(GameEnvironment.DataPath, "mods-dev", folder);

        if (Directory.Exists(staged))
        {
            report.Add("  source: " + staged + " (staged " + BuiltWhen(staged, resolved) + ")");
            return staged;
        }

        report.Add("  source: the installed folder - which the game has locked, so this will");
        report.Add("          reload the same bytes. Use mrl.link, or build with -p:Dev=true.");
        return installed;
    }

    /// <summary>
    /// When the staged build was produced, so a stale one is obvious rather than
    /// mystifying - reloading old bytes looks exactly like a reload that did nothing.
    /// </summary>
    private static string BuiltWhen(string staged, ResolvedMod resolved)
    {
        try
        {
            string[] assemblies = resolved.Metadata.Assemblies ?? Array.Empty<string>();
            if (assemblies.Length == 0)
            {
                return "unknown";
            }

            string file = Path.Combine(staged, assemblies[0]);
            if (!File.Exists(file))
            {
                return "no assembly present";
            }

            TimeSpan age = DateTime.Now - File.GetLastWriteTime(file);
            return age.TotalMinutes < 1
                ? "seconds ago"
                : (int)age.TotalMinutes + " minutes ago";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type != null);
        }
    }
}
