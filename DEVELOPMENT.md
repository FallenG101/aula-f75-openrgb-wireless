# Development, recovery and hardware notes

## Restore a working development copy

On Windows, with Git installed, choose a permanent folder and run:

```powershell
cd H:\Development
git clone https://github.com/FallenG101/aula-f75-openrgb-wireless.git aula-f75-openrgb-wireless
cd aula-f75-openrgb-wireless
.\Build-F75.ps1
```

The build script uses the installed x64 .NET Framework compiler, builds the bridge and receiver diagnostics, and runs offline and mock tests. OpenRGB 1.1 and its `hidapi-hotplug.dll` are expected under `C:\Program Files\OpenRGB`. Review the script before running it. See README for setup and runtime requirements.

The [v0.2.3-rc.1 prerelease](https://github.com/FallenG101/aula-f75-openrgb-wireless/releases/tag/v0.2.3-rc.1) contains the tested Windows ZIP and a SHA-256 checksum. The release tag points to commit `4caf36828f75addb5378a5a04fefccca65873bab`; the published ZIP's SHA-256 is `4accab32220933b9ac409c918972bc007ee31d11e2de9cb6d10c65a89db856a0`. It is experimental.

Settings and logs live alongside the running executable and are not restored by a clone. A Windows Startup shortcut points to a specific executable path: if the old executable is removed or moved, update or recreate the shortcut to target the intended permanent executable with `--start-minimized`. Verify startup by logging in. The OpenRGB SDK client connection to localhost port 6743 is configured separately in OpenRGB.

## Hardware evidence and implementation

The tested receiver identifies as USB `3554:FA09`, revision `3000`, interface 1, usage `FF02/0002`, with a 20-byte input/output collection. A validated response identified model CD. The observed HID report descriptor has SHA-256 `63c9063b2fdf03b2b064258390f14cbedd0c106f5bae34afd1de473f029c0406`. USB revision is not established as firmware version. This evidence is from one unit and does not establish compatibility with other variants.

The model response observed was `13 05 01 00 0A 03 00 00 00 00 CD 01 00 00 00 00 00 00 00 F4`. The bridge requires its specific descriptor/revision and a checksummed model response; ambiguous receivers and unknown models are refused.

The tested mapping includes Escape=0, W=14, Space=35 and Right Arrow=89. Individual and four-key colors, typing, and receiver reconnect were checked on the owner's unit. All 80 positions and the maximum 29-report distinct-color frame were checked offline, not exhaustively on hardware.

Full keyboard state is serialized as one logical message, grouped by RGB color and split into report-13 / command-88 packets. Competing effects confounded early partial-message experiments, so the exact cause of the observed flicker is not established. Current v0.2.3 timing and lossy Smooth rendering are described in README; older investigation notes describing 5 ms or 12 ms spacing refer to earlier builds, not the current release.

## Validation still needed

- Long-duration typing stability and battery runtime under comparable use.
- Full Windows login startup and PC suspend/resume.
- All physical key positions, compact solid-color brightness and maximum-color frames on hardware.
- Other receiver revisions, keyboard variants, Bluetooth and Linux.

A single repeated-key/input freeze occurred on v0.2.1 with a receiver connected through a hub. The battery was later reported very low; its cause remains unknown. A short charged-keyboard test on v0.2.3 showed smooth animation and normal typing, but does not establish a long-term fix.

## Native OpenRGB contribution

The project is a standalone SDK companion, not an OpenRGB core patch. No native patch has been built or submitted. A future contribution needs implementation against current OpenRGB, appropriate platform and hardware tests, full layout and high-color-count validation, and evidence before broadening device matching. Preserve unique receiver identification, model checks, complete-frame serialization and bounded traffic; do not introduce unrelated firmware, pairing, reset, remapping or flash-save commands.
