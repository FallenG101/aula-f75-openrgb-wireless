# Changelog

## 0.2.1 — 2026-11-11

- Default to an OLED-style black main window, dark controls, dark title bar and dark tray menu.
- Preserve the rendering and radio behavior from 0.2.
- Owner visually confirmed idle sleep still works on the Smooth build. Battery runtime, Windows login startup and PC suspend/resume remain unmeasured or untested.
- Build and existing offline/mock-runtime checks pass; visually verified the deployed dark window and OpenRGB reconnection.

## 0.2 — 2026-11-11

- Optional Smooth rendering: compact solid commands and complete frames with at most eight input colors. Preserve small palettes and black/nonblack boundaries.
- Coalesce incoming updates and plan only when due. Bound sustained lighting traffic to 70 reports/second; retain exact-color fallback.
- New window, executable/taskbar/tray keyboard icon, color picker, pause/resume, Exit and live report-rate/CPU/memory readings.
- Save rendering preference; preserve minimize/close-to-tray and --start-minimized.
- Reopen the existing 0.2 instance on second launch; add --exit.
- Pass 21,989 offline checks and five mock SDK/runtime checks without HID access.
- Owner visually confirmed the corrected animation is very smooth. At this release, battery runtime, idle sleep/wake and full login/suspend tests were pending; see the 0.2.1 follow-up above.

## 0.1 — 2026-11-08

- Standalone localhost SDK bridge for the specifically identified model-CD F75 Pro receiver.
- Complete-key atomic messages, per-key colors, reconnect and tray support.
- Owner confirmed sampled mapping, typing, solid colors and reconnect. Animations worked with noticeable choppiness. Owner later confirmed idle sleep.
