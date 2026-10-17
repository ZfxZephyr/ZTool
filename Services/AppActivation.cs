using System;
using System.Runtime.InteropServices;

namespace ZTool.Services;

/// <summary>
/// Launches a Store app by AppUserModelID. A package has no path to hand to
/// ShellExecute, so it goes through the shell's activation manager instead.
/// </summary>
public static class AppActivation
{
    private const int Ok = 0;

    [Flags]
    private enum Options
    {
        None = 0,
        DesignMode = 1,
        NoErrorUI = 2,
        NoSplashScreen = 4,
    }

    /// <summary>True when the shell took the request.</summary>
    public static bool TryActivate(string appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId))
        {
            return false;
        }

        object? instance = null;
        var manager = default(IApplicationActivationManager);

        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"));
            if (type is null)
            {
                return false;
            }

            instance = Activator.CreateInstance(type);
            if (instance is not IApplicationActivationManager activation)
            {
                return false;
            }

            manager = activation;

            // No error UI: a launcher that cannot open a row says so in its own
            // footer, it does not summon a Windows dialog over the palette.
            return activation.ActivateApplication(appUserModelId, null, Options.NoErrorUI, out _) == Ok;
        }
        catch (Exception ex) when (ex is COMException or ExternalException or InvalidCastException)
        {
            return false;
        }
        finally
        {
            if (manager is not null)
            {
                Marshal.FinalReleaseComObject(manager);
            }
            else if (instance is not null)
            {
                Marshal.FinalReleaseComObject(instance);
            }
        }
    }

    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            Options options,
            out uint processId);

        // Declared only to hold the vtable in order; never called.
        [PreserveSig]
        int ActivateForFile(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string verb,
            out uint processId);

        [PreserveSig]
        int ActivateForProtocol(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            out uint processId);
    }
}
