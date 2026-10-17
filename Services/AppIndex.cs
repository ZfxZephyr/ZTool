using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ZTool.Models;

namespace ZTool.Services;

/// <summary>
/// Installed applications. The Start Menu gives the shortcuts; the shell's
/// AppsFolder gives the Store builds that never drop one. Launching the .lnk
/// itself rather than its resolved target still honours the shortcut's
/// arguments, working directory and compatibility shim; the target is only
/// read, to say where the app actually lives.
/// </summary>
public sealed class AppIndex : IResultProvider
{
    private static readonly string[] Extensions = { ".lnk", ".appref-ms", ".exe" };

    // Start Menu folders are full of maintenance entries nobody launches twice,
    // and the AppsFolder adds the readme pile on top of them.
    private static readonly string[] Noise =
    {
        "uninstall", "卸载", "help", "帮助", "readme", "说明", "documentation",
        "license", "许可", "eula", "setup", "install", "安装",
        "manual", "手册", "文档", "release notes", "faq", "website", "网站",
        "入门", "教程", "tutorial",
    };

    // Tool caches, version control and build output. Walking these wastes the
    // file budget on artefacts nobody searches for by name.
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".vs", ".vscode", ".idea", ".gradle", ".m2", ".nuget",
        ".cache", "__pycache__", "node_modules", "bower_components", "packages",
        "bin", "obj", "target", "build", "dist", "out", "vendor",
        "unpackage", "Applet", "WeChat Files",
        "Cache", "Code Cache", "GPUCache", "DawnCache", "Crashpad",
        "Service Worker", "Local Storage", "Temporary Internet Files",
    };

    private List<SearchResult> _apps = new();

    public int Count => _apps.Count;

    public IEnumerable<SearchResult> Candidates() => _apps;

    public void Rebuild()
    {
        var found = new List<SearchResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in StartMenuRoots())
        {
            foreach (var file in Walk(root))
            {
                if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = Naming.ForApplication(file);
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                {
                    continue;
                }

                if (Noise.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                found.Add(new SearchResult
                {
                    Title = name,
                    Subtitle = Describe(file, root),
                    Kind = ResultKind.Application,
                    Icon = ShellIcon.For(file),
                    PinyinFull = TitlePinyin.Full(name),
                    PinyinInitials = TitlePinyin.Initials(name),
                    Launch = () =>
                    {
                        ShellOpen(file);
                        return true;
                    },
                });
            }
        }

        // Store builds and anything else registered only by AppUserModelID leave
        // no .lnk behind; the AppsFolder is the only list that has them. A name
        // the Start Menu already claimed keeps its shortcut — one app, one row.
        foreach (var app in AppsFolder.Enumerate())
        {
            if (string.IsNullOrWhiteSpace(app.Name) || !seen.Add(app.Name))
            {
                continue;
            }

            if (Noise.Any(n => app.Name.Contains(n, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var id = app.Id;
            var packaged = AppsFolder.IsAppId(id);

            found.Add(new SearchResult
            {
                Title = app.Name,
                Subtitle = app.Publisher,
                Kind = ResultKind.Application,
                Icon = app.Icon,
                PinyinFull = TitlePinyin.Full(app.Name),
                PinyinInitials = TitlePinyin.Initials(app.Name),
                Launch = () =>
                {
                    LaunchFromFolder(id, packaged);
                    return true;
                },
            });
        }

        _apps = found;
    }

    /// <summary>
    /// Opens an entry the AppsFolder gave us. A package is addressed by AUMID
    /// and goes through the activation manager, with the shell's own moniker as
    /// the fallback. Failing both leaves the row where it is, still selectable,
    /// for the next Enter.
    /// </summary>
    private static void LaunchFromFolder(string id, bool packaged)
    {
        if (packaged && AppActivation.TryActivate(id))
        {
            return;
        }

        ShellOpen(AppsFolder.ParsingName(id));
    }

    /// <summary>
    /// Where the app really lives. The resolved target beats the Start Menu
    /// group when we can read it; the group is the fallback, never a filler.
    /// </summary>
    private static string Describe(string file, string root)
    {
        var target = ResolveShortcut(file);
        if (!string.IsNullOrEmpty(target))
        {
            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir))
            {
                return dir;
            }
        }

        return StartMenuGroup(root, file);
    }

    /// <summary>Folder under the Start Menu with the plumbing stripped out.</summary>
    private static string StartMenuGroup(string root, string file)
    {
        var parent = Path.GetDirectoryName(file);
        if (string.IsNullOrEmpty(parent) || string.Equals(parent, root, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var relative = Path.GetRelativePath(root, parent);
        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            return parent;
        }

        var group = string.Join(" / ", relative
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(part => !part.Equals("Programs", StringComparison.OrdinalIgnoreCase)));

        return group;
    }

    /// <summary>
    /// Reads the shortcut target through the Windows script host. Returns
    /// empty on anything unexpected — a launcher must still list the app when
    /// its metadata is unreadable.
    /// </summary>
    private static string ResolveShortcut(string file)
    {
        if (!Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return file;
        }

        object? shell = null;
        object? shortcut = null;

        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null)
            {
                return string.Empty;
            }

            shell = Activator.CreateInstance(type);
            if (shell is null)
            {
                return string.Empty;
            }

            shortcut = type.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { file });

            if (shortcut is null)
            {
                return string.Empty;
            }

            return shortcut.GetType().InvokeMember(
                       "TargetPath",
                       System.Reflection.BindingFlags.GetProperty,
                       null,
                       shortcut,
                       null) as string
                   ?? string.Empty;
        }
        catch (Exception ex) when (ex is COMException or ExternalException or System.Reflection.TargetInvocationException)
        {
            return string.Empty;
        }
        finally
        {
            if (shortcut is not null)
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null)
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static IEnumerable<string> StartMenuRoots()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        };

        return roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r));
    }

    private static void ShellOpen(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>Walk that survives permission errors instead of dying on the first one.</summary>
    internal static IEnumerable<string> Walk(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir).ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    if (SkippedDirectories.Contains(Path.GetFileName(sub)))
                    {
                        continue;
                    }

                    pending.Push(sub);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Skip what we cannot read; the rest of the walk still matters.
            }
        }
    }
}
