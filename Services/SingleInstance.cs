using System;
using System.Threading;
using Avalonia.Threading;

namespace ZTool.Services;

/// <summary>
/// One launcher per machine. This process owns a global hotkey and a tray
/// icon for its whole life, so a second copy is never useful — it would
/// silently lose the hotkey to the first and leave two icons behind.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\ZTool.SingleInstance";
    private const string SummonName = @"Local\ZTool.Summon";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _summon;

    // Registered wait keeps a reference alive until Dispose; dropping it
    // leaves the callback reachable from an unowned threadpool thread.
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle summon)
    {
        _mutex = mutex;
        _summon = summon;
    }

    /// <summary>
    /// Takes ownership for this process, or returns null when another copy
    /// already owns it — in which case the caller should exit at once.
    /// </summary>
    public static SingleInstance? Claim()
    {
        // The summon channel is created before the ownership mutex so a second
        // copy always finds something to knock on.
        var summon = new EventWaitHandle(false, EventResetMode.AutoReset, SummonName);
        var mutex = new Mutex(true, MutexName, out var isFirst);

        if (!isFirst)
        {
            mutex.Dispose();
            summon.Dispose();
            return null;
        }

        return new SingleInstance(mutex, summon);
    }

    /// <summary>Asks the owning copy to show itself. Safe to call from anyone.</summary>
    public static void RequestSummon()
    {
        try
        {
            using var summon = EventWaitHandle.OpenExisting(SummonName);
            summon.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // Nobody home. The caller will discover this when nothing appears.
        }
    }

    /// <summary>Wires the show-me callback. Runs on a threadpool thread.</summary>
    public void OnSummonRequested(Action show)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _summon,
            (state, timedOut) =>
            {
                if (!timedOut)
                {
                    Dispatcher.UIThread.Post(show);
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _registration = null;

        _summon.Dispose();

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Never owned — nothing to hand back.
        }

        _mutex.Dispose();
    }
}
