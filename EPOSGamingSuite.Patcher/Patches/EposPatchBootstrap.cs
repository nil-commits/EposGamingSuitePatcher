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

            Type soundVmType = uiAsm?.GetType("Sennheiser.Gs.UI.ViewModel.SoundViewModel");
            MethodInfo popPlaybackDetails = AccessTools.Method(soundVmType, "PopulatePlaybackDeviceDetails");
            if (popPlaybackDetails != null)
            {
                harmony.Patch(
                    popPlaybackDetails,
                    postfix: new HarmonyMethod(typeof(EposPatchBootstrap), nameof(PopulatePlaybackDeviceDetails_Postfix)));
            }

            Type micVmType = uiAsm?.GetType("Sennheiser.Gs.UI.ViewModel.MicrophoneViewModel");
            MethodInfo popMicDetails = AccessTools.Method(micVmType, "PopulateMicrophoneDeviceDetails");
            if (popMicDetails != null)
            {
                harmony.Patch(
                    popMicDetails,
                    postfix: new HarmonyMethod(typeof(EposPatchBootstrap), nameof(PopulateMicrophoneDeviceDetails_Postfix)));
            }

            Type ucPlaybackType = uiAsm?.GetType("Sennheiser.Gs.UI.UserControls.UCPlaybackDevice");
            MethodInfo updateAutoSleepMic = AccessTools.Method(ucPlaybackType, "UpdateAutoSleepOnMicChange");
            if (updateAutoSleepMic != null)
            {
                harmony.Patch(
                    updateAutoSleepMic,
                    prefix: new HarmonyMethod(typeof(EposPatchBootstrap), nameof(UCPlayback_UpdateAutoSleepOnMicChange_Prefix)));
            }

            MethodInfo updateLatencyMic = AccessTools.Method(ucPlaybackType, "UpdateLatencyOnMicChange");
            if (updateLatencyMic != null)
            {
                harmony.Patch(
                    updateLatencyMic,
                    prefix: new HarmonyMethod(typeof(EposPatchBootstrap), nameof(UCPlayback_UpdateLatencyOnMicChange_Prefix)));
            }

            Type ucMicType = uiAsm?.GetType("Sennheiser.Gs.UI.UserControls.UCMicrophoneDevice");
            MethodInfo updateAutoSleepPlayback = AccessTools.Method(ucMicType, "UpdateAutoSleepOnPlaybackChange");
            if (updateAutoSleepPlayback != null)
            {
                harmony.Patch(
                    updateAutoSleepPlayback,
                    prefix: new HarmonyMethod(typeof(EposPatchBootstrap), nameof(UCMic_UpdateAutoSleepOnPlaybackChange_Prefix)));
            }

            MethodInfo updateLatencyPlayback = AccessTools.Method(ucMicType, "UpdateLatencyOnPlaybackChange");
            if (updateLatencyPlayback != null)
            {
                harmony.Patch(
                    updateLatencyPlayback,
                    prefix: new HarmonyMethod(typeof(EposPatchBootstrap), nameof(UCMic_UpdateLatencyOnPlaybackChange_Prefix)));
            }

            MethodInfo btnMicLatencyClick = AccessTools.Method(ucMicType, "BtnMicrophoneAdaptiveLatency_Click");
            if (btnMicLatencyClick != null)
            {
                harmony.Patch(
                    btnMicLatencyClick,
                    prefix: new HarmonyMethod(typeof(EposPatchBootstrap), nameof(UCMic_BtnMicrophoneAdaptiveLatency_Click_Prefix)));
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

    /// <summary>
    /// Populates the missing PlaybackDeviceDongleVIDUI, PlaybackDeviceDonglePIDUI, and PlaybackDeviceDongleSerialNumberUI
    /// properties on SoundViewModel when wireless dongle devices (like H3PRO Hybrid Dongle PID 771) are connected.
    /// </summary>
    public static void PopulatePlaybackDeviceDetails_Postfix(object __instance)
    {
        if (__instance == null)
        {
            return;
        }

        try
        {
            Type spkDataType = ServicesAssembly?.GetType("Sennheiser.Gs.UI.Services.Model.PlaybackDeviceData");
            object spkInstance = AccessTools.Method(spkDataType, "GetInstance")?.Invoke(null, null);
            if (spkInstance == null)
            {
                return;
            }

            dynamic vm = __instance;
            dynamic spk = spkInstance;
            vm.PlaybackDeviceDongleVIDUI = (string)spk.PlaybackDeviceDongleVID ?? string.Empty;
            vm.PlaybackDeviceDonglePIDUI = (string)spk.PlaybackDeviceDonglePID ?? string.Empty;
            vm.PlaybackDeviceDongleSerialNumberUI = (string)spk.PlaybackDeviceDongleSerialNumber ?? string.Empty;
        }
        catch
        {
        }
    }

    /// <summary>
    /// Populates the missing MicrophoneDeviceDongleVIDUI, MicrophoneDeviceDonglePIDUI, and MicrophoneDeviceDongleSerialNumberUI
    /// properties on MicrophoneViewModel when wireless dongle devices (like H3PRO Hybrid Dongle PID 771) are connected.
    /// </summary>
    public static void PopulateMicrophoneDeviceDetails_Postfix(object __instance)
    {
        if (__instance == null)
        {
            return;
        }

        try
        {
            Type micDataType = ServicesAssembly?.GetType("Sennheiser.Gs.UI.Services.Model.MicrophoneDeviceData");
            object micInstance = AccessTools.Method(micDataType, "GetInstance")?.Invoke(null, null);
            if (micInstance == null)
            {
                return;
            }

            dynamic vm = __instance;
            dynamic mic = micInstance;
            vm.MicrophoneDeviceDongleVIDUI = (string)mic.MicrophoneDeviceDongleVID ?? string.Empty;
            vm.MicrophoneDeviceDonglePIDUI = (string)mic.MicrophoneDeviceDonglePID ?? string.Empty;
            vm.MicrophoneDeviceDongleSerialNumberUI = (string)mic.MicrophoneDeviceDongleSerialNumber ?? string.Empty;
        }
        catch
        {
        }
    }

    /// <summary>
    /// Replaces localized UI label string comparison ("On") with state-based toggling of PlaybackAutoSleepStatusUI.
    /// </summary>
    public static bool UCPlayback_UpdateAutoSleepOnMicChange_Prefix(object __instance)
    {
        if (__instance == null)
        {
            return false;
        }

        try
        {
            Type t = __instance.GetType();
            dynamic soundVm = AccessTools.Field(t, "soundViewModel")?.GetValue(__instance);
            var lbl = AccessTools.Field(t, "lblautosleepstatus")?.GetValue(__instance) as System.Windows.FrameworkElement;
            if (soundVm != null && lbl != null)
            {
                if ((string)soundVm.PlaybackAutoSleepStatusUI == "1")
                {
                    lbl.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty, "Off");
                    soundVm.PlaybackAutoSleepStatusUI = "0";
                }
                else
                {
                    lbl.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty, "On");
                    soundVm.PlaybackAutoSleepStatusUI = "1";
                }
            }
        }
        catch
        {
        }

        return false;
    }

    /// <summary>
    /// Replaces localized UI label string comparison ("ADAPTIVE LATENCY") with state-based toggling of LatencyLevel.
    /// </summary>
    public static bool UCPlayback_UpdateLatencyOnMicChange_Prefix(object __instance)
    {
        if (__instance == null)
        {
            return false;
        }

        try
        {
            Type t = __instance.GetType();
            dynamic spkData = AccessTools.Field(t, "playbackDeviceData")?.GetValue(__instance);
            var lbl = AccessTools.Field(t, "lbWirelessmodestatus")?.GetValue(__instance) as System.Windows.FrameworkElement;
            if (spkData != null && lbl != null)
            {
                if ((string)spkData.LatencyLevel == "0")
                {
                    spkData.LatencyLevel = "1";
                    lbl.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty, "FIXED_LATENCY");
                }
                else
                {
                    spkData.LatencyLevel = "0";
                    lbl.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty, "ADAPTIVE_LATENCY");
                }
            }
        }
        catch
        {
        }

        return false;
    }

    /// <summary>
    /// Replaces localized UI label string comparison ("On") with state-based toggling of MicrophoneAutoSleepStatusUI.
    /// </summary>
    public static bool UCMic_UpdateAutoSleepOnPlaybackChange_Prefix(object __instance)
    {
        if (__instance == null)
        {
            return false;
        }

        try
        {
            Type t = __instance.GetType();
            dynamic micVm = AccessTools.Field(t, "microphoneViewModel")?.GetValue(__instance);
            var lbl = AccessTools.Field(t, "lblMicrophoneDeviceAutosleepstatus")?.GetValue(__instance) as System.Windows.FrameworkElement;
            if (micVm != null && lbl != null)
            {
                if ((string)micVm.MicrophoneAutoSleepStatusUI == "1")
                {
                    lbl.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty, "Off");
                    micVm.MicrophoneAutoSleepStatusUI = "0";
                }
                else
                {
                    lbl.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty, "On");
                    micVm.MicrophoneAutoSleepStatusUI = "1";
                }
            }
        }
        catch
        {
        }

        return false;
    }

    /// <summary>
    /// Replaces localized UI label string comparison ("ADAPTIVE LATENCY") with state-based toggling of LatencyLevel.
    /// </summary>
    public static bool UCMic_UpdateLatencyOnPlaybackChange_Prefix(object __instance)
    {
        if (__instance == null)
        {
            return false;
        }

        try
        {
            Type t = __instance.GetType();
            dynamic micData = AccessTools.Field(t, "microphoneDeviceData")?.GetValue(__instance);
            var lbl = AccessTools.Field(t, "lbMicrophoneDeviceWirelessModestatus")?.GetValue(__instance) as System.Windows.FrameworkElement;
            if (micData != null && lbl != null)
            {
                if ((string)micData.LatencyLevel == "0")
                {
                    micData.LatencyLevel = "1";
                    lbl.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty, "FIXED_LATENCY");
                }
                else
                {
                    micData.LatencyLevel = "0";
                    lbl.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty, "ADAPTIVE_LATENCY");
                }
            }
        }
        catch
        {
        }

        return false;
    }

    /// <summary>
    /// Fixes UCMicrophoneDevice.BtnMicrophoneAdaptiveLatency_Click passing IsPlayback: true instead of IsPlayback: false.
    /// </summary>
    public static bool UCMic_BtnMicrophoneAdaptiveLatency_Click_Prefix(object __instance)
    {
        if (__instance is not System.Windows.Threading.DispatcherObject dispObj)
        {
            return false;
        }

        try
        {
            Type t = __instance.GetType();
            dispObj.Dispatcher.BeginInvoke((Action)delegate
            {
                try
                {
                    dynamic micData = AccessTools.Field(t, "microphoneDeviceData")?.GetValue(__instance);
                    dynamic settingsVm = AccessTools.Field(t, "settingsViewModel")?.GetValue(__instance);
                    dynamic devDataVm = AccessTools.Field(t, "deviceDataViewModel")?.GetValue(__instance);
                    var lbl = AccessTools.Field(t, "lbMicrophoneDeviceWirelessModestatus")?.GetValue(__instance) as System.Windows.FrameworkElement;
                    if (micData == null || string.IsNullOrEmpty((string)micData.LatencyLevel))
                    {
                        return;
                    }

                    int latencyType = Convert.ToInt32((string)micData.LatencyLevel);
                    if (latencyType == 0)
                    {
                        micData.LatencyLevel = "1";
                        lbl?.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty, "FIXED_LATENCY");
                        settingsVm?.AdaptiveLatency(1, false);
                    }
                    else
                    {
                        micData.LatencyLevel = "0";
                        lbl?.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty, "ADAPTIVE_LATENCY");
                        settingsVm?.AdaptiveLatency(0, false);
                    }

                    devDataVm?.UpdateLatencyInPlaybackSectionOnMicSectionChange();
                }
                catch
                {
                }
            }, System.Windows.Threading.DispatcherPriority.DataBind);
        }
        catch
        {
        }

        return false;
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
