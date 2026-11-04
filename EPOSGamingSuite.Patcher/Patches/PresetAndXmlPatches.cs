using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using HarmonyLib;

namespace EPOSGamingSuite.Patcher.Patches;

/// <summary>
/// Fixes XML preset handling bugs:
/// - Prevents GetCommonFilePathPreset from double-wrapping paths that are already full .xml paths
///   (fixes PresetDataModel.GetPresetFromSettings line 258 bug).
/// - Enforces CultureInfo.InvariantCulture when parsing EQ, Reverb, and NoiseGate float values
///   in PopulateValues00, PopulatePreset10, and PopulatePreset20 (fixes locale bug on German Windows).
/// - Fixes FileStream handle leak in XmlDoc.CreateNewFile.
/// </summary>
internal static class PresetAndXmlPatches
{
    public static void Apply(Harmony harmony, Assembly servicesAsm)
    {
        if (servicesAsm == null)
        {
            return;
        }

        Type presetModelType = servicesAsm.GetType("Sennheiser.Gs.UI.Services.Model.PresetDataModel");
        if (presetModelType != null)
        {
            MethodInfo getCommonPathMethod = AccessTools.Method(presetModelType, "GetCommonFilePathPreset");
            if (getCommonPathMethod != null)
            {
                harmony.Patch(
                    getCommonPathMethod,
                    prefix: new HarmonyMethod(typeof(PresetAndXmlPatches), nameof(GetCommonFilePathPreset_Prefix)));
            }

            MethodInfo pop00Method = AccessTools.Method(presetModelType, "PopulateValues00");
            if (pop00Method != null)
            {
                harmony.Patch(
                    pop00Method,
                    prefix: new HarmonyMethod(typeof(PresetAndXmlPatches), nameof(PopulateValues00_Prefix)));
            }

            MethodInfo pop10Method = AccessTools.Method(presetModelType, "PopulatePreset10");
            if (pop10Method != null)
            {
                harmony.Patch(
                    pop10Method,
                    prefix: new HarmonyMethod(typeof(PresetAndXmlPatches), nameof(PopulatePreset10_Prefix)));
            }

            MethodInfo pop20Method = AccessTools.Method(presetModelType, "PopulatePreset20");
            if (pop20Method != null)
            {
                harmony.Patch(
                    pop20Method,
                    prefix: new HarmonyMethod(typeof(PresetAndXmlPatches), nameof(PopulatePreset20_Prefix)));
            }
        }

        Type xmlDocType = servicesAsm.GetType("Sennheiser.Gs.UI.Services.Utils.XmlDoc");
        if (xmlDocType != null)
        {
            MethodInfo createNewFileMethod = AccessTools.Method(xmlDocType, "CreateNewFile");
            if (createNewFileMethod != null)
            {
                harmony.Patch(
                    createNewFileMethod,
                    prefix: new HarmonyMethod(typeof(PresetAndXmlPatches), nameof(XmlDocCreateNewFile_Prefix)));
            }
        }
    }

    public static bool GetCommonFilePathPreset_Prefix(string partialFileName, int Custom, ref string __result)
    {
        if (!string.IsNullOrEmpty(partialFileName) &&
            partialFileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
            Path.IsPathRooted(partialFileName))
        {
            __result = partialFileName;
            return false;
        }
        return true;
    }

    public static bool PopulateValues00_Prefix(object __instance, XElement level2Element)
    {
        if (__instance == null || level2Element == null)
        {
            return false;
        }

        try
        {
            string raw = level2Element.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            dynamic self = __instance;
            switch (level2Element.Name.LocalName)
            {
                case "EQBand1":
                    self.SpeakerEq1Value = Math.Round(ParseInvariantDouble(raw));
                    break;
                case "EQBand2":
                    self.SpeakerEq2Value = Math.Round(ParseInvariantDouble(raw));
                    break;
                case "EQBand3":
                    self.SpeakerEq3Value = Math.Round(ParseInvariantDouble(raw));
                    break;
                case "EQBand4":
                    self.SpeakerEq4Value = Math.Round(ParseInvariantDouble(raw));
                    break;
                case "EQBand5":
                    self.SpeakerEq5Value = Math.Round(ParseInvariantDouble(raw));
                    break;
                case "ReverbKnob":
                    self.ReverbValue = ParseInvariantDouble(raw);
                    break;
                case "Audio3D":
                    self.Audio3DValue = ParseInvariantByte(raw);
                    break;
                case "ECOMode":
                    self.ECOModeValue = ParseInvariantByte(raw);
                    break;
                case "micEQ":
                    self.micEQValue = ParseInvariantByte(raw);
                    break;
                case "NoiseGate":
                    self.NoiseGateValue = (double)ParseInvariantByte(raw);
                    break;
                case "NoiseCancellation":
                    self.NoiseCancellationValue = ParseInvariantByte(raw);
                    break;
            }
        }
        catch
        {
        }

        return false;
    }

