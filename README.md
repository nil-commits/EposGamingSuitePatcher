# EPOS Gaming Suite — Clean-Room Runtime Patcher (Lib.Harmony)

> **Kudos to [REA (Reverse Engineer Anything)](https://github.com/morluto/rea)** for enabling us to completely decompile and analyze the original EPOS Gaming Suite to pinpoint and fix its underlying bugs ([see how we did it below](#how-we-reverse-engineered-it-with-rea)).

A 100% self-contained, clean-room **Lib.Harmony** runtime patcher and launcher (`EPOSGamingSuite.Patcher`) for the abandoned **EPOS Gaming Suite** (`v1.12.2.1185`) on Windows 10/11.

> **Disclaimer & Legal Notice:** This is an **unofficial, community-made patch** and is **NOT affiliated with, endorsed by, or sponsored by EPOS, Sennheiser, or Demant A/S**. All product and company names are trademarks of their respective holders and are used solely for identification and interoperability purposes. This repository contains **0% original EPOS/Sennheiser source code, XAML resources, graphics, or proprietary binaries**. It requires an existing local installation of the official EPOS Gaming Suite (`C:\Program Files (x86)\EPOS\Gaming Suite\`) and applies targeted runtime method patches in memory via [Lib.Harmony](https://github.com/pardeike/Harmony) to restore hardware interoperability and fix critical software bugs.

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

### Option A: Pre-Built Release (Recommended)
1. Download [`EPOSGamingSuitePatcher-v1.0.0-win-x86.zip`](https://github.com/nil-commits/EposGamingSuitePatcher/releases/tag/v1.0.0) from the [Releases page](https://github.com/nil-commits/EposGamingSuitePatcher/releases) and extract it anywhere.
2. Run `EPOSGamingSuite.Patcher.exe` once and approve the Administrator (UAC) prompt.
   - It automatically installs a persistent `.NET Framework` `AppDomainManager` hook into `C:\Program Files (x86)\EPOS\Gaming Suite\EPOSGamingSuite.exe.config` (without modifying any original EPOS `.exe` or `.dll` binary) and launches `EPOSGamingSuite.exe`.
   - From then on, closing and reopening EPOS Gaming Suite from your normal Desktop shortcut, Start Menu, Taskbar, or Windows Autostart will **always** load all Harmony runtime patches automatically.

### Option B: Build from Source
```powershell
dotnet build EPOSGamingSuite.Patcher.sln -c Release
.\EPOSGamingSuite.Patcher\bin\x86\Release\net461\EPOSGamingSuite.Patcher.exe
```

### Uninstalling the Hook
To remove the persistent hook and restore the original `EPOSGamingSuite.exe.config`:
```powershell
.\EPOSGamingSuite.Patcher.exe --uninstall
```

---

## Security & VirusTotal Verification

All code in this repository is 100% open-source C# and can be inspected or built locally from source. Pre-built release binaries (`v1.0.0`) have the following SHA-256 checksums and VirusTotal report links:

| File | SHA-256 | VirusTotal |
| :--- | :--- | :--- |
| `EPOSGamingSuite.Patcher.exe` | `9d170e0146cb87f38174f2f6bf7ef80c0b67896d6688cc188c449f749bf5e56d` | [VirusTotal Report (EXE — 0 Detections)](https://www.virustotal.com/gui/file/9d170e0146cb87f38174f2f6bf7ef80c0b67896d6688cc188c449f749bf5e56d) |
| `EPOSGamingSuitePatcher-v1.0.0-win-x86.zip` | `64a12e697856fa976dbf7508e86a0ab5b437a6997913458b38356fb5b95fee56` | [VirusTotal Report (ZIP — 0 Detections)](https://www.virustotal.com/gui/file/64a12e697856fa976dbf7508e86a0ab5b437a6997913458b38356fb5b95fee56) |
| Repository URL | `25b252a69fddc6b88f7a37b58367f3b14f73f0740e1fdb245e27f081d45bd176` | [VirusTotal Report (Repo URL — 0 Detections)](https://www.virustotal.com/gui/url/25b252a69fddc6b88f7a37b58367f3b14f73f0740e1fdb245e27f081d45bd176) |

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
