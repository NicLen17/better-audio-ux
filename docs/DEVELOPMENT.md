# Development guide

Better Audio UX is a Windows 11 output selector built with C#, WPF, and .NET 10 LTS. Its UI is in Spanish. It includes an integrated session mixer, Windows Sonic controls, and animated view transitions.

## Running locally

After publishing, open **Better Audio.lnk** from the repository root or run `artifacts/BetterAudio/BetterAudio.exe`. The self-contained package includes the runtime. Keep all package files together; administrator privileges are not required.

Startup shows the HUD and adds a green icon to the tray. Windows controls whether it appears in the visible tray or the hidden-icons menu. The application does not change that preference.

The HUD centers on the tray icon rectangle returned by `Shell_NotifyIconGetRect` and stays within the monitor work area. The background uses WPF transparency, shadow, and a blurred halo rather than system Acrylic. The carousel uses continuous arc positioning, scaling, opacity, and Z rotation.

## Interaction and state

See the [README controls table](../README.md#controls) for keyboard and mouse shortcuts.

Hover waits 350 ms before showing a preview without keyboard focus. Moving away hides it after a grace period. Clicking makes the panel interactive. Pinning keeps it open when another application receives focus; unpinning restores focus-loss dismissal. The window is reused rather than recreated. Wheel handling over an inactive window depends on the corresponding Windows setting.

Automatic switching is enabled by default and saved in `%LOCALAPPDATA%\BetterAudio\settings.json`. Pending output changes are replaced by the most recent selection, while COM calls remain serialized. Disabling automatic switching cancels queued requests; an operation already running may finish. Re-enabling affects subsequent navigation.

The central bubble shows the selected output's commercial name; its tooltip retains the original endpoint name. Adjacent outputs use 65% scale and approximately 40% opacity. Status labels distinguish the Windows default from available alternatives. Manual mode allows previewing and adjusting each endpoint before confirmation. Automatic mode also follows externally changed defaults. Activation sets the console, multimedia, and communications roles. Applications with explicit routing may keep their assignments.

The integrated mixer tracks sessions for the selected endpoint. Applications can expose multiple sessions. Master volume is separate from session controls. The wheel over the session list always scrolls; use sliders or keyboard input to change session volume. Session controls are preserved during updates and use stable ordering. Missing application icons use a generic fallback, aligned with the mute controls.

The spatial toggle selects Windows Sonic when enabled and disables processing when unchecked. State is queried through `ISpatialAudioClient` and verified again after writes. Unsupported or unknown states disable the toggle and provide a tooltip. The gear opens endpoint properties for other providers and their licensing. Re-enabling selects Sonic even if another provider was previously active.

## Implementation decisions

- **.NET 10 LTS:** the desktop application targets .NET 10. See the [official support policy](https://dotnet.microsoft.com/en-us/platform/support/policy).
- **WPF HUD:** 408 × 378 DIPs in the main view and 408 × 482 in the mixer; messages add 28 DIPs. The footer has three rectangular sections and a separate rounded base with a dark separator. Gear colors invert when spatial audio is enabled. Mouse clicks do not leave a persistent focus border; keyboard navigation retains its focus indicator.
- **Native tray icon:** GUID identity and `Shell_NotifyIconW`; [actual bounds](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyicongetrect) rather than reflection into private NotifyIcon fields. Protocol v4 replaces the standard tooltip with the HUD. Explorer restarts restore the icon. The context menu uses Windows Forms.
- **NAudio.Wasapi 3.3.0:** a focused dependency instead of the NAudio metapackage. `CreateNotificationClient()` receives connection, removal, rename, and default-output notifications. Endpoint volume/mute events are also observed. [Source](https://github.com/naudio/NAudio).
- **Dedicated COM thread:** UI code receives snapshots, never live COM objects. Endpoint notifications coalesce for 100 ms; volume writes coalesce for 45 ms. Queued writes preserve endpoint and session identifiers when navigation changes. Closing the mixer releases its session notifications.
- **Default output:** `ComPolicyConfig.cs` isolates the undocumented `IPolicyConfig` interface. Failures appear in the HUD and local logs. Future Windows versions may require interoperability changes.
- **Spatial backend:** reads use [`ISpatialAudioClient::GetMaxDynamicObjectCount`](https://learn.microsoft.com/en-us/windows/win32/api/spatialaudioclient/nf-spatialaudioclient-ispatialaudioclient-getmaxdynamicobjectcount). Writes use SoundVolumeCommandLine 1.28, launched without a window and with a timeout. Original vendor files are preserved; the build verifies the executable SHA-256 against its provenance file. Output properties use the [Windows Settings URI](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings). The application does not write spatial configuration directly into the registry.
- **Opening:** audio state is prepared while hidden. The native window stays at opacity 1; only `HudSurface` fades in for 170 ms. Clicks during a pending hover opening promote the same opening. Cancellation invalidates pending continuations, preventing a second reveal after asynchronous reads.
- **View transitions:** height interpolates for 300 ms using cubic easing. `SizeChanged` repositions the window without forcing its final height. Reversals start at the current visible height. Content fades for 260 ms and translates for 300 ms. Transitions respect the Windows animation preference; disabled animations apply the target size immediately.
- **Single instance:** session mutex and local IPC bring the existing instance forward when another executable is launched.
- **Placement:** tray monitor work area and DPI determine placement; the cursor is a fallback if the icon rectangle is unavailable. No global keyboard/mouse hooks or continuous audio polling.
- **Packaging:** self-contained win-x64 ReadyToRun output, without WPF trimming or extracting a single-file bundle on each startup.

## Build and test

Requires Windows and the **.NET 10 SDK**. `build.ps1` uses `%LOCALAPPDATA%\BetterAudio\sdk` if present, otherwise `dotnet` from PATH.

```powershell
# Publish
.\build.ps1

# Regression tests and portable ZIP
.\build.ps1 -Test -Package

# Also check the local Windows audio integration
.\build.ps1 -Test -Live -Package
```

The script refuses to publish while Better Audio is running; quit through the tray menu first. It does not terminate processes automatically. NuGet dependencies are recorded in `packages.lock.json`. GitHub Actions runs the same build and regression tests on Windows and uploads the portable application as an artifact.

```text
AudioFlyoutApp/
  Core/Audio/       Core Audio, dedicated COM thread, IPolicyConfig
  Core/Models/      Endpoint and session snapshots
  UI/Flyout/       Window, carousel, keyboard and wheel input
  UI/ViewModels/   Reactive state, navigation, coalesced writes
  Services/        Tray, Win32 placement, preferences, logging
AudioFlyoutApp.Tests/
  Program.cs       Regression runner without an additional test framework
```

The regression runner covers selection, coalesced writes, simulated unplug/recovery, preferences, geometry, initial layout, session updates, spatial failures, pending/cancelled openings, hover interpolation, resizing and interrupted transitions. Tests respect the runner's Windows animation preference: immediate resizing is expected when animations are disabled, as on hosted CI runners.

After building the test project, pass a mode to `AudioFlyoutApp.Tests.dll`:

| Mode | Behavior |
| --- | --- |
| No arguments | Regression suite with simulated audio |
| `--live` | Enumerate real endpoints/sessions, query spatial state, write a session's existing volume/mute values unchanged, and briefly verify a test tray icon |
| `--spatial-roundtrip` | Explicitly enable Sonic and restore the initial disabled state; skip if a provider is already active |
| `--ui` | Preview with real endpoints |
| `--ui-demo` | Main-view preview using demonstration data |
| `--ui-mixer-demo` | Mixer preview using demonstration data |
| `--render` | Export both views as PNG files under artifacts without showing windows |

## Diagnostics and scope

Rotating local logs are stored in `%LOCALAPPDATA%\BetterAudio\app.log`. No application telemetry, accounts, remote services, or automatic startup registration are included.

The portable package is unsigned and has no installer or updater. Simulated disconnect tests do not replace physical Bluetooth/USB reconnection tests. Monitor/DPI combinations need testing on the relevant hardware. Display output names come from their drivers; icon classification is heuristic. Microphone controls and per-application output routing are outside the current scope.

## Third-party distribution

SoundVolumeCommandLine is Nir Sofer freeware. Its terms require unchanged redistribution with all original files and prohibit sale or inclusion in a commercial product. This free/personal package preserves the original readme and other vendor files. A commercial distribution must replace that backend or obtain permission from the author. See the [original license and documentation](https://www.nirsoft.net/utils/sound_volume_command_line.html) and [third-party notices](../THIRD-PARTY-NOTICES.md). Do not translate or modify bundled third-party license files.

## Audio mirroring

`IAudioMirrorService` exposes a single fixed source/destination pair. Capture and playback are created and disposed on the existing audio apartment. NAudio's WASAPI loopback recorder requests 48 kHz float stereo from the Windows mixer, including downmixing surround headsets. Shared playback lets Windows convert to the destination format. `VolumeSampleProvider` changes only the copy gain; master levels and the default endpoint are untouched.

The transfer buffer is limited to 400 ms. Capture clears a backlog exceeding 150 ms to prevent accumulated delay after a driver stall or clock drift. This is a best-effort live copy rather than sample-synchronized multi-device playback. Worker stop callbacks queue cleanup onto the apartment and check the capture identity so old events cannot stop a replacement stream. Endpoint release and application shutdown also clean up both workers. No automatic restart or persistent routing is applied.

Regression tests cover the panel state, invalid pairs, serialization, copy volume, interruption, and recovery. Run `AudioFlyoutApp.Tests.dll --mirror-live` for a hardware test: it plays short chimes on the current default, copies them at 25% to another audible endpoint, measures destination loopback audio, verifies copy silence at 0%, and checks restart/worker-stop cleanup and preservation of endpoint settings. It requires two active outputs and no unrelated audio on the destination. `--render` also writes `artifacts/hud-mirror-preview.png`.
