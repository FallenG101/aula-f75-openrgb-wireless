# Changelog

## 0.2.3-rc.1 — 2026-11-11

- Restore animation throughput using one-shot high-resolution Windows waits, with no busy-spin loop or global timer-resolution change.
- Enforce at least 15 ms between report starts and 15 ms quiet after frames; cap sustained lighting traffic at 60 reports/s and solids at 10 frames/s.
- Retain the dark interface, saved Smooth rendering, pause/resume, --start-minimized and --start-paused.
- Pass 22,047 offline checks and 113 mock runtime checks in the validated run. Mock eight-report throughput measured 7.48 frames/s; this is not a hardware frame-rate measurement.
- Owner reported very smooth animation and normal typing/backspacing after charging. One prior repeated-key freeze has not recurred in the short check; very low battery is a possible cause. This remains an unconfirmed issue, not a proven software defect or fix.

## 0.2.2-rc.1 — 2026-11-11

- Enforce minimum 20 ms report-start spacing and 25 ms frame quiet time; cap sustained lighting traffic at 40 reports/s and whole-board solids at 10 updates/s.
- Preserve Smooth palette reduction and the OLED-style interface. Animation can be slower than 0.2.1.
- Add --start-paused and rotating local aggregate timing diagnostics, without recording keystrokes or typed text.
- Pass 22,047 offline assertions and 43 mock runtime assertions in the validated run, including no short bursts and inter-frame quiet time.
- Investigated one repeated-key lockup on a USB hub. Owner reported a very low battery as another possible cause. A short typing check passed on the same hub; root cause and long-term resolution remain unconfirmed.
- Experimental prerelease; battery runtime and login/suspend testing remain pending.

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
