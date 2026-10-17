using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ZTool.Services;

/// <summary>
/// Processes whose copies stay out of the history. Password managers go here by
/// default — the list is a text file one name per line because editing it is a
/// once-a-year event and a settings panel would be the wrong tool for it.
/// Reread on every copy so an edit takes effect without a restart.
/// </summary>
public static class ProcessExclusions
{
    // The obvious ones. A clipboard history that swallows a password manager's
    // output is a liability, not a feature.
    private static readonly string[] Defaults =
    {
        "KeePass", "KeePassXC", "1Password", "Bitwarden", "LastPass",
        "Dashlane", "RoboForm", "Enpass", "PasswordSafe",
    };

    public static IReadOnlyList<string> DefaultsForFile => Defaults;

    /// <summary>True when this process is on the list. Compares bare names, case-insensitively.</summary>
    public static bool IsExcluded(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var bare = BareName(processName);

        foreach (var line in ReadLines())
        {
            if (bare.Equals(BareName(line), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Creates the file with the defaults the first time it is asked for, so the
    /// user has something to edit rather than a blank page to guess at.
    /// </summary>
    public static string EnsureFile()
    {
        var path = SettingsStore.ExclusionsPath;

        if (!File.Exists(path))
        {
            try
            {
                File.WriteAllLines(path, Defaults);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Fall through: IsExcluded still consults the in-code defaults.
            }
        }

        return path;
    }

    private static IEnumerable<string> ReadLines()
    {
        foreach (var fallback in Defaults)
        {
            yield return fallback;
        }

        string[] lines;
        try
        {
            lines = File.Exists(SettingsStore.ExclusionsPath)
                ? File.ReadAllLines(SettingsStore.ExclusionsPath)
                : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith('#'))
            {
                yield return trimmed;
            }
        }
    }

    /// <summary>"C:\Program Files\KeePass\KeePass.exe", "KeePass.exe" and "KeePass" are one name.</summary>
    private static string BareName(string value)
    {
        var name = value.Trim().Trim('"');

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        var slash = name.LastIndexOfAny(new[] { '\\', '/' });
        return slash >= 0 ? name[(slash + 1)..] : name;
    }
}
