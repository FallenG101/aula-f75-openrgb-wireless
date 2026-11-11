# AULA F75 Pro wireless RGB for OpenRGB

An experimental Windows companion connects a tested AULA F75 Pro 2.4 GHz receiver to OpenRGB through a local SDK connection. Version 0.2 adds smoother rendering, a simple interface, a keyboard tray/taskbar icon, pause/resume and live process statistics. The owner described the corrected Smooth build as “Super smooth.” This is a standalone companion, not an official OpenRGB driver.

## Setup

1. Install OpenRGB 1.1 at `C:\Program Files\OpenRGB`. Download `F75-Wireless-Bridge.exe` from this repository to a permanent, writable folder.
2. Connect the receiver, select 2.4 GHz mode, unplug the keyboard cable and press a key to wake it.
3. Run the companion. Wait for **Receiver identified: model CD**.
4. In OpenRGB's **SDK Client**, connect to `127.0.0.1` port `6743`. Save the connection if desired; avoid duplicate entries. Keep the existing service connection on 6742.
5. Select **AULA F75 Pro (2.4 GHz)**. Enable it in an effect for animation; exclude it from active effects before choosing manual colors.
6. Enable **Smooth rendering (experimental)** for the faster path. The preference is saved in `F75-settings.txt` beside the executable. Fresh installations default to the exact-color fallback.

The bridge targets exactly one receiver with VID/PID `3554:FA09`, revision `3000`, interface 1, usage `FF02/0002`, the tested 20-byte descriptor, and checksummed model ID `CD`. Unknown models and ambiguous multiple receivers are refused. Other variants, Bluetooth and Linux are untested. Only model queries and temporary lighting commands are used; no flash saves, firmware, reset, pairing or remapping.

The keyboard is exposed through the desktop client's SDK connection. Applications connected only to the service on 6742 may not see it; SDK applications can connect directly to 6743. The bridge listens only on localhost and does not contact an Internet service. Other local applications reaching that port can change temporary lighting.

## Tray and startup

Closing or minimizing leaves the app in the tray. Double-click the keyboard icon, choose **Open F75**, or launch the executable again to show the existing 0.2 window. **Exit** stops it. **Pause lighting** stops lighting writes even while OpenRGB sends new colors; Resume uses the latest state. Pause is not saved and does not guarantee immediate LED shutoff.

Create a shortcut to the executable with `--start-minimized` in its target arguments, then place it in the folder opened by **Win+R → shell:startup**. Keep OpenRGB configured to start too, with the 6743 connection saved. This starts at user login, not before login. Keep the executable in its permanent location.

Only one 0.2 copy runs per user session. An older bridge can still conflict on port 6743. If mouse/fan effects behave erratically, check for duplicate OpenRGB desktop instances; the normal background service is separate.

## Rendering and power

Smooth mode sends a whole-board solid color in one compact report, preserving 8-bit brightness. Multicolor frames use at most eight colors selected from the current input frame and at most eight reports. Small palettes retain exact colors; complex gradients lose detail. Black stays black and nonblack keys are never mapped to black. The first candidate's channel rounding was replaced after it produced unwanted hues.

Smooth uses 5 ms inter-report spacing, a minimum 50 ms frame interval, and a sustained budget of 70 lighting reports/second. Calculated ceilings are 20 solid updates/second and 8.75 updates/second for eight-report frames. USB write time can lower these; they are not measured FPS guarantees. Incoming frames are coalesced to the latest state and planned only when due.

With Smooth off, exact colors use one complete 6–29-report frame, 12 ms spacing, a minimum 80 ms frame interval and the same report budget. This is slower but preserves all per-key RGB values.

**Battery runtime has not been measured.** The owner confirmed idle sleep on the previous build; version 0.2 sleep/wake needs a fresh check. Bounded traffic and fewer solid-color reports are useful proxies, not proof of battery neutrality. Compare charge intervals at the same brightness/effect and similar active use. Pause or exit if sleep regresses. UI CPU/memory readings describe the PC process, not keyboard power.

## Validation and building

Previous-build hardware checks confirmed steady whole-board red, Escape/W/Space/Right Arrow mapping, individual W magenta, normal typing and receiver unplug/replug recovery. The owner confirmed the corrected 0.2 animation is very smooth. All 80 physical positions, compact solid/brightness behavior, maximum-color exact frames, Windows login and PC suspend/resume have not been exhaustively retested on 0.2.

The build passes **21,989 offline checks** for framing, checksums, mapping, SDK versions 0–3, malformed input, all 256 solid brightness levels and randomized palette coverage, plus **five mock runtime checks** for SDK updates, pacing, pause and resume. These do not access HID hardware and do not replace visual tests.

Exit the bridge and diagnostics, then run `Build-F75.ps1` in Windows PowerShell. It uses the x64 .NET Framework compiler, embeds `F75.ico`, builds both programs and runs the checks. Runtime HID access uses `C:\Program Files\OpenRGB\hidapi-hotplug.dll`; changing that location requires editing the path and rebuilding.

Arguments: `--start-minimized`, `--exit`, `--self-test`, `--runtime-test`. The developer-only `--mock --headless` server uses 6743 and cannot run alongside the real bridge. Runtime tests use an ephemeral local TCP port instead.

See [CHANGELOG.md](CHANGELOG.md). No native OpenRGB patch or upstream submission has been made.

## Credits and license

GPL-2.0-or-later; see `LICENSE-GPL-2.0.txt`.

Protocol and layout references: [OpenRGB wired support](https://gitlab.com/CalcProgrammer1/OpenRGB/-/merge_requests/3062), [wireless draft](https://gitlab.com/CalcProgrammer1/OpenRGB/-/merge_requests/3027), [device research](https://gitlab.com/CalcProgrammer1/OpenRGB/-/issues/5166), [Keylux notes](https://github.com/SibteProf/keylux/blob/main/docs/PROTOCOL.md), and [Aula Manager notes](https://github.com/free-soldier28/Aula-Manager/blob/main/docs/PROTOCOL.md). No Keylux or Aula Manager implementation code is incorporated.
