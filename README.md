# EPOS Gaming Suite — Clean-Room Runtime Patcher (Lib.Harmony)

> **Kudos to [REA (Reverse Engineer Anything)](https://github.com/morluto/rea)** for enabling us to completely decompile and analyze the original EPOS Gaming Suite to pinpoint and fix its underlying bugs ([see how we did it below](#how-we-reverse-engineered-it-with-rea)).

A 100% self-contained, clean-room **Lib.Harmony** runtime patcher and launcher (`EPOSGamingSuite.Patcher`) for the abandoned **EPOS Gaming Suite** (`v1.12.2.1185`) on Windows 10/11.

> **Legal Notice:** This repository contains **0% original EPOS/Sennheiser source code, XAML resources, graphics, or proprietary binaries**. It requires an existing local installation of the official EPOS Gaming Suite (`C:\Program Files (x86)\EPOS\Gaming Suite\`) and applies targeted runtime method patches in memory via [Lib.Harmony](https://github.com/pardeike/Harmony) to restore hardware interoperability and fix critical software bugs.

---

## Summary of Runtime Patches

| Patch Class | Target Method(s) | What It Fixes |
| :--- | :--- | :--- |
| [`CoreAudioPatches`](EPOSGamingSuite.Patcher/Patches/CoreAudioPatches.cs) | `DeviceDataModel.SearchSelectedDevice`, `SetDefaultDevice`, `PopulatePlaybackDeviceData`, `PopulateMicrophoneDeviceData`, `UpdateDeviceInfoForPlayback` | Fixes the `PropVariant 4127` crash on Windows 10/11, binds wireless dongle `VID`/`PID` (`5013`/`771`) for volume/gain control, and fixes the playback list copy-paste bug. |
| [`FastConnectPatches`](EPOSGamingSuite.Patcher/Patches/FastConnectPatches.cs) | `DeviceDataModel.pipeServer_SendMessage`, `DisplayMessageDeviceConnected`, `DisplayMessageDeviceDiconnected` | Detects active EPOS USB/Dongle audio endpoints (`VID 0x1395` / `5013`) directly via Windows CoreAudio (`MMDeviceEnumerator`), connects the headset in `< 100 ms` (bypassing the 12-second dead cloud server stall), and ignores bogus transient `DEVICE_DISCONNECTED` messages while the hardware is active. |
| [`NamedPipePatches`](EPOSGamingSuite.Patcher/Patches/NamedPipePatches.cs) | `PipeFileStream.GetPipeFileHandle` | Upgrades the Named Pipe buffer from `4,096` bytes to `65,536` bytes (`64 KB`) and reduces `ERROR_PIPE_BUSY` retry backoff from `2,000 ms` (`20 s` UI freeze) to `100 ms`. |
| [`PresetAndXmlPatches`](EPOSGamingSuite.Patcher/Patches/PresetAndXmlPatches.cs) | `PresetDataModel.GetCommonFilePathPreset`, `PopulateValues00`, `PopulatePreset10`, `PopulatePreset20`, `XmlDoc.CreateNewFile` | Prevents double `.xml` path wrapping, enforces `CultureInfo.InvariantCulture` for all EQ/Reverb/NoiseGate values, and disposes leaked `FileStream` handles. |

---

## Usage

### Option A: Direct Launcher (No Admin Rights Required)
Build the project and run `EPOSGamingSuite.Patcher.exe` directly:
```powershell
dotnet build EPOSGamingSuite.Patcher.sln -c Release
.\EPOSGamingSuite.Patcher\bin\x86\Release\net461\EPOSGamingSuite.Patcher.exe
```
`EPOSGamingSuite.Patcher.exe` locates `C:\Program Files (x86)\EPOS\Gaming Suite\EPOSGamingSuite.exe`, applies all Harmony patches in memory, and starts the UI.

### Option B: Automatic AppDomainManager Hook (Zero-Touch Startup)
To have the original `EPOSGamingSuite.exe` (including Desktop shortcuts and Windows Autostart) load the Harmony runtime patches automatically without modifying any EPOS `.exe` or `.dll` binary, run as Administrator:
```powershell
.\EPOSGamingSuite.Patcher\bin\x86\Release\net461\EPOSGamingSuite.Patcher.exe --install
```
To remove the hook and restore `EPOSGamingSuite.exe.config`:
```powershell
.\EPOSGamingSuite.Patcher\bin\x86\Release\net461\EPOSGamingSuite.Patcher.exe --uninstall
```

---

## Diagnostics

Runtime patch status is logged to:
```text
C:\ProgramData\EPOS\Gaming Suite\Logs\HarmonyPatcher.log
```
Example output:
```text
2026-11-04 09:12:57.516 [INF] All Harmony runtime patches applied successfully.
2026-11-04 09:12:58.585 [INF] Fast-connecting active EPOS audio endpoint (PID 771) via Windows CoreAudio.
```

---

## How We Reverse Engineered It with [REA](https://github.com/morluto/rea)

After EPOS wound down its gaming division, the **EPOS Gaming Suite (`v1.12.2.1185`)** was left unmaintained. On modern Windows 10/11 systems, headsets (such as the **EPOS H3PRO** / **EPOS H3PRO Dongle**, `GSX 300`, `GSP 670`, `GSP 370`, `GTW 270`, and `B20`) would frequently fail to be recognized, startup would hang or feel sluggish, and custom EQ presets would silently break on non-English locales.

Using **[REA: Reverse Engineer Anything](https://github.com/morluto/rea)** ([rea.tools](https://rea.tools/) / [`rea-agents`](https://www.npmjs.com/package/rea-agents)), we completely decompiled the original suite and traced the failure chain end-to-end:

1. **PE Layout, Entropy & Managed Metadata Triage (`inspect_binary_layout`, `inspect_byte_entropy`, `inspect_managed_artifact`)**
   - Starting from a raw process memory dump (`EPOSGamingSuite_dump.exe`), REA's `inspect_binary_layout` and `inspect_pe_load_config` revealed that the PE section table had `PointerToRawData == VirtualAddress` (`0x2000` instead of `0x0400`) because it was dumped directly from virtual memory without unmapping section headers.
   - After realigning the section headers, REA's `inspect_managed_artifact` and `project_managed_application_graph` mapped the entire `.NET Framework 4.6.1` (`x86`) WPF client (`Sennheiser.Gs.UI`), its Autofac DI container (`ContainerConfig`), and its companion service assembly (`GamingSuite.UI.Services.dll`), enabling a full decompilation of all 108 UI files and 55 service files.

2. **Tracing the Named Pipe IPC & Native Service Boundary (`inspect_managed_members`, `verify_managed_native_boundaries`)**
   - Inspecting the boundary between `EPOSGamingSuite.exe`, `GamingSuite.UI.Services.dll`, and the native Windows service (`EPOSGamingSuiteService.exe`) revealed the two-way Named Pipe protocol over `\\.\pipe\GSUIPipe` and `\\.\pipe\GSUIAgentPipe`:
     - On startup, `MainWindow` sends `[{"requestType":"4"}]` (`REQ_SW_VERSION`) and `[{"requestType":"11"}]` (`REQ_REFRESH_CONNECTED_DEVICE`).
     - In response to `REQ_REFRESH_CONNECTED_DEVICE` (`11`), `EPOSGamingSuiteService` **first broadcasts `messageType: "5"` (`DEVICE_DISCONNECTED`)** and then attempts to reach decommissioned EPOS/Sennheiser cloud update servers (`HandleRestExecutorCallBack` / `processGetUpdateOrConfig`).
     - Because those cloud servers are offline, the service stalls for **12–15 seconds** throwing `WinHTTP Error 12180` (`ERROR_WINHTTP_AUTODETECTION_FAILED`) before finally sending `messageType: "4"` (`DEVICE_CONNECTED`) with the headset payload (`VID 5013`, `PID 257`, Dongle `PID 771`).

3. **Pinpointing the Windows 11 CoreAudio Crash (`PropVariant 4127`)**
   - Decompiling `Sennheiser.Gs.UI.Services.Model.DeviceDataModel.SearchSelectedDevice` and correlating it with `GamingSuiteUI.log` revealed why Windows 11 audio endpoints (`Kopfhörer (EPOS H3PRO Dongle)` / `Kopfhörermikrofon (EPOS H3PRO Dongle)`) failed to bind:
   - Before checking whether a `PropertyKey` matched `PKEY_DeviceInterface_FriendlyName` (`b3f8fa53-0004-438e-9003-51a46e139bfc`, `propertyId = 2`), the original code executed:
     ```csharp
     _logger.LogDebug(Device.Properties.GetValue(i).Value.ToString() + ...);
     ```
   - On Windows 10/11, CoreAudio endpoints expose `VT_VECTOR | VT_LPWSTR` (`VarEnum` `4127`) properties. Calling `.Value` on `NAudio.CoreAudioApi.Interfaces.PropVariant` for type `4127` throws `System.NotSupportedException: PropVariant 4127`, aborting the loop before it ever reaches the USB `VID_1395&PID_0303` property.

4. **Uncovering Copy-Paste, Dongle-PID & Locale Bugs**
   - **Copy-Paste Bug in `UpdateDeviceInfoForPlayback`**: Removed the playback `DeviceInfo` from `allConnectedPlaybackDeviceList`, updated its firmware fields, and mistakenly added it to `allConnectedMicrophoneDeviceList`.
   - **Unbound Dongle VID/PID**: `PopulatePlaybackDeviceData` and `PopulateMicrophoneDeviceData` never populated `selectedDevicedetailSpeaker` / `selectedDevicedetailMic` with the wireless dongle's `VID`/`PID`, causing initial Windows volume/gain lookups (`GetSelectedDeviceInfoForVolume`) to search for `VID=0, PID=0`.
   - **Double Path Wrapping & German Locale Bug**: `PresetDataModel.GetPresetFromSettings` called `GetFilePathCustomPreset(GetFilePathCustomPreset(innerText))` twice, and parsed XML decimal strings (`6.0`, `4.02`) using the current OS thread culture—breaking on German (`de-DE`) systems that expect `,` instead of `.`.