    public static bool PopulatePreset10_Prefix(object __instance, XElement selectedPresetXML)
    {
        if (__instance == null || selectedPresetXML == null)
        {
            return false;
        }

        try
        {
            dynamic self = __instance;
            foreach (XElement slider in selectedPresetXML.Elements("Slider"))
            {
                foreach (XElement item in slider.Elements())
                {
                    PopulateValues00_Prefix(__instance, item);
                    string raw = item.Attribute("name")?.Value;
                    if (string.IsNullOrEmpty(raw))
                    {
                        continue;
                    }

                    switch (item.Name.LocalName)
                    {
                        case "EQBand6":
                            self.SpeakerEq6Value = Math.Round(ParseInvariantDouble(raw));
                            break;
                        case "EQBand7":
                            self.SpeakerEq7Value = Math.Round(ParseInvariantDouble(raw));
                            break;
                        case "EQBand8":
                            self.SpeakerEq8Value = Math.Round(ParseInvariantDouble(raw));
                            break;
                        case "EQBand9":
                            self.SpeakerEq9Value = Math.Round(ParseInvariantDouble(raw));
                            break;
                    }
                }
            }
        }
        catch
        {
        }

        return false;
    }

    public static bool PopulatePreset20_Prefix(object __instance, XElement selectedPresetXML)
    {
        if (__instance == null || selectedPresetXML == null)
        {
            return false;
        }

        try
        {
            PopulatePreset10_Prefix(__instance, selectedPresetXML);
            dynamic self = __instance;
            foreach (XElement slider in selectedPresetXML.Elements("Slider"))
            {
                foreach (XElement item in slider.Elements())
                {
                    string raw = item.Attribute("name")?.Value;
                    if (string.IsNullOrEmpty(raw))
                    {
                        continue;
                    }

                    switch (item.Name.LocalName)
                    {
                        case "MicEQBand1":
                            self.MicEq1Value = Math.Round(ParseInvariantDouble(raw));
                            break;
                        case "MicEQBand2":
                            self.MicEq2Value = Math.Round(ParseInvariantDouble(raw));
                            break;
                        case "MicEQBand3":
                            self.MicEq3Value = Math.Round(ParseInvariantDouble(raw));
                            break;
                        case "MicEQBand4":
                            self.MicEq4Value = Math.Round(ParseInvariantDouble(raw));
                            break;
                        case "MicEQBand5":
                            self.MicEq5Value = Math.Round(ParseInvariantDouble(raw));
                            break;
                        case "MicEQBand6":
                            self.MicEq6Value = Math.Round(ParseInvariantDouble(raw));
                            break;
                        case "MicEQBand7":
                            self.MicEq7Value = Math.Round(ParseInvariantDouble(raw));
                            break;
                        case "MicEQBand8":
                            self.MicEq8Value = Math.Round(ParseInvariantDouble(raw));
                            break;
                        case "MicEQBand9":
                            self.MicEq9Value = Math.Round(ParseInvariantDouble(raw));
                            break;
                        case "HighPassFilter":
                            self.HighPassFilterValue = ParseInvariantByte(raw);
                            break;
                    }
                }
            }
        }
        catch
        {
        }

        return false;
    }

    public static bool XmlDocCreateNewFile_Prefix(string customFilePath, FileMode mode)
    {
        if (!string.IsNullOrEmpty(customFilePath))
        {
            try
            {
                using (new FileStream(customFilePath, mode))
                {
                }
            }
            catch
            {
            }
        }
        return false;
    }

    private static double ParseInvariantDouble(string value)
    {
        string normalized = value.Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0.0;
    }

    private static byte ParseInvariantByte(string value)
    {
        if (byte.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte b))
        {
            return b;
        }
        double d = ParseInvariantDouble(value);
        return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(d)));
    }
}
