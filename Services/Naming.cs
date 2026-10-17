using System;
using System.IO;

namespace ZTool.Services;

/// <summary>
/// Turning a filename into the name a person would say out loud. Windows
/// writes OS chrome into filenames; a launcher has to read past it.
/// </summary>
public static class Naming
{
    private static readonly string[] ShortcutSuffixes =
    {
        " - 快捷方式", " - Shortcut", " - Raccourci", " - Verknüpfung",
    };

    /// <summary>
    /// Name for an application entry. Drops the shortcut label and a trailing
    /// executable extension, neither of which is part of the app's name.
    /// </summary>
    public static string ForApplication(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);

        foreach (var suffix in ShortcutSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        // "MobaXterm.exe - 快捷方式" leaves "MobaXterm.exe"; the extension is
        // not the name either.
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^".exe".Length];
        }

        return name.Trim();
    }

    /// <summary>Files Windows drops in every folder and nobody searches for.</summary>
    public static bool IsSystemFile(string fileName) =>
        fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("ehthumbs.db", StringComparison.OrdinalIgnoreCase);
}
