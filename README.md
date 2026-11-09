# AULA F75 Pro wireless RGB for OpenRGB

An experimental Windows companion exposes a compatible AULA F75 Pro 2.4 GHz receiver to OpenRGB through a local SDK connection. Tested with OpenRGB 1.1 on one keyboard. Solid colors and four physical key positions were verified; Gradient Wave works but is choppy.

## Setup

1. Install OpenRGB 1.1 at `C:\Program Files\OpenRGB`, and connect the keyboard's 2.4 GHz receiver.
2. Run `F75-Wireless-Bridge.exe` and wake the keyboard. Wait for the receiver identification message.
3. In OpenRGB's **SDK Client**, connect to `127.0.0.1` port `6743`, and optionally save the connection.
4. Select **AULA F75 Pro (2.4 GHz)** under Devices. Keep the companion running. For manual colors, exclude the keyboard from any active effect first.

The bridge accepts a receiver with VID/PID `3554:FA09`, revision `3000`, the tested report descriptor and model ID `CD`. Other variants and Bluetooth are untested. Multiple matching receivers are refused. It streams temporary lighting; it does not save settings to the keyboard. It listens only on localhost. Other applications connecting to the OpenRGB service on port 6742 may not see this desktop-client device; SDK clients can connect directly to port 6743.

Wireless effects can be choppy. Each state is sent as 20-byte reports, paced at 12 ms per report; more distinct colors require more reports. Closing or minimizing the window keeps the companion running in the system tray. Double-click the tray icon or choose Show to reopen it; choose Exit from the tray menu to stop it. Use `--start-minimized` to launch directly into the tray. If other RGB devices behave erratically, check for duplicate OpenRGB desktop instances; the normal background service is separate.

## Build

Close both the bridge and diagnostics, then run `Build-F75.ps1` in Windows PowerShell. It uses the Windows .NET Framework x64 compiler and the installed OpenRGB `hidapi-hotplug.dll`. The included offline self-test checks framing, checksums, mapping, SDK serialization and malformed input handling. Hardware validation sampled four keys; the whole layout and maximum color-count path need further testing.

## Credits and license

GPL-2.0-or-later; see `LICENSE-GPL-2.0.txt`.

Protocol and layout references: [OpenRGB wired support](https://gitlab.com/CalcProgrammer1/OpenRGB/-/merge_requests/3062), [wireless draft](https://gitlab.com/CalcProgrammer1/OpenRGB/-/merge_requests/3027), [device research](https://gitlab.com/CalcProgrammer1/OpenRGB/-/issues/5166), [Keylux notes](https://github.com/SibteProf/keylux/blob/main/docs/PROTOCOL.md), and [Aula Manager notes](https://github.com/free-soldier28/Aula-Manager/blob/main/docs/PROTOCOL.md). No Keylux or Aula Manager implementation code is incorporated.

