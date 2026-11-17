# AULA F75 Pro Wireless Bridge

Control your AULA F75 Pro’s RGB from OpenRGB over its **2.4 GHz USB receiver**. The bridge runs in the Windows tray and exposes the keyboard through OpenRGB’s local SDK connection.

**Windows x64 · OpenRGB 1.1 · Experimental prerelease**

[**Download v0.2.3-rc.1 for Windows**](https://github.com/FallenG101/aula-f75-openrgb-wireless/releases/tag/v0.2.3-rc.1) · [Changelog](CHANGELOG.md) · [Build and hardware notes](DEVELOPMENT.md)

## Get started

1. Install OpenRGB 1.1 in `C:\Program Files\OpenRGB`. The bridge loads `hidapi-hotplug.dll` from that directory.
2. Download the Windows ZIP from the release assets and extract it to a permanent, writable folder.
3. Plug in the receiver, switch the keyboard to **2.4 GHz**, unplug the keyboard cable, and press a key to wake it.
4. Launch `F75-Wireless-Bridge.exe` and wait for **Receiver identified: model CD**.
5. In OpenRGB, open **SDK Client** and connect to **`127.0.0.1:6743`**. Save one connection for the bridge and keep the existing service connection on port `6742`.
6. Select **AULA F75 Pro (2.4 GHz)** in OpenRGB. Include it in an effect for animation, or exclude it from active effects before setting colors manually.

## Compatibility

The bridge was tested with one AULA F75 Pro receiver. It matches USB ID `3554:FA09`, revision `3000`, interface `1`, HID usage `FF02/0002`, the tested 20-byte descriptor, and a checksummed model response of `CD`. Unknown models and ambiguous matches are refused. Other hardware variants, Bluetooth, and Linux remain untested.

The bridge sends model queries and temporary lighting commands. It listens on localhost, where other local applications can also reach port `6743`. SDK apps connected only to the OpenRGB service on `6742` may need a separate connection to see the keyboard.

## Choose a rendering mode

| Mode | Best for | How it works |
| --- | --- | --- |
| **Exact colors** (default) | Static layouts and per-key color fidelity | Preserves each RGB value in a complete frame of 6–29 reports. |
| **Smooth rendering** | Animated effects | Uses one compact report for solid colors with 8-bit brightness. Multicolor frames use up to eight colors from the input frame, so complex gradients lose detail. |

Both modes use paced writes and keep the latest incoming frame. The prerelease enforces at least 15 ms between report starts, 15 ms of quiet after each frame, a 100 ms minimum frame interval, and a sustained limit of 60 lighting reports per second. Calculated ceilings are 10 solid updates per second and 7.5 eight-report updates per second. Hardware throughput depends on USB write time.

## Tray controls and startup

- **Pause lighting** stops writes. **Resume** applies the latest state. Pause is session-only and can leave the last color visible.
- Closing the window sends the app to the tray. Double-click the tray icon or launch the EXE again to reopen it. Use **Exit** in the tray menu to stop the process.
- To launch at sign-in, add a shortcut to the EXE with `--start-minimized` in **Win+R → `shell:startup`**. Configure OpenRGB to start with its saved SDK connection. Update the shortcut if you move the EXE.
- The bridge saves its rendering preference in `F75-settings.txt` next to the EXE. Its window shows report rate, process CPU, and memory usage.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| Receiver stays undetected | Confirm the keyboard is awake in 2.4 GHz mode with the cable unplugged. Connect only one matching receiver. |
| Keyboard is absent from OpenRGB | Check the bridge status and connect OpenRGB’s SDK Client to `127.0.0.1:6743`. Close any older bridge instance using that port. |
| Manual colors keep changing | Exclude the keyboard from active effects before setting a manual color. |
| Typing repeats or stops | Pause or exit the bridge. Charge the keyboard and try the receiver directly in a PC USB port if it was on a hub. |

One repeated-key freeze occurred on v0.2.1 with a USB hub and a reportedly very low battery. A short test after charging on v0.2.3 showed smooth animation and normal typing. The cause remains open. Battery runtime, Windows sign-in, and PC suspend/resume still need validation. Local rotating logs record aggregate timing and report counts and omit typed input.

## Build from source

Exit the bridge and diagnostics, then run `.\Build-F75.ps1` in Windows PowerShell. It uses the x64 .NET Framework compiler, embeds `F75.ico`, builds both programs, and runs offline and mock checks. See the [development notes](DEVELOPMENT.md) for hardware coverage, protocol details, and recovery steps.

<details>
<summary>Command-line options</summary>

- `--start-minimized` starts in the tray.
- `--start-paused` starts with lighting writes paused.
- `--exit` stops the existing instance.
- `--self-test` and `--runtime-test` run offline and mock checks.
- `--mock --headless` runs the development SDK server on port `6743`. Exit the real bridge first.

</details>

## Credits and license

This independent OpenRGB SDK companion is licensed under [GPL-2.0-or-later](LICENSE-GPL-2.0.txt). Protocol and layout references include [OpenRGB wired support](https://gitlab.com/CalcProgrammer1/OpenRGB/-/merge_requests/3062), [the wireless draft](https://gitlab.com/CalcProgrammer1/OpenRGB/-/merge_requests/3027), [device research](https://gitlab.com/CalcProgrammer1/OpenRGB/-/issues/5166), [Keylux notes](https://github.com/SibteProf/keylux/blob/main/docs/PROTOCOL.md), and [Aula Manager notes](https://github.com/free-soldier28/Aula-Manager/blob/main/docs/PROTOCOL.md). Keylux and Aula Manager provided protocol references.
