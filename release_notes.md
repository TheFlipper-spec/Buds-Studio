## Buds Studio v1.0.0 Pro

### What's New
- **Custom App Icon**: Embedded multi-resolution application icon (256x256, 128, 64, 48, 32, 16) for Windows Explorer, window title bars, and system tray.
- **Dynamic iOS-Style Popups**: Fluid spring animations with circular battery progress rings on Bluetooth connection.
- **Hardware Telemetry Protocols**:
  - `realme Buds T500 Pro` / `Oppo` / `OnePlus`: Live 35-byte RFCOMM serial battery telemetry.
  - `Apple AirPods Pro / AirPods Max`: Strict BLE Proximity advertisement parsing with discrete L/R/Case battery.
  - `EDIFIER (W800BT Pro, W820NB, etc.)`: Proprietary serial command frame query + GATT Battery fallback.
  - `Universal Bluetooth Headsets (Sony, JBL, Bose, Samsung)`: Standard GATT Battery Service (0x180F).
- **Smart Case Detection**: Accurately shows `—` when in ears, and pops up with live percentage when buds are in an open case.
- **System Tray Percentage**: Live two/three-digit battery number rendered on taskbar icon.
- **Smart Single Instance**: Double-clicking the `.exe` activates and brings the running window to the foreground instead of failing.
- **Studio Customization Suite**:
  - Porcelain Light & Obsidian Dark themes.
  - 7 accent color palettes.
  - Live interactive preview.
  - Windowed and Fullscreen modes.
  - Sliders for on-screen duration and vertical offset.

### Downloads
- **Windows (10/11 x64)**: [`RealmeBudsPopup.exe`](https://github.com/TheFlipper-spec/Buds-Studio/releases/download/v1.0.0/RealmeBudsPopup.exe) — Standalone single-file executable, zero runtime installation required.
- **Android (API 24+)**: [`Buds-Studio-v1.0.0.apk`](https://github.com/TheFlipper-spec/Buds-Studio/releases/download/v1.0.0/Buds-Studio-v1.0.0.apk) — Jetpack Compose native app with background service and floating HUD overlay.
