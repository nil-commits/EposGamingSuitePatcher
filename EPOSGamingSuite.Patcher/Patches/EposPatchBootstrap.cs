using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace EPOSGamingSuite.Patcher.Patches;

/// <summary>
/// Initializes Lib.Harmony and applies all clean-room runtime fixes to the loaded EPOS assemblies.
/// </summary>
public static class EposPatchBootstrap
{
    public const string HarmonyId = "dev.nilcommits.eposgamingsuite.patcher";
    private static bool _applied;
    private static readonly object SyncRoot = new object();

    public static Assembly ServicesAssembly { get; private set; }
    public static Assembly UiAssembly { get; private set; }

    public static void ApplyAll(string installDir, Assembly servicesAsm = null, Assembly uiAsm = null)
    {
        lock (SyncRoot)
        {
            if (_applied)
            {
                return;
            }

            ServicesAssembly = servicesAsm ?? LoadOrFindAssembly(installDir, "GamingSuite.UI.Services", "GamingSuite.UI.Services.dll");
            UiAssembly = uiAsm ?? LoadOrFindAssembly(installDir, "EPOSGamingSuite", "EPOSGamingSuite.exe");

            var harmony = new Harmony(HarmonyId);

            CoreAudioPatches.Apply(harmony, ServicesAssembly);
            FastConnectPatches.Apply(harmony, ServicesAssembly);
            NamedPipePatches.Apply(harmony, ServicesAssembly);
            PresetAndXmlPatches.Apply(harmony, ServicesAssembly);

            _applied = true;
            LogPatchStatus("All Harmony runtime patches applied successfully.");
        }
    }

    private static Assembly LoadOrFindAssembly(string installDir, string simpleName, string fileName)
    {
        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(loaded.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
            {
                return loaded;
            }
        }

        string candidate = Path.Combine(installDir, fileName);
        return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
    }

    internal static void LogPatchStatus(string message)
    {
        try
        {
            Trace.WriteLine("[EPOSGamingSuite.Patcher] " + message);
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EPOS", "Gaming Suite", "Logs");
            if (Directory.Exists(logDir))
            {
                File.AppendAllText(
                    Path.Combine(logDir, "HarmonyPatcher.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [INF] {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
