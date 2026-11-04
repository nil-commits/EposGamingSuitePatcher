using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace EPOSGamingSuite.Patcher.Patches;

/// <summary>
/// Runtime patches for Windows 10/11 CoreAudio enumeration, PropVariant 4127 crash prevention,
/// and USB/Dongle VID/PID resolution.
/// </summary>
internal static class CoreAudioPatches
{
    private const string EndpointPathFormatGuid = "b3f8fa53-0004-438e-9003-51a46e139bfc";
    private const string FormFactorFormatGuid = "1da5d803-d492-4edd-8c23-e0c0ffee7f0e";

    private static Type _deviceDataModelType;
    private static FieldInfo _selectedSpeakerField;
    private static FieldInfo _selectedMicField;
    private static FieldInfo _playbackDataField;
    private static FieldInfo _micDataField;

    public static void Apply(Harmony harmony, Assembly servicesAsm)
    {
        if (servicesAsm == null)
        {
            return;
        }

        _deviceDataModelType = servicesAsm.GetType("Sennheiser.Gs.UI.Services.Model.DeviceDataModel");
        if (_deviceDataModelType == null)
        {
            return;
        }

        _selectedSpeakerField = AccessTools.Field(_deviceDataModelType, "selectedDevicedetailSpeaker");
        _selectedMicField = AccessTools.Field(_deviceDataModelType, "selectedDevicedetailMic");
        _playbackDataField = AccessTools.Field(_deviceDataModelType, "playbackDeviceData");
        _micDataField = AccessTools.Field(_deviceDataModelType, "microphoneDeviceData");

        MethodInfo searchMethod = AccessTools.Method(_deviceDataModelType, "SearchSelectedDevice");
        if (searchMethod != null)
        {
            harmony.Patch(
                searchMethod,
                prefix: new HarmonyMethod(typeof(CoreAudioPatches), nameof(SearchSelectedDevice_Prefix)));
        }

        MethodInfo setDefaultMethod = AccessTools.Method(_deviceDataModelType, "SetDefaultDevice");
        if (setDefaultMethod != null)
        {
            harmony.Patch(
                setDefaultMethod,
                postfix: new HarmonyMethod(typeof(CoreAudioPatches), nameof(SetDefaultDevice_Postfix)));
        }

        MethodInfo popPlaybackMethod = AccessTools.Method(_deviceDataModelType, "PopulatePlaybackDeviceData");
        if (popPlaybackMethod != null)
        {
            harmony.Patch(
                popPlaybackMethod,
                postfix: new HarmonyMethod(typeof(CoreAudioPatches), nameof(PopulatePlaybackDeviceData_Postfix)));
        }

        MethodInfo popMicMethod = AccessTools.Method(_deviceDataModelType, "PopulateMicrophoneDeviceData");
        if (popMicMethod != null)
        {
            harmony.Patch(
                popMicMethod,
                postfix: new HarmonyMethod(typeof(CoreAudioPatches), nameof(PopulateMicrophoneDeviceData_Postfix)));
        }

        MethodInfo updatePlaybackMethod = AccessTools.Method(_deviceDataModelType, "UpdateDeviceInfoForPlayback");
        if (updatePlaybackMethod != null)
        {
            harmony.Patch(
                updatePlaybackMethod,
                prefix: new HarmonyMethod(typeof(CoreAudioPatches), nameof(UpdateDeviceInfoForPlayback_Prefix)));
        }
    }

