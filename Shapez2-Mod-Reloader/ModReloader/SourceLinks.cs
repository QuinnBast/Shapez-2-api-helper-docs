using System;
using System.Collections.Generic;
using System.IO;
using ILogger = Core.Logging.ILogger;

/// <summary>
/// Remembers where each mod's fresh build lands, so a modder configures it once per machine
/// instead of changing how their project builds.
///
/// This exists because of a small piece of luck: a normal build still writes the compiled
/// assembly to the project's <c>obj/Debug</c> before it tries to copy it into the mods
/// folder. Only that copy fails while the game holds the installed file. So pointing the
/// reloader at <c>obj/Debug</c> means an unmodified project, built the way its author
/// already builds it, produces reloadable bytes.
///
/// Stored as one <c>folder|path</c> per line rather than JSON - it wants to be readable and
/// hand-editable more than it wants structure.
/// </summary>
public class SourceLinks
{
    private readonly ILogger Logger;
    private readonly string File;
    private readonly Dictionary<string, string> Links =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public SourceLinks(ILogger logger, string dataPath)
    {
        Logger = logger;
        File = Path.Combine(dataPath, "mod-reloader-sources.txt");
        Load();
    }

    public string ConfigPath => File;

    public IEnumerable<KeyValuePair<string, string>> All => Links;

    public bool TryGet(string folder, out string path)
    {
        return Links.TryGetValue(folder, out path);
    }

    public void Set(string folder, string path)
    {
        Links[folder] = path;
        Save();
    }

    public bool Remove(string folder)
    {
        bool removed = Links.Remove(folder);
        if (removed)
        {
            Save();
        }

        return removed;
    }

    private void Load()
    {
        try
        {
            if (!System.IO.File.Exists(File))
            {
                return;
            }

            foreach (string line in System.IO.File.ReadAllLines(File))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                {
                    continue;
                }

                int separator = trimmed.IndexOf('|');
                if (separator <= 0)
                {
                    continue;
                }

                Links[trimmed.Substring(0, separator).Trim()] = trimmed.Substring(separator + 1).Trim();
            }
        }
        catch (Exception exception)
        {
            Logger.Exception?.LogException(exception);
        }
    }

    private void Save()
    {
        try
        {
            List<string> lines = new List<string>
            {
                "# Mod Reloader: where each mod's fresh build lands.",
                "# One <mod folder>|<directory> per line. Edit by hand or use mrl.link.",
                "# Pointing at a project's obj/Debug works without changing how it builds.",
                string.Empty
            };

            foreach (KeyValuePair<string, string> link in Links)
            {
                lines.Add(link.Key + "|" + link.Value);
            }

            System.IO.File.WriteAllLines(File, lines.ToArray());
        }
        catch (Exception exception)
        {
            Logger.Exception?.LogException(exception);
        }
    }
}
