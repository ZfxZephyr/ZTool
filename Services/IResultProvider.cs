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

    /// <summary>
    /// The query about to be scored. A provider backed by an external index —
    /// Everything, for one — uses this to go and fetch candidates, because its
    /// whole value is filtering millions of rows down to a handful per query.
    /// A provider with a fixed set ignores it, which is why it defaults to a
    /// no-op and the Start Menu and command sources never mention it.
    /// </summary>
    void Prime(string query)
    {
    }
}
