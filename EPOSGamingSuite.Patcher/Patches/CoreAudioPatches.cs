using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace EPOSGamingSuite.Patcher.Patches;

/// <summary>
/// Runtime patches for Windows 10/11 CoreAudio enumeration, PropVariant 4127 crash prevention,
/// H3PRO Hybrid Wired/Wireless (USB + USB-BT) dual-connection handling, battery updates,
/// wireless dropdown removal, and USB/Dongle VID/PID resolution.
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
    private static FieldInfo _deviceDataField;
    private static FieldInfo _deviceDetailsListField;

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
        _deviceDataField = AccessTools.Field(_deviceDataModelType, "deviceData");
        _deviceDetailsListField = AccessTools.Field(_deviceDataModelType, "deviceDetailsList");

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
                prefix: new HarmonyMethod(typeof(CoreAudioPatches), nameof(SetDefaultDevice_Prefix)));
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

        MethodInfo updateBatteryMethod = AccessTools.Method(_deviceDataModelType, "UpdateBatteryPercentage");
        if (updateBatteryMethod != null)
        {
            harmony.Patch(
                updateBatteryMethod,
                prefix: new HarmonyMethod(typeof(CoreAudioPatches), nameof(UpdateBatteryPercentage_Prefix)));
        }

        MethodInfo removePlaybackMethod = AccessTools.Method(_deviceDataModelType, "RemoveAllPlaybackDevices");
        if (removePlaybackMethod != null)
        {
            harmony.Patch(
                removePlaybackMethod,
                prefix: new HarmonyMethod(typeof(CoreAudioPatches), nameof(RemoveAllPlaybackDevices_Prefix)));
        }

        MethodInfo removeMicMethod = AccessTools.Method(_deviceDataModelType, "RemoveAllMicrophoneDevices");
        if (removeMicMethod != null)
        {
            harmony.Patch(
                removeMicMethod,
                prefix: new HarmonyMethod(typeof(CoreAudioPatches), nameof(RemoveAllMicrophoneDevices_Prefix)));
        }

        MethodInfo removeBothMethod = AccessTools.Method(_deviceDataModelType, "RemovePlaybackAndMicrophoneDevices");
        if (removeBothMethod != null)
        {
            harmony.Patch(
                removeBothMethod,
                prefix: new HarmonyMethod(typeof(CoreAudioPatches), nameof(RemovePlaybackAndMicrophoneDevices_Prefix)));
        }

        MethodInfo updatePlaybackMethod = AccessTools.Method(_deviceDataModelType, "UpdateDeviceInfoForPlayback");
        if (updatePlaybackMethod != null)
        {
            harmony.Patch(
                updatePlaybackMethod,
                prefix: new HarmonyMethod(typeof(CoreAudioPatches), nameof(UpdateDeviceInfoForPlayback_Prefix)));
        }

        MethodInfo updateMicMethod = AccessTools.Method(_deviceDataModelType, "UpdateDeviceInfoForMicrophone");
        if (updateMicMethod != null)
        {
            harmony.Patch(
                updateMicMethod,
                prefix: new HarmonyMethod(typeof(CoreAudioPatches), nameof(UpdateDeviceInfoForMicrophone_Prefix)));
        }

        MethodInfo updateOtherMethod = AccessTools.Method(_deviceDataModelType, "UpdateDeviceInfoForOtherAndNonPairedOtherDevice");
        if (updateOtherMethod != null)
        {
            harmony.Patch(
                updateOtherMethod,
                prefix: new HarmonyMethod(typeof(CoreAudioPatches), nameof(UpdateDeviceInfoForOther_Prefix)));
        }

        MethodInfo updateBtMethod = AccessTools.Method(_deviceDataModelType, "UpdateDeviceInfoForOtherAndNonPairedBTDevice");
        if (updateBtMethod != null)
        {
            harmony.Patch(
                updateBtMethod,
                prefix: new HarmonyMethod(typeof(CoreAudioPatches), nameof(UpdateDeviceInfoForBT_Prefix)));
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
    /// Fixes SetDefaultDevice so that SelectedPlaybackDeviceName and SelectedMicrophoneDeviceName
    /// use deviceDisplayName (e.g. "EPOS H3PRO (WIRED)" vs "EPOS H3PRO") before notifying the UI dropdowns,
    /// and ensures defaultSpeakerDevice / defaultMicrophoneDevice match the active connection.
    /// </summary>
    public static bool SetDefaultDevice_Prefix(object __instance, object[] __args)
    {
        if (__instance == null || __args == null || __args.Length == 0 || __args[0] == null)
        {
            return false;
        }

        try
        {
            dynamic self = __instance;
            IList deviceList = __args[0] as IList;
            if (deviceList == null)
            {
                return false;
            }

            dynamic detailsList = _deviceDetailsListField?.GetValue(__instance);
            dynamic defSpk = detailsList?.defaultSpeaker;
            dynamic defMic = detailsList?.defaultMicrophone;
            self.defaultSpeaker = defSpk;
            self.defaultMicrophone = defMic;

            object matchedSpkObj = null;
            if (!ReferenceEquals((object)defSpk, null))
            {
                string dsVid = Convert.ToString(defSpk.VID);
                string dsPid = Convert.ToString(defSpk.PID);
                string dsSn = Convert.ToString(defSpk.serialNumber);

                for (int i = 0; i < deviceList.Count; i++)
                {
                    dynamic x = deviceList[i];
                    if (((string)x.VID == dsVid && (string)x.PID == dsPid && (string)x.serialNumber == dsSn) ||
                        ((string)x.dongleVid == dsVid && (string)x.donglePid == dsPid))
                    {
                        matchedSpkObj = deviceList[i];
                        break;
                    }
                }
            }

            IList playbackList = self.allConnectedPlaybackDeviceList as IList;
            if ((matchedSpkObj == null || ((dynamic)matchedSpkObj).VID == null) && playbackList != null && playbackList.Count > 0)
            {
                matchedSpkObj = playbackList[0];
            }

            if (matchedSpkObj != null)
            {
                dynamic matchedSpk = matchedSpkObj;
                if ((string)matchedSpk.VID != null)
                {
                    self.defaultSpeakerDevice = matchedSpk;
                    string displayName = (string)matchedSpk.deviceDisplayName;
                    self.SelectedPlaybackDeviceName = !string.IsNullOrEmpty(displayName) ? displayName : (string)matchedSpk.deviceName;
                }
            }

            object matchedMicObj = null;
            if (!ReferenceEquals((object)defMic, null))
            {
                string dmVid = Convert.ToString(defMic.VID);
                string dmPid = Convert.ToString(defMic.PID);
                string dmSn = Convert.ToString(defMic.serialNumber);

                for (int i = 0; i < deviceList.Count; i++)
                {
                    dynamic x = deviceList[i];
                    if (((string)x.VID == dmVid && (string)x.PID == dmPid && (string)x.serialNumber == dmSn) ||
                        ((string)x.dongleVid == dmVid && (string)x.donglePid == dmPid))
                    {
                        matchedMicObj = deviceList[i];
                        break;
                    }
                }
            }

            IList micList = self.allConnectedMicrophoneDeviceList as IList;
            if ((matchedMicObj == null || ((dynamic)matchedMicObj).VID == null) && micList != null && micList.Count > 0)
            {
                matchedMicObj = micList[0];
            }

            if (matchedMicObj != null)
            {
                dynamic matchedMic = matchedMicObj;
                if ((string)matchedMic.VID != null)
                {
                    self.defaultMicrophoneDevice = matchedMic;
                    string displayName = (string)matchedMic.deviceDisplayName;
                    self.SelectedMicrophoneDeviceName = !string.IsNullOrEmpty(displayName) ? displayName : (string)matchedMic.deviceName;
                }
            }

            InvokeEvent(__instance, "EventGetAllPlaybackDeviceNames");
            InvokeEvent(__instance, "EventGetAllMicrophoneDeviceNames");
            InvokeEvent(__instance, "EventGetNonPairedBTDeviceNames");
        }
        catch (Exception ex)
        {
            EposPatchBootstrap.LogPatchStatus("SetDefaultDevice_Prefix warning: " + ex.Message);
        }

        return false;
    }

    /// <summary>
    /// Ensures PlaybackDeviceDongleVID/PID/SerialNumber are cleared when switching to wired USB,
    /// and always updates selectedDevicedetailSpeaker to match the active connection's VID/PID.
    /// </summary>
    public static void PopulatePlaybackDeviceData_Postfix(object __instance, object[] __args)
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

            if (__args != null && __args.Length > 0 && __args[0] != null)
            {
                dynamic details = __args[0];
                string dVid = Convert.ToString(details.dongleVid);
                string dPid = Convert.ToString(details.donglePid);
                string dSn = Convert.ToString(details.dongleSerialNumber);

                if (string.IsNullOrEmpty(dPid) || dPid == "0")
                {
                    spkData.PlaybackDeviceDongleVID = string.Empty;
                    spkData.PlaybackDeviceDonglePID = string.Empty;
                    spkData.PlaybackDeviceDongleSerialNumber = string.Empty;
                }
                else
                {
                    spkData.PlaybackDeviceDongleVID = dVid ?? string.Empty;
                    spkData.PlaybackDeviceDonglePID = dPid;
                    spkData.PlaybackDeviceDongleSerialNumber = dSn ?? string.Empty;
                }
            }

            string devType = (string)spkData.DeviceType;
            string dongleVid = (string)spkData.PlaybackDeviceDongleVID;
            string donglePid = (string)spkData.PlaybackDeviceDonglePID;
            string vidStr = (devType == "USB" || string.IsNullOrEmpty(dongleVid) || dongleVid == "0")
                ? (string)spkData.PlaybackDeviceVID
                : dongleVid;
            string pidStr = (devType == "USB" || string.IsNullOrEmpty(donglePid) || donglePid == "0")
                ? (string)spkData.PlaybackDevicePID
                : donglePid;

            if (short.TryParse(vidStr, out short v) && short.TryParse(pidStr, out short p))
            {
                selSpk.Vid = v;
                selSpk.Pid = p;
                _selectedSpeakerField?.SetValue(__instance, selSpk);
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// Ensures MicrophoneDeviceDongleVID/PID/SerialNumber are cleared when switching to wired USB,
    /// and always updates selectedDevicedetailMic to match the active connection's VID/PID.
    /// </summary>
    public static void PopulateMicrophoneDeviceData_Postfix(object __instance, object[] __args)
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

            if (__args != null && __args.Length > 0 && __args[0] != null)
            {
                dynamic details = __args[0];
                string dVid = Convert.ToString(details.dongleVid);
                string dPid = Convert.ToString(details.donglePid);
                string dSn = Convert.ToString(details.dongleSerialNumber);

                if (string.IsNullOrEmpty(dPid) || dPid == "0")
                {
                    micData.MicrophoneDeviceDongleVID = string.Empty;
                    micData.MicrophoneDeviceDonglePID = string.Empty;
                    micData.MicrophoneDeviceDongleSerialNumber = string.Empty;
                }
                else
                {
                    micData.MicrophoneDeviceDongleVID = dVid ?? string.Empty;
                    micData.MicrophoneDeviceDonglePID = dPid;
                    micData.MicrophoneDeviceDongleSerialNumber = dSn ?? string.Empty;
                }
            }

            string devType = (string)micData.DeviceType;
            string dongleVid = (string)micData.MicrophoneDeviceDongleVID;
            string donglePid = (string)micData.MicrophoneDeviceDonglePID;
            string vidStr = (devType == "USB" || string.IsNullOrEmpty(dongleVid) || dongleVid == "0")
                ? (string)micData.MicrophoneDeviceVID
                : dongleVid;
            string pidStr = (devType == "USB" || string.IsNullOrEmpty(donglePid) || donglePid == "0")
                ? (string)micData.MicrophoneDevicePID
                : donglePid;

            if (short.TryParse(vidStr, out short v) && short.TryParse(pidStr, out short p))
            {
                selMic.Vid = v;
                selMic.Pid = p;
                _selectedMicField?.SetValue(__instance, selMic);
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// Fixes UpdateBatteryPercentage where playbackDeviceData only checked PlaybackDeviceDonglePID (771)
    /// against devicedetail.Pid (257), causing playback battery percentage to never update.
    /// </summary>
    public static bool UpdateBatteryPercentage_Prefix(object __instance, object[] __args)
    {
        if (__instance == null || __args == null || __args.Length == 0 || __args[0] == null)
        {
            return false;
        }

        try
        {
            IList deviceDetails = __args[0] as IList;
            if (deviceDetails == null)
            {
                return false;
            }

            dynamic self = __instance;
            dynamic spkData = _playbackDataField?.GetValue(__instance);
            dynamic micData = _micDataField?.GetValue(__instance);
            dynamic devData = _deviceDataField?.GetValue(__instance);

            for (int i = 0; i < deviceDetails.Count; i++)
            {
                dynamic detail = deviceDetails[i];
                if (detail == null)
                {
                    continue;
                }

                string vid = Convert.ToString(detail.Vid);
                string pid = Convert.ToString(detail.Pid);
                string sn = Convert.ToString(detail.SerialNumber);
                string valStr = Convert.ToString(detail.value);
                if (!int.TryParse(valStr, out int batteryLevel))
                {
                    continue;
                }

                if (spkData != null && (string)spkData.PlaybackDeviceVID == vid &&
                    ((string)spkData.PlaybackDevicePID == pid || (string)spkData.PlaybackDeviceDonglePID == pid) &&
                    (string.IsNullOrEmpty(sn) || (string)spkData.PlaybackDeviceSerialNumber == sn || (string)spkData.PlaybackDeviceDongleSerialNumber == sn))
                {
                    spkData.BatteryLevel = batteryLevel;
                    if (devData != null)
                    {
                        devData.BatteryLevel = batteryLevel;
                    }
                }

                if (micData != null && (string)micData.MicrophoneDeviceVID == vid &&
                    ((string)micData.MicrophoneDevicePID == pid || (string)micData.MicrophoneDeviceDonglePID == pid) &&
                    (string.IsNullOrEmpty(sn) || (string)micData.MicrophoneDeviceSerialNumber == sn || (string)micData.MicrophoneDeviceDongleSerialNumber == sn))
                {
                    micData.BatteryLevel = batteryLevel;
                }
            }

            Action updateAction = () => InvokeEvent(__instance, "EventMainWindowUpdateBatteryPercentage");
            self.DispatcherBeginInvoke(updateAction, System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            EposPatchBootstrap.LogPatchStatus("UpdateBatteryPercentage_Prefix warning: " + ex.Message);
        }

        return false;
    }

    /// <summary>
    /// Fixes RemoveAllPlaybackDevices so wireless (Dongle / USB-BT) devices stored under donglePid/dongleSerialNumber
    /// are properly removed from allConnectedPlaybackDeviceList and allConnectedPlaybackDeviceNames.
    /// </summary>
    public static bool RemoveAllPlaybackDevices_Prefix(object __instance, object[] __args)
    {
        if (__instance != null && __args != null && __args.Length > 0 && __args[0] != null)
        {
            RemoveDeviceFromLists(__instance, __args[0], isPlayback: true, invokeHideEvent: false);
        }
        return false;
    }

    /// <summary>
    /// Fixes RemoveAllMicrophoneDevices so wireless (Dongle / USB-BT) devices stored under donglePid/dongleSerialNumber
    /// are properly removed from allConnectedMicrophoneDeviceList and allConnectedMicrophoneDeviceNames.
    /// </summary>
    public static bool RemoveAllMicrophoneDevices_Prefix(object __instance, object[] __args)
    {
        if (__instance != null && __args != null && __args.Length > 0 && __args[0] != null)
        {
            RemoveDeviceFromLists(__instance, __args[0], isPlayback: false, invokeHideEvent: false);
        }
        return false;
    }

    /// <summary>
    /// Fixes RemovePlaybackAndMicrophoneDevices so wireless (Dongle / USB-BT) devices are properly removed.
    /// </summary>
    public static bool RemovePlaybackAndMicrophoneDevices_Prefix(object __instance, object[] __args)
    {
        if (__instance != null && __args != null && __args.Length > 0 && __args[0] != null)
        {
            RemoveDeviceFromLists(__instance, __args[0], isPlayback: true, invokeHideEvent: true);
            RemoveDeviceFromLists(__instance, __args[0], isPlayback: false, invokeHideEvent: true);
        }
        return false;
    }

    private static void RemoveDeviceFromLists(object instance, object deviceListItemObj, bool isPlayback, bool invokeHideEvent)
    {
        try
        {
            dynamic self = instance;
            dynamic item = deviceListItemObj;
            string vid = Convert.ToString(item.VID);
            string pid = Convert.ToString(item.PID);
            string sn = Convert.ToString(item.serialNumber);
            string dVid = Convert.ToString(item.dongleVid);
            string dPid = Convert.ToString(item.donglePid);
            string dSn = Convert.ToString(item.dongleSerialNumber);

            IList deviceList = (isPlayback ? self.allConnectedPlaybackDeviceList : self.allConnectedMicrophoneDeviceList) as IList;
            IList namesList = (isPlayback ? self.allConnectedPlaybackDeviceNames : self.allConnectedMicrophoneDeviceNames) as IList;

            bool removedAny = false;
            if (namesList != null)
            {
                for (int i = namesList.Count - 1; i >= 0; i--)
                {
                    dynamic x = namesList[i];
                    string xVid = Convert.ToString(x.VID);
                    string xPid = Convert.ToString(x.PID);
                    string xSn = Convert.ToString(x.SerialNumber);
                    string xDevSn = Convert.ToString(x.DeviceSerialNumber);

                    bool matchesUsb = xVid == vid && xPid == pid && (string.IsNullOrEmpty(sn) || xSn == sn);
                    bool matchesDongle = (!string.IsNullOrEmpty(dPid) && dPid != "0" && xVid == dVid && xPid == dPid && (string.IsNullOrEmpty(dSn) || xSn == dSn)) ||
                                         (!string.IsNullOrEmpty(sn) && xDevSn == sn && xVid == vid);

                    if (matchesUsb || matchesDongle)
                    {
                        namesList.RemoveAt(i);
                        removedAny = true;
                    }
                }
            }

            if (deviceList != null)
            {
                for (int i = deviceList.Count - 1; i >= 0; i--)
                {
                    dynamic x = deviceList[i];
                    string xVid = Convert.ToString(x.VID);
                    string xPid = Convert.ToString(x.PID);
                    string xSn = Convert.ToString(x.serialNumber);
                    string xdPid = Convert.ToString(x.donglePid);
                    string xdSn = Convert.ToString(x.dongleSerialNumber);

                    bool matchesUsb = xVid == vid && xPid == pid && (string.IsNullOrEmpty(sn) || xSn == sn);
                    bool matchesDongle = (!string.IsNullOrEmpty(dPid) && dPid != "0" && xdPid == dPid && (string.IsNullOrEmpty(dSn) || xdSn == dSn)) ||
                                         (xVid == vid && xdPid == pid && (string.IsNullOrEmpty(sn) || xdSn == sn));

                    if (matchesUsb || matchesDongle)
                    {
                        deviceList.RemoveAt(i);
                        removedAny = true;
                    }
                }
            }

            if (removedAny && invokeHideEvent)
            {
                InvokeEvent(instance, isPlayback ? "EventPlaybackHideDeviceDetails" : "EventMicrophoneHideDeviceDetails");
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// Fixes original copy-paste bug in UpdateDeviceInfoForPlayback where updated playback device info
    /// was added to allConnectedMicrophoneDeviceList instead of allConnectedPlaybackDeviceList,
    /// and replaces .First() with safe iteration to prevent InvalidOperationException.
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
            object matchedItemObj = null;
            for (int i = 0; i < playbackList.Count; i++)
            {
                dynamic candidate = playbackList[i];
                if ((string)candidate.VID == targetVid &&
                    ((string)candidate.PID == targetPid || (string)candidate.donglePid == targetPid) &&
                    ((string)candidate.serialNumber == targetSn || (string)candidate.dongleSerialNumber == targetSn))
                {
                    matchIndex = i;
                    matchedItemObj = playbackList[i];
                    break;
                }
            }

            if (matchIndex >= 0 && matchedItemObj != null)
            {
                dynamic matchedItem = matchedItemObj;
                if (!string.IsNullOrEmpty((string)matchedItem.VID))
                {
                    playbackList.RemoveAt(matchIndex);
                    matchedItem.newfirmwareVersion = versionInfo.newfirmwareVersion;
                    matchedItem.newdongleFirmwareVersion = versionInfo.newdongleFirmwareVersion;
                    playbackList.Add(matchedItem);

                    dynamic spkData = _playbackDataField?.GetValue(__instance);
                    if (spkData != null &&
                        targetVid == (string)spkData.PlaybackDeviceVID &&
                        (targetPid == (string)spkData.PlaybackDevicePID || targetPid == (string)spkData.PlaybackDeviceDonglePID))
                    {
                        self.PopulatePlaybackDeviceData(matchedItem);
                        InvokeEvent(__instance, "PlaybackdeviceFWAvailable");
                    }
                }
            }
        }
        catch
        {
        }

        return false;
    }

    public static bool UpdateDeviceInfoForMicrophone_Prefix(object __instance, object[] __args)
    {
        if (__instance == null || __args == null || __args.Length == 0 || __args[0] == null)
        {
            return false;
        }

        try
        {
            dynamic self = __instance;
            dynamic versionInfo = __args[0];
            IList micList = self.allConnectedMicrophoneDeviceList as IList;
            if (micList == null || micList.Count <= 0)
            {
                return false;
            }

            string targetVid = Convert.ToString(versionInfo.VID);
            string targetPid = Convert.ToString(versionInfo.PID);
            string targetSn = Convert.ToString(versionInfo.serialNumber);

            int matchIndex = -1;
            object matchedItemObj = null;
            for (int i = 0; i < micList.Count; i++)
            {
                dynamic candidate = micList[i];
                if ((string)candidate.VID == targetVid &&
                    ((string)candidate.PID == targetPid || (string)candidate.donglePid == targetPid) &&
                    ((string)candidate.serialNumber == targetSn || (string)candidate.dongleSerialNumber == targetSn))
                {
                    matchIndex = i;
                    matchedItemObj = micList[i];
                    break;
                }
            }

            if (matchIndex >= 0 && matchedItemObj != null)
            {
                dynamic matchedItem = matchedItemObj;
                if (!string.IsNullOrEmpty((string)matchedItem.VID))
                {
                    micList.RemoveAt(matchIndex);
                    matchedItem.newfirmwareVersion = versionInfo.newfirmwareVersion;
                    matchedItem.newdongleFirmwareVersion = versionInfo.newdongleFirmwareVersion;
                    micList.Add(matchedItem);

                    dynamic micData = _micDataField?.GetValue(__instance);
                    if (micData != null &&
                        targetVid == (string)micData.MicrophoneDeviceVID &&
                        (targetPid == (string)micData.MicrophoneDevicePID || targetPid == (string)micData.MicrophoneDeviceDonglePID))
                    {
                        self.PopulateMicrophoneDeviceData(matchedItem);
                        InvokeEvent(__instance, "MicrophonedeviceFWAvailable");
                    }
                }
            }
        }
        catch
        {
        }

        return false;
    }

    public static bool UpdateDeviceInfoForOther_Prefix(object __instance, object[] __args)
    {
        if (__instance == null || __args == null || __args.Length == 0 || __args[0] == null)
        {
            return false;
        }

        try
        {
            dynamic self = __instance;
            dynamic versionInfo = __args[0];
            IList otherList = self.allConnectedOtherDeviceList as IList;
            if (otherList == null || otherList.Count <= 0)
            {
                return false;
            }

            string targetVid = Convert.ToString(versionInfo.VID);
            string targetPid = Convert.ToString(versionInfo.PID);
            string targetSn = Convert.ToString(versionInfo.serialNumber);

            int matchIndex = -1;
            object matchedItemObj = null;
            for (int i = 0; i < otherList.Count; i++)
            {
                dynamic candidate = otherList[i];
                if ((string)candidate.VID == targetVid && (string)candidate.PID == targetPid && (string)candidate.serialNumber == targetSn)
                {
                    matchIndex = i;
                    matchedItemObj = otherList[i];
                    break;
                }
            }

            if (matchIndex >= 0 && matchedItemObj != null)
            {
                dynamic matchedItem = matchedItemObj;
                if (!string.IsNullOrEmpty((string)matchedItem.VID))
                {
                    otherList.RemoveAt(matchIndex);
                    matchedItem.newfirmwareVersion = versionInfo.newfirmwareVersion;
                    otherList.Add(matchedItem);

                    if (targetVid == (string)self.OtherDeviceVID && targetPid == (string)self.OtherDevicePID && targetSn == (string)self.OtherDeviceSerialNumber)
                    {
                        self.PopulateOtherDeviceData(matchedItem);
                        InvokeEvent(__instance, "OtherDeviceFWAvailable");
                    }
                }
            }
        }
        catch
        {
        }

        return false;
    }

    public static bool UpdateDeviceInfoForBT_Prefix(object __instance, object[] __args)
    {
        if (__instance == null || __args == null || __args.Length == 0 || __args[0] == null)
        {
            return false;
        }

        try
        {
            dynamic self = __instance;
            dynamic versionInfo = __args[0];
            IList btList = self.allConnectedNonPairedBTDeviceList as IList;
            if (btList == null || btList.Count <= 0)
            {
                return false;
            }

            string targetVid = Convert.ToString(versionInfo.VID);
            string targetPid = Convert.ToString(versionInfo.PID);
            string targetSn = Convert.ToString(versionInfo.serialNumber);

            int matchIndex = -1;
            object matchedItemObj = null;
            for (int i = 0; i < btList.Count; i++)
            {
                dynamic candidate = btList[i];
                if ((string)candidate.VID == targetVid && (string)candidate.PID == targetPid && (string)candidate.serialNumber == targetSn)
                {
                    matchIndex = i;
                    matchedItemObj = btList[i];
                    break;
                }
            }

            if (matchIndex >= 0 && matchedItemObj != null)
            {
                dynamic matchedItem = matchedItemObj;
                if (!string.IsNullOrEmpty((string)matchedItem.VID))
                {
                    btList.RemoveAt(matchIndex);
                    matchedItem.newfirmwareVersion = versionInfo.newfirmwareVersion;
                    matchedItem.newdongleFirmwareVersion = versionInfo.newdongleFirmwareVersion;
                    btList.Add(matchedItem);

                    if (targetVid == (string)self.NonPairedDongleDeviceVID && targetPid == (string)self.NonPairedDongleDevicePID && targetSn == (string)self.NonPairedDongleDeviceSerialNumber)
                    {
                        self.PopulateNonPairedDongleDeviceData(matchedItem);
                        InvokeEvent(__instance, "NonPairedDeviceFWAvailable");
                    }
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static void InvokeEvent(object instance, string eventFieldName)
    {
        try
        {
            FieldInfo evtField = AccessTools.Field(_deviceDataModelType, eventFieldName);
            if (evtField?.GetValue(instance) is Delegate del)
            {
                del.DynamicInvoke();
            }
        }
        catch
        {
        }
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
