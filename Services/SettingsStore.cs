using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ZTool.Services;

/// <summary>
/// Where an app-level scalar ends up. Small, hand-editable, and shared: plugins
/// keep their own state here rather than each inventing a store, so a settings
/// surface has one place to read and write.
/// </summary>
public static class SettingsStore
{
    public const string FolderName = "ZTool";

    private static readonly object Gate = new();

    private static string PathTo => System.IO.Path.Combine(Folder, "settings.json");

    /// <summary>Everything lives in one folder beside the clipboard database.</summary>
    public static string Folder
    {
        get
        {
            var folder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                FolderName);

            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public static string ExclusionsPath => System.IO.Path.Combine(Folder, "exclusions.txt");

    public static bool ReadBool(string key, bool fallback)
    {
        lock (Gate)
        {
            return TryRead(out var map) && map.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.True;
        }
    }

    public static void WriteBool(string key, bool value)
    {
        lock (Gate)
        {
            var map = TryRead(out var existing) ? existing : new Dictionary<string, JsonElement>();
            map[key] = JsonSerializer.SerializeToElement(value);
            Write(map);
        }
    }

    private static bool TryRead(out Dictionary<string, JsonElement> map)
    {
        map = new Dictionary<string, JsonElement>();

        try
        {
            if (!File.Exists(PathTo))
            {
                return false;
            }

            var loaded = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(PathTo));
            if (loaded is null)
            {
                return false;
            }

            map = loaded;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A settings file that cannot be read is an unconfigured app, not a
            // broken one. Defaults stand and the next write repairs the file.
            return false;
        }
    }

    private static void Write(Dictionary<string, JsonElement> map)
    {
        try
        {
            File.WriteAllText(PathTo, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a preference is not worth failing whatever the user was doing.
        }
    }
}