    /// <summary>
    /// Replaces SearchSelectedDevice to check PropertyKey GUIDs BEFORE reading PropVariant.Value,
    /// preventing NotSupportedException (PropVariant 4127 / VT_VECTOR | VT_LPWSTR) on Windows 10/11.
    /// </summary>
    public static bool SearchSelectedDevice_Prefix(object Device, object selectedDevice, int dataFlow, ref object __result)
    {
        __result = null;
        if (Device == null || selectedDevice == null)
        {
            return false;
        }

        try
        {
            dynamic dev = Device;
            dynamic sel = selectedDevice;
            int targetVid = (int)sel.Vid;
            int targetPid = (int)sel.Pid;
            if (targetVid == 0 && targetPid == 0)
            {
                return false;
            }

            dynamic props = dev.Properties;
            int count = (int)props.Count;

            for (int i = 0; i < count; i++)
            {
                dynamic key = props.Get(i);
                string formatId = key.formatId.ToString();
                int propId = (int)key.propertyId;

                if (!string.Equals(formatId, EndpointPathFormatGuid, StringComparison.OrdinalIgnoreCase) || propId != 2)
                {
                    continue;
                }

                string devicePath;
                try
                {
                    object rawVal = props.GetValue(i).Value;
                    devicePath = rawVal?.ToString();
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrEmpty(devicePath) || !devicePath.Contains("VID_") || !devicePath.Contains("PID_"))
                {
                    continue;
                }

                if (ParseHexId(devicePath, "VID_") != targetVid || ParseHexId(devicePath, "PID_") != targetPid)
                {
                    continue;
                }

                bool matchesFlow = dataFlow == 1; // DataFlow.Capture == 1
                if (dataFlow == 0) // DataFlow.Render == 0
                {
                    bool foundFormFactor = false;
                    for (int j = 0; j < count; j++)
                    {
                        dynamic key2 = props.Get(j);
                        if (string.Equals(key2.formatId.ToString(), FormFactorFormatGuid, StringComparison.OrdinalIgnoreCase) && (int)key2.propertyId == 0)
                        {
                            try
                            {
                                string ff = props.GetValue(j).Value?.ToString();
                                foundFormFactor = true;
                                if (ff == "1" || ff == "3" || ff == "5")
                                {
                                    matchesFlow = true;
                                    break;
                                }
                            }
                            catch
                            {
                            }
                        }
                    }

                    if (!foundFormFactor)
                    {
                        matchesFlow = true;
                    }
                }

                if (matchesFlow)
                {
                    __result = Device;
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            EposPatchBootstrap.LogPatchStatus("SearchSelectedDevice_Prefix warning: " + ex.Message);
        }

        return false;
    }

    /// <summary>
    /// Ensures defaultSpeakerDevice and defaultMicrophoneDevice fall back to the first connected EPOS playback/mic
    /// device when the service reports a dongle/headset PID mismatch.
    /// </summary>
    public static void SetDefaultDevice_Postfix(object __instance)
    {
        if (__instance == null)
        {
            return;
        }

        try
        {
            dynamic self = __instance;
            dynamic spkDev = self.defaultSpeakerDevice;
            IList playbackList = self.allConnectedPlaybackDeviceList as IList;
            if (spkDev.VID == null && playbackList != null && playbackList.Count > 0)
            {
                dynamic firstSpk = playbackList[0];
                self.defaultSpeakerDevice = firstSpk;
                if (firstSpk.VID != null)
                {
                    self.SelectedPlaybackDeviceName = (string)firstSpk.deviceName;
                }
            }

            dynamic micDev = self.defaultMicrophoneDevice;
            IList micList = self.allConnectedMicrophoneDeviceList as IList;
            if (micDev.VID == null && micList != null && micList.Count > 0)
            {
                dynamic firstMic = micList[0];
                self.defaultMicrophoneDevice = firstMic;
                if (firstMic.VID != null)
                {
                    self.SelectedMicrophoneDeviceName = (string)firstMic.deviceName;
                }
            }
        }
        catch
        {
        }
    }

    public static void PopulatePlaybackDeviceData_Postfix(object __instance)
    {
        try
        {
            object spkDataObj = _playbackDataField?.GetValue(__instance);
            object selSpkObj = _selectedSpeakerField?.GetValue(__instance);
            if (spkDataObj == null || selSpkObj == null)
            {
                return;
            }

            dynamic spkData = spkDataObj;
            dynamic selSpk = selSpkObj;
            if ((int)selSpk.Vid == 0)
            {
                string devType = (string)spkData.DeviceType;
                string dongleVid = (string)spkData.PlaybackDeviceDongleVID;
                string donglePid = (string)spkData.PlaybackDeviceDonglePID;
                string vidStr = (devType == "USB" || string.IsNullOrEmpty(dongleVid) || dongleVid == "0")
                    ? (string)spkData.PlaybackDeviceVID
                    : dongleVid;
                string pidStr = (devType == "USB" || string.IsNullOrEmpty(donglePid) || donglePid == "0")
                    ? (string)spkData.PlaybackDevicePID
                    : donglePid;

                if (int.TryParse(vidStr, out int v) && int.TryParse(pidStr, out int p))
                {
                    selSpk.Vid = v;
                    selSpk.Pid = p;
                }
            }
        }
        catch
        {
        }
    }

    public static void PopulateMicrophoneDeviceData_Postfix(object __instance)
    {
        try
        {
            object micDataObj = _micDataField?.GetValue(__instance);
            object selMicObj = _selectedMicField?.GetValue(__instance);
            if (micDataObj == null || selMicObj == null)
            {
                return;
            }

            dynamic micData = micDataObj;
            dynamic selMic = selMicObj;
            if ((int)selMic.Vid == 0)
            {
                string devType = (string)micData.DeviceType;
                string dongleVid = (string)micData.MicrophoneDeviceDongleVID;
                string donglePid = (string)micData.MicrophoneDeviceDonglePID;
                string vidStr = (devType == "USB" || string.IsNullOrEmpty(dongleVid) || dongleVid == "0")
                    ? (string)micData.MicrophoneDeviceVID
                    : dongleVid;
                string pidStr = (devType == "USB" || string.IsNullOrEmpty(donglePid) || donglePid == "0")
                    ? (string)micData.MicrophoneDevicePID
                    : donglePid;

                if (int.TryParse(vidStr, out int v) && int.TryParse(pidStr, out int p))
                {
                    selMic.Vid = v;
                    selMic.Pid = p;
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// Fixes original copy-paste bug in UpdateDeviceInfoForPlayback where updated playback device info
    /// was added to allConnectedMicrophoneDeviceList instead of allConnectedPlaybackDeviceList.
    /// </summary>
    public static bool UpdateDeviceInfoForPlayback_Prefix(object __instance, object[] __args)
    {
        if (__instance == null || __args == null || __args.Length == 0 || __args[0] == null)
        {
            return false;
        }

        try
        {
            dynamic self = __instance;
            dynamic versionInfo = __args[0];
            IList playbackList = self.allConnectedPlaybackDeviceList as IList;
            if (playbackList == null || playbackList.Count <= 0)
            {
                return false;
            }

            string targetVid = Convert.ToString(versionInfo.VID);
            string targetPid = Convert.ToString(versionInfo.PID);
            string targetSn = Convert.ToString(versionInfo.serialNumber);

            int matchIndex = -1;
            dynamic matchedItem = null;
            for (int i = 0; i < playbackList.Count; i++)
            {
                dynamic candidate = playbackList[i];
                if ((string)candidate.VID == targetVid && (string)candidate.PID == targetPid && (string)candidate.serialNumber == targetSn)
                {
                    matchIndex = i;
                    matchedItem = candidate;
                    break;
                }
            }

            if (matchIndex >= 0 && matchedItem != null && !string.IsNullOrEmpty((string)matchedItem.VID))
            {
                playbackList.RemoveAt(matchIndex);
                matchedItem.newfirmwareVersion = versionInfo.newfirmwareVersion;
                matchedItem.newdongleFirmwareVersion = versionInfo.newdongleFirmwareVersion;
                playbackList.Add(matchedItem); // Fixed: added to allConnectedPlaybackDeviceList

                dynamic spkData = _playbackDataField?.GetValue(__instance);
                if (spkData != null &&
                    targetVid == (string)spkData.PlaybackDeviceVID &&
                    (targetPid == (string)spkData.PlaybackDevicePID || targetPid == (string)spkData.PlaybackDeviceDonglePID))
                {
                    self.PopulatePlaybackDeviceData(matchedItem);
                }
            }
        }
        catch
        {
        }

        return false;
    }

    internal static int ParseHexId(string devicePath, string prefix)
    {
        int idx = devicePath.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0 || idx + prefix.Length + 4 > devicePath.Length)
        {
            return 0;
        }
        return Convert.ToInt32(devicePath.Substring(idx + prefix.Length, 4), 16);
    }
}
