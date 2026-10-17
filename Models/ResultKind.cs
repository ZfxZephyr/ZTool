namespace ZTool.Models;

/// <summary>
/// What a hit is, not what it looks like. Providers tag candidates so the
/// list can stay quiet and still be scannable at a glance.
/// </summary>
public enum ResultKind
{
    Application,
    File,
    Command,
}
