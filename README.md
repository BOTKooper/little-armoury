# Little Armoury

A small native Windows app for ASUS operating modes, fan curves and hardware stats.

![Little Armoury dashboard](docs/dashboard.png)

## Download

**[Download LittleArmoury.exe](https://github.com/botkooper/little-armoury/releases/latest/download/LittleArmoury.exe)** and run it. There is no installer.

You need Windows 10 or 11 and the ASUS System Control Interface driver. GPU readings need the NVIDIA driver.

## Controls

- CPU temperature, load, frequency estimate, fan RPM and configured memory speed.
- NVIDIA GPU temperature, load, core and memory clocks, fan RPM, actual power and current power limit.
- RAM and system-drive usage.
- Silent, Performance and Turbo ASUS presets. Turbo requires external power.
- Editable eight-point CPU and GPU fan curves, with a draggable graph and numeric inputs. Changes apply only when you click **Apply**. Choosing a preset restores factory fan behavior.
- Saved custom modes combining an ASUS preset, maximum CPU performance and a boost switch.
- System tray support. Minimize to the tray; double-click the icon to reopen. Close the window or choose **Quit** to exit.

## Custom modes

The CPU percentage controls Windows maximum processor performance. It is neither a CPU utilization limit nor a wattage limit. Lowering it can reduce game performance substantially. Disabling boost prevents above-base CPU boost.

A custom mode uses a temporary copy of the active Windows power plan. It sets the minimum CPU state to 5%, plus the selected maximum and boost policy, on both AC power and battery. The original plan is not edited. Choosing a normal ASUS preset or quitting restores the previous plan and removes the temporary copy. If you choose another plan in Windows yourself, the app respects that choice. An interrupted session is recovered on the next launch.

The included **Everyday** mode uses Performance, an 80% maximum CPU setting and boost disabled. Opening the app does not apply it. Profiles are selected manually; starting or closing a game does not switch them. Saved modes and the power-session recovery record live in `%LOCALAPPDATA%\LittleArmoury`.

## Hardware behavior

Tested on an ASUS ROG Strix G713RM with Ryzen 7 6800H, RTX 3060 Laptop GPU and BIOS G713RM.327. Other ASUS models have not been verified.

Keep the **ASUS System Control Interface** driver: it provides the direct firmware interface. Little Armoury does not call Armoury Crate APIs or require its service. A running Armoury Crate service can override modes; the app shows a notice when it detects it. Using the app after uninstalling Armoury Crate has not been tested.

The selected preset is the last one successfully requested in the current session. This laptop's firmware reports capabilities rather than its current mode, so the app does not infer a selection at startup. Another app can change the mode afterward.

GPU polling may keep the discrete GPU awake. Turn off **Sensors** in the GPU panel to release NVML. Polling is also released when the app is minimized. Missing readings show a dash. The GPU power limit is a live driver value, rather than a fixed advertised maximum. Actual clocks, temperatures and fan speeds depend on workload and firmware.

The Windows power link opens Windows power settings. The supported ASUS interface used here exposes Silent, Performance and Turbo, rather than Armoury Crate's proprietary Windows mode.

## Build and test

Use the compiler included with .NET Framework 4.8; no additional packages are needed:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1
```

The executable is written to `dist\LittleArmoury.exe`. The regular suite runs 62 assertions without writing firmware or Windows power settings. It covers curve parsing and validation, firmware request encoding, saved profiles, power-policy rollback and recovery, foreign-plan protection, rendering, and saving edits without applying them.

Optional live Windows power-policy verification adds nine assertions. It temporarily activates an 80% CPU / boost-off plan, checks AC/DC readback, then restores the original plan. Run it with Little Armoury closed:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -LivePowerPolicy
```

Read-only diagnostics:

```powershell
.\dist\LittleArmoury.exe --diagnostics diagnostics.txt
```

ASUS preset and fan-curve writes are not exercised by the automated suite.

## Implementation and license

Native WinForms and GDI drawing, using system fonts. ASUS protocol identifiers and buffer layout can be cross-checked against the Linux kernel's `drivers/platform/x86/asus-wmi.c` and `include/linux/platform_data/x86/asus-wmi.h`. This implementation does not ship another application's code or drivers.

[MIT license](LICENSE).
