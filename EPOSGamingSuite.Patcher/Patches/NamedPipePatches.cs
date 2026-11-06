using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;

namespace EPOSGamingSuite.Patcher.Patches;

/// <summary>
/// Runtime patches for Named Pipe IPC stability:
/// - Upgrades pipe client buffer size from 4 KB (4096) to 64 KB (65536)
/// - Reduces ERROR_PIPE_BUSY retry delay from 2,000 ms (20 s freeze) to 100 ms
/// - Marks NamedPipeServer.listenThread as a background thread so closing the app does not leave a background process
/// </summary>
internal static class NamedPipePatches
{
    public static void Apply(Harmony harmony, Assembly servicesAsm)
    {
        if (servicesAsm == null)
        {
            return;
        }

        Type pipeFileStreamType = servicesAsm.GetType("Sennheiser.Gs.UI.Services.Model.PipeFileStream");
        if (pipeFileStreamType != null)
        {
            MethodInfo getHandleMethod = AccessTools.Method(pipeFileStreamType, "GetPipeFileHandle");
            if (getHandleMethod != null)
            {
                harmony.Patch(
                    getHandleMethod,
                    transpiler: new HarmonyMethod(typeof(NamedPipePatches), nameof(GetPipeFileHandle_Transpiler)));
            }
        }

        Type namedPipeServerType = servicesAsm.GetType("Sennheiser.Gs.UI.Services.Model.NamedPipeServer");
        if (namedPipeServerType != null)
        {
            MethodInfo startMethod = AccessTools.Method(namedPipeServerType, "Start");
            if (startMethod != null)
            {
                harmony.Patch(
                    startMethod,
                    postfix: new HarmonyMethod(typeof(NamedPipePatches), nameof(NamedPipeServer_Start_Postfix)));
            }
        }
    }

    public static IEnumerable<CodeInstruction> GetPipeFileHandle_Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int val)
            {
                if (val == 4096)
                {
                    instruction.operand = 65536;
                }
                else if (val == 2000)
                {
                    instruction.operand = 100;
                }
            }
            yield return instruction;
        }
    }

    public static void NamedPipeServer_Start_Postfix(object __instance)
    {
        try
        {
            if (__instance == null)
            {
                return;
            }
            FieldInfo threadField = AccessTools.Field(__instance.GetType(), "listenThread");
            if (threadField?.GetValue(__instance) is Thread thread && thread.IsAlive)
            {
                thread.IsBackground = true;
            }
        }
        catch
        {
        }
    }
}
