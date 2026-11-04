using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;

namespace EPOSGamingSuite.Patcher.Patches;

/// <summary>
/// Eliminates the 12-second startup delay caused by EPOSGamingSuiteService timing out on dead EPOS cloud servers
/// (WinHTTP Error 12180) and suppresses transient DEVICE_DISCONNECTED events while an EPOS audio endpoint is active.
/// </summary>
internal static class FastConnectPatches
{
    private const int EposVendorId = 5013; // 0x1395
    private const string EndpointPathFormatGuid = "b3f8fa53-0004-438e-9003-51a46e139bfc";

    private static Type _deviceDataModelType;
    private static Type _mmEnumeratorType;
    private static FieldInfo _deviceDataField;
    private static MethodInfo _displayConnectedMethod;

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

        _deviceDataField = AccessTools.Field(_deviceDataModelType, "deviceData");
        _displayConnectedMethod = AccessTools.Method(_deviceDataModelType, "DisplayMessageDeviceConnected");
        _mmEnumeratorType = AccessTools.TypeByName("NAudio.CoreAudioApi.MMDeviceEnumerator");

        MethodInfo sendMsgMethod = AccessTools.Method(_deviceDataModelType, "pipeServer_SendMessage", new[] { typeof(Enum) });
        if (sendMsgMethod != null)
        {
            harmony.Patch(
                sendMsgMethod,
                postfix: new HarmonyMethod(typeof(FastConnectPatches), nameof(PipeServerSendMessage_Postfix)));
        }

        if (_displayConnectedMethod != null)
        {
            harmony.Patch(
                _displayConnectedMethod,
                prefix: new HarmonyMethod(typeof(FastConnectPatches), nameof(DisplayMessageDeviceConnected_Prefix)));
        }

