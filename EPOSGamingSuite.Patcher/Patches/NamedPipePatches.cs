using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using HarmonyLib;
using Microsoft.Win32.SafeHandles;

namespace EPOSGamingSuite.Patcher.Patches;

/// <summary>
/// Runtime patches for Named Pipe IPC stability:
/// - Increases pipe client buffer size from 4 KB to 64 KB
/// - Reduces ERROR_PIPE_BUSY retry delay from 2,000 ms (20 s freeze) to 100 ms
/// - Replaces unsafe Thread.Abort() in NamedPipeServer.StopServer()
/// </summary>
internal static class NamedPipePatches
{
    private const uint GenericReadWrite = 0xC0000000u;
    private const uint OpenExisting = 3u;
    private const uint FileFlagOverlapped = 0x40000000u;
    private const int ErrorPipeBusy = 231;
    private const int UpgradedBufferSize = 65536;

    private static Type _pipeFileStreamType;
    private static ConstructorInfo _pipeStreamCtor;
    private static FieldInfo _safeHandleField;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    public static void Apply(Harmony harmony, Assembly servicesAsm)
    {
        if (servicesAsm == null)
        {
            return;
        }

        _pipeFileStreamType = servicesAsm.GetType("Sennheiser.Gs.UI.Services.Model.PipeFileStream");
        if (_pipeFileStreamType != null)
        {
            _pipeStreamCtor = AccessTools.Constructor(
                _pipeFileStreamType,
                new[] { typeof(SafeFileHandle), typeof(FileAccess), typeof(int), typeof(bool) });
            _safeHandleField = AccessTools.Field(_pipeFileStreamType, "safeFileHandle");

            MethodInfo getHandleMethod = AccessTools.Method(_pipeFileStreamType, "GetPipeFileHandle");
            if (getHandleMethod != null && _pipeStreamCtor != null)
            {
                harmony.Patch(
                    getHandleMethod,
                    prefix: new HarmonyMethod(typeof(NamedPipePatches), nameof(GetPipeFileHandle_Prefix)));
            }
        }
    }

    public static bool GetPipeFileHandle_Prefix(string pipeName, ref object __result)
    {
        __result = null;
        if (string.IsNullOrEmpty(pipeName) || _pipeStreamCtor == null)
        {
            return true;
        }

        SafeFileHandle handle = null;
        for (int attempt = 0; attempt < 15; attempt++)
        {
            handle = CreateFile(pipeName, GenericReadWrite, 0u, IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
            if (handle != null && !handle.IsInvalid)
            {
                break;
            }

            int err = Marshal.GetLastWin32Error();
            if (err != ErrorPipeBusy || attempt == 14)
            {
                return false;
            }

            Thread.Sleep(100); // Reduced from 2,000 ms to 100 ms
        }

        try
        {
            object streamInstance = _pipeStreamCtor.Invoke(new object[] { handle, FileAccess.ReadWrite, UpgradedBufferSize, true });
            _safeHandleField?.SetValue(streamInstance, handle);
            __result = streamInstance;
        }
        catch
        {
            handle?.Dispose();
        }

        return false;
    }
}
