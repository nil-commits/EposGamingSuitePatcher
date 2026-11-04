# EPOS Gaming Suite — Clean-Room Runtime Patcher (Lib.Harmony)

A 100% self-contained, clean-room **Lib.Harmony** runtime patcher and launcher (`EPOSGamingSuite.Patcher`) for the official **EPOS Gaming Suite** (`v1.12.2.1185`) on Windows 10/11.

> **Legal Notice:** This repository contains **0% original EPOS/Sennheiser source code, XAML resources, graphics, or proprietary binaries**. It requires an existing local installation of the official EPOS Gaming Suite (`C:\Program Files (x86)\EPOS\Gaming Suite\`) and applies targeted runtime method patches in memory via [Lib.Harmony](https://github.com/pardeike/Harmony) to restore hardware interoperability and fix critical software bugs.

---

## Fixed Bugs & Improvements

### 1. Windows 10/11 CoreAudio `PropVariant 4127` Crash (`CoreAudioPatches.cs`)
- **Problem**: `DeviceDataModel.SearchSelectedDevice` in the original `GamingSuite.UI.Services.dll` evaluates `Device.Properties.GetValue(i).Value.ToString()` inside a debug log statement on every `MMDevice` property *before* checking the `PropertyKey` format GUID (`b3f8fa53-0004-438e-9003-51a46e139bfc`). On Windows 10/11, audio endpoints expose properties of type `VT_VECTOR | VT_LPWSTR` (`4127`), causing `NAudio` to throw `System.NotSupportedException: PropVariant 4127` and aborting headset/dongle matching.
- **Fix**: Replaces `SearchSelectedDevice` via `[HarmonyPrefix]` to validate `formatId` and `propertyId` prior to dereferencing `PropVariant.Value`, with defensive exception handling.

### 2. Instant Headset Detection & Dead Cloud Timeout Bypass (`FastConnectPatches.cs`)
- **Problem**: On startup (`REQ_REFRESH_CONNECTED_DEVICE`), `EPOSGamingSuiteService` immediately emits a `DEVICE_DISCONNECTED` (`messageType: "5"`) message and stalls for 12–15 seconds attempting to reach decommissioned EPOS cloud update servers (`WinHTTP Error 12180`) before reporting `DEVICE_CONNECTED` (`messageType: "4"`).
- **Fix**:
  - Inspects active Windows CoreAudio endpoints (`MMDeviceEnumerator`) for EPOS USB/Dongle hardware (`VID 0x1395` / `5013`, e.g., **EPOS H3PRO Dongle** `PID 771` / **EPOS H3PRO** `PID 257`) and fast-connects the headset in `< 100 ms`.
  - Suppresses bogus intermediate `DEVICE_DISCONNECTED` events while the EPOS audio endpoint remains active in Windows.

### 3. Dongle VID/PID & Playback/Microphone Binding (`CoreAudioPatches.cs`)
- **Problem**:
  - `PopulatePlaybackDeviceData` and `PopulateMicrophoneDeviceData` did not initialize `selectedDevicedetailSpeaker` / `selectedDevicedetailMic` with the wireless dongle `VID`/`PID`, causing initial volume and gain queries to fail.
  - `UpdateDeviceInfoForPlayback` contained a copy-paste bug that added updated playback devices to `allConnectedMicrophoneDeviceList` instead of `allConnectedPlaybackDeviceList`.
- **Fix**: Patched via `[HarmonyPostfix]` and `[HarmonyPrefix]` on `DeviceDataModel`.

### 4. Named Pipe IPC Stability (`NamedPipePatches.cs`)
- **Problem**: `PipeFileStream.GetPipeFileHandle` used a `4,096`-byte buffer and a blocking `10 * 2,000 ms` (`20 s`) sleep loop on `ERROR_PIPE_BUSY`.
- **Fix**: Upgrades the pipe buffer to `65,536` bytes (`64 KB`) and reduces retry backoff to `100 ms`.

### 5. Locale-Safe XML Presets & Path Fix (`PresetAndXmlPatches.cs`)
- **Problem**:
  - `PresetDataModel.GetPresetFromSettings` called `GetFilePathCustomPreset(GetFilePathCustomPreset(innerText))`, corrupting custom preset paths by prepending the directory and appending `.xml` twice.
  - Floating-point EQ, Reverb, and NoiseGate values were parsed using thread-local culture, failing on German (`de-DE`) systems when reading `.` decimal separators.
  - `XmlDoc.CreateNewFile` leaked an undisposed `FileStream` handle.
- **Fix**: Patched via `[HarmonyPrefix]` using `CultureInfo.InvariantCulture` and `using` blocks.

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