        MethodInfo displayDisconnectedMethod = AccessTools.Method(_deviceDataModelType, "DisplayMessageDeviceDiconnected");
        if (displayDisconnectedMethod != null)
        {
            harmony.Patch(
                displayDisconnectedMethod,
                prefix: new HarmonyMethod(typeof(FastConnectPatches), nameof(DisplayMessageDeviceDisconnected_Prefix)));
        }
    }

    public static void PipeServerSendMessage_Postfix(object __instance, Enum @enum)
    {
        if (__instance == null || @enum == null)
        {
            return;
        }

        string enumName = @enum.ToString();
        if (enumName == "REQ_REFRESH_CONNECTED_DEVICE" || enumName == "REQ_CONNECTED_DEVICE")
        {
            if (!IsDeviceCurrentlyConnected(__instance))
            {
                Task.Run(() => TryFastConnect(__instance));
            }
        }
    }

    public static void DisplayMessageDeviceConnected_Prefix(string deviceDetails)
    {
        if (!string.IsNullOrWhiteSpace(deviceDetails) && deviceDetails.Contains("\"DeviceList\""))
        {
            try
            {
                File.WriteAllText(GetCacheFilePath(), deviceDetails);
            }
            catch
            {
            }
        }
    }

    public static bool DisplayMessageDeviceDisconnected_Prefix(object __instance)
    {
        if (HasActiveEposEndpointInWindows(out int pid))
        {
            EposPatchBootstrap.LogPatchStatus($"Suppressed transient DEVICE_DISCONNECTED while EPOS audio endpoint (PID {pid}) is active in Windows.");
            if (!IsDeviceCurrentlyConnected(__instance))
            {
                Task.Run(() => TryFastConnect(__instance));
            }
            return false;
        }
        return true;
    }

    private static void TryFastConnect(object deviceDataModelInstance)
    {
        try
        {
            if (IsDeviceCurrentlyConnected(deviceDataModelInstance) || !HasActiveEposEndpointInWindows(out int activePid))
            {
                return;
            }

            string jsonPayload = null;
            string cachePath = GetCacheFilePath();
            if (File.Exists(cachePath))
            {
                string cached = File.ReadAllText(cachePath);
                if (!string.IsNullOrWhiteSpace(cached) && cached.Contains("\"DeviceList\""))
                {
                    jsonPayload = cached;
                }
            }

            if (string.IsNullOrEmpty(jsonPayload) && (activePid == 771 || activePid == 257 || activePid == 263))
            {
                jsonPayload = BuildDefaultH3ProPayload(activePid);
            }

            if (!string.IsNullOrEmpty(jsonPayload) && !IsDeviceCurrentlyConnected(deviceDataModelInstance))
            {
                EposPatchBootstrap.LogPatchStatus($"Fast-connecting active EPOS audio endpoint (PID {activePid}) via Windows CoreAudio.");
                _displayConnectedMethod?.Invoke(deviceDataModelInstance, new object[] { jsonPayload });
            }
        }
        catch (Exception ex)
        {
            EposPatchBootstrap.LogPatchStatus("TryFastConnect error: " + ex.Message);
        }
    }

    private static bool IsDeviceCurrentlyConnected(object deviceDataModelInstance)
    {
        try
        {
            object devData = _deviceDataField?.GetValue(deviceDataModelInstance);
            if (devData != null)
            {
                dynamic d = devData;
                return (bool)d.DeviceConnected;
            }
        }
        catch
        {
        }
        return false;
    }

    private static bool HasActiveEposEndpointInWindows(out int activePid)
    {
        activePid = 0;
        try
        {
            if (_mmEnumeratorType == null)
            {
                _mmEnumeratorType = AccessTools.TypeByName("NAudio.CoreAudioApi.MMDeviceEnumerator");
            }
            if (_mmEnumeratorType == null)
            {
                return false;
            }

            dynamic enumerator = Activator.CreateInstance(_mmEnumeratorType);
            Type dataFlowType = AccessTools.TypeByName("NAudio.CoreAudioApi.DataFlow");
            Type deviceStateType = AccessTools.TypeByName("NAudio.CoreAudioApi.DeviceState");
            object dataFlowAll = Enum.ToObject(dataFlowType, 2); // DataFlow.All
            object stateActive = Enum.ToObject(deviceStateType, 1); // DeviceState.Active

            MethodInfo enumMethod = _mmEnumeratorType.GetMethod("EnumerateAudioEndPoints");
            dynamic endpoints = enumMethod.Invoke(enumerator, new[] { dataFlowAll, stateActive });
            int count = (int)endpoints.Count;

            for (int i = 0; i < count; i++)
            {
                dynamic dev = endpoints[i];
                dynamic props = dev.Properties;
                int propCount = (int)props.Count;

                for (int j = 0; j < propCount; j++)
                {
                    dynamic key = props.Get(j);
                    if (!string.Equals(key.formatId.ToString(), EndpointPathFormatGuid, StringComparison.OrdinalIgnoreCase) || (int)key.propertyId != 2)
                    {
                        continue;
                    }

                    string pathVal;
                    try
                    {
                        pathVal = props.GetValue(j).Value?.ToString();
                    }
                    catch
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(pathVal) && pathVal.Contains("VID_") && pathVal.Contains("PID_"))
                    {
                        int vid = CoreAudioPatches.ParseHexId(pathVal, "VID_");
                        int pid = CoreAudioPatches.ParseHexId(pathVal, "PID_");
                        if (vid == EposVendorId)
                        {
                            activePid = pid;
                            return true;
                        }
                    }
                }
            }
        }
        catch
        {
        }
        return false;
    }

    private static string GetCacheFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "EPOS",
            "Gaming Suite",
            "Config",
            "LastConnectedDeviceCache.json");
    }

    private static string BuildDefaultH3ProPayload(int activePid)
    {
        bool isDongle = activePid == 771 || activePid == 263;
        string hsConn = isDongle ? "Dongle" : "USB";
        string devTypeStr = isDongle ? "BT" : "USB";
        int donglePid = isDongle ? activePid : 0;
        int dongleVid = isDongle ? EposVendorId : 0;

        return "{\"DeviceList\":[{\"AutoSleepStatus\":\"1\",\"DeviceType\":\"" + devTypeStr +
               "\",\"HSConnection\":\"" + hsConn +
               "\",\"SideToneGain\":\"0\",\"Smartbutton\":\"1\",\"batterypercentage\":100,\"deviceDisplayName\":\"EPOS H3PRO\",\"deviceName\":\"EPOS H3PRO\",\"deviceType\":3,\"directSoundEnabled\":1,\"dongleFirmwareVersion\":\"3.12.1\",\"donglePid\":" + donglePid +
               ",\"dongleSerialNumber\":\"EPOS_DONGLE\",\"dongleVid\":" + dongleVid +
               ",\"firmwareVersion\":\"1.3.5\",\"frcAvailability\":\"\",\"hpfAvailability\":\"\",\"latencyLevel\":\"-1\",\"light3DAvailability\":\"\",\"manufacturer\":\"Sennheiser\",\"micDirection\":\"\",\"micEqEnabled\":0,\"micEqLevels\":[0,0,0,0,0,0,0,0,0],\"micFrcEnabled\":0,\"micFrcPreset\":0,\"micHpfEnabled\":0,\"micMetersEnabled\":0,\"micNrEnabled\":0,\"micNrPreset\":0,\"newdongleFirmwareVersion\":\"\",\"newfirmwareVersion\":\"\",\"noiseGateEnabled\":0,\"noiseGateLevel\":0.5,\"noiseReductionLevel\":\"1\",\"nrAvailability\":\"\",\"pid\":257,\"reverbEnabled\":0,\"reverbLevel\":0,\"serialNumber\":\"0123456789ABCDEF\",\"speakerEqEnabled\":1,\"speakerEqLevels\":[0,0,0,0,0,0,0,0,0],\"vid\":5013,\"vpLanguage\":\"\",\"vplanguagelist\":[]}],\"VersionMismatch\":0,\"defaultMicrophone\":{\"pid\":257,\"serialNumber\":\"0123456789ABCDEF\",\"vid\":5013},\"defaultSpeaker\":{\"pid\":257,\"serialNumber\":\"0123456789ABCDEF\",\"vid\":5013},\"messageType\":\"4\"}";
    }
}
