using System;
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
            if (uiAsm != null)
            {
                UiAssembly = uiAsm;
            }

            var harmony = new Harmony(HarmonyId);

            CoreAudioPatches.Apply(harmony, ServicesAssembly);
            FastConnectPatches.Apply(harmony, ServicesAssembly);
            NamedPipePatches.Apply(harmony, ServicesAssembly);
            PresetAndXmlPatches.Apply(harmony, ServicesAssembly);

            if (UiAssembly != null)
            {
                PatchUiAssembly(harmony, UiAssembly);
            }
            else
            {
                foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(loaded.GetName().Name, "EPOSGamingSuite", StringComparison.OrdinalIgnoreCase))
                    {
                        UiAssembly = loaded;
                        PatchUiAssembly(harmony, loaded);
                        break;
                    }
                }

                if (UiAssembly == null)
                {
                    AppDomain.CurrentDomain.AssemblyLoad += (_, args) =>
                    {
                        if (string.Equals(args.LoadedAssembly?.GetName().Name, "EPOSGamingSuite", StringComparison.OrdinalIgnoreCase))
                        {
                            UiAssembly = args.LoadedAssembly;
                            PatchUiAssembly(harmony, args.LoadedAssembly);
                        }
                    };
                }
            }

            _applied = true;
            LogPatchStatus("All Harmony runtime patches applied successfully.");
        }
    }

    private static void PatchUiAssembly(Harmony harmony, Assembly uiAsm)
    {
        try
        {
            Type appType = uiAsm?.GetType("Sennheiser.Gs.UI.App");
            MethodInfo getWindowHandle = AccessTools.Method(appType, "GetWindowHandle");
            if (getWindowHandle != null)
            {
                harmony.Patch(
                    getWindowHandle,
                    prefix: new HarmonyMethod(typeof(EposPatchBootstrap), nameof(GetWindowHandle_Prefix)));
            }
        }
        catch
        {
        }
    }

    public static void GetWindowHandle_Prefix(ref string title)
    {
        if (string.IsNullOrEmpty(title))
        {
            title = "MainWindow";
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

        try
        {
            return Assembly.Load(simpleName);
        }
        catch
        {
            string candidate = Path.Combine(installDir, fileName);
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }
    }

    internal static void LogPatchStatus(string message)
    {
        try
        {
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
