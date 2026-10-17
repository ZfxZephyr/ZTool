using System;
using System.Collections.Generic;
using System.Linq;
using ZTool.Models;

namespace ZTool.Services;

/// <summary>
/// Things the tool itself can do. These need no index and must stay available
/// even when the app and file scans come back empty.
/// </summary>
public sealed class CommandProvider : IResultProvider
{
    private readonly List<SearchResult> _commands;

    /// <summary>Run returns false for commands that are a change of scene rather than a departure.</summary>
    public CommandProvider(params (string Title, string Subtitle, Func<bool> Run)[] commands)
    {
        _commands = commands
            .Select(c => new SearchResult
            {
                Title = c.Title,
                Subtitle = c.Subtitle,
                Kind = ResultKind.Command,
                PinyinFull = TitlePinyin.Full(c.Title),
                PinyinInitials = TitlePinyin.Initials(c.Title),
                Launch = c.Run,
            })
            .ToList();
    }

    public IEnumerable<SearchResult> Candidates() => _commands;
}
