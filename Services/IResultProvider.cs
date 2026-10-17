using System.Collections.Generic;
using ZTool.Models;

namespace ZTool.Services;

/// <summary>
/// A source of launchable things. Providers only enumerate candidates;
/// matching and ordering are the search service's job so every source ranks
/// the same way.
/// </summary>
public interface IResultProvider
{
    IEnumerable<SearchResult> Candidates();
}
