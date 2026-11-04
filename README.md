# Buds Studio

> **Luxury Bluetooth Buds & Headphone Manager for Windows 10 / 11**  
> Fluid iOS-inspired popup cards, accurate hardware battery telemetry, dynamic tray percentage, and intelligent form-factor auto-detection.

---

## Overview

**Buds Studio** is a native, high-performance C# / WPF application engineered to bring smartphone-level battery popups and audio device management to Windows desktop.

Unlike standard Windows Bluetooth settings that frequently display delayed or inaccurate percentages and completely ignore charging cases, **Buds Studio** interfaces directly with low-level RFCOMM serial and Bluetooth Low Energy (BLE) advertisement protocols to provide real-time battery telemetry for both earbuds and their charging case.

---

## Key Features

### 1. Fluid iOS-Style Spring Animation
- Smooth drop-down card with quadratic easing and spring physics (`BackEase`).
- Animated circular progress gauges reflecting live battery levels.
- Non-intrusive floating HUD with customizable screen duration (2–12s) and vertical offset.

### 2. Live Hardware Battery Telemetry
- **realme / Oppo / OnePlus**: Low-level 35-byte RFCOMM serial telemetry protocol on dedicated virtual COM ports.
- **Apple AirPods / AirPods Pro / AirPods Max**: BLE proximity advertisement parser (`Company 0x004C`, Opcode `0x07`/`0x10`) active when paired with PC.
- **EDIFIER (W800BT Pro, W820NB, etc.)**: Proprietary command frame (`AA 01 D0 21 94`) over serial + Bluetooth GATT.
- **Universal Bluetooth Audio (Sony, JBL, Bose, Samsung)**: Standard Bluetooth GATT Battery Service (`0x180F`).

### 3. Smart Case Open Detection
- Correctly reports `—` when earbuds are in ears and the charging case is closed.
- Automatically detects when earbuds are placed back into an open case, immediately surfacing case battery percentage in the desktop popup.

### 4. Background System Tray Indicator
- Dynamic tray icon rendering live two-digit or three-digit battery percentage directly on taskbar.
- Detailed tooltip on hover (`L: 80% | R: 80% | Case: —`).
- Single-instance background service with zero idle CPU overhead.

### 5. Automatic Form-Factor Detection
- Automatically identifies whether connected hardware is **TWS In-Ear** (Left, Case, Right layout) or **Wireless Over-Ear** (single headband/earcup layout).
- Zero manual switching required.

### 6. Studio Customization Suite
- **Interactive Live Preview**: Real-time feedback for all visual changes.
- **Fullscreen & Windowed Support**: Dedicated toggle for immersive view on high-resolution displays.
- **Themes**: Apple Porcelain Light and Graphite Obsidian Dark.
- **Accent Palettes**: Royal Blue, Graphite, Pearl White, Emerald, Sunset Orange, Cyber Purple, Ruby Red.

---

## Supported Devices

| Manufacturer | Models | Protocol | Features |
|---|---|---|---|
| **realme** | Buds T500 Pro, Buds Air 3/5/6, Buds T300 | RFCOMM Serial / BLE | Left, Right, Case battery, Open-case trigger |
| **Apple** | AirPods Pro (1st & 2nd Gen), AirPods (1-3), AirPods Max | BLE Proximity (0x004C) | Discrete L/R/Case %, charging flags |
| **EDIFIER** | W800BT Pro, W820NB, WH950NB, W240TN | Serial SPP / GATT | Over-Ear layout, battery level |
| **Universal** | Sony, JBL, Bose, Samsung Galaxy Buds, Soundcore | Standard GATT 0x180F | Single/dual battery, auto over-ear detection |

---

## Quick Start

### Running Pre-compiled Executable
1. Download the standalone executable from the [Releases](https://github.com/TheFlipper-spec/Buds-Studio/releases) tab: `RealmeBudsPopup.exe`.
2. Launch the `.exe`. The Buds Studio customization window will appear.
3. Connect your headphones via Windows Bluetooth.
4. The popup card will automatically glide in from the top of your screen.

### Autostart on Windows Boot
- Toggle **«Автозапуск приложения вместе с Windows»** in the Buds Studio settings window, or run `install-autostart.bat`.

---

## Building from Source

### Prerequisites
- Windows 10 (19041+) or Windows 11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Compilation
```powershell
# Clone repository
git clone https://github.com/TheFlipper-spec/Buds-Studio.git
cd Buds-Studio

# Build in Debug mode
dotnet build

# Publish single-file standalone Release executable
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

---

## License

Distributed under the MIT License. See `LICENSE` for details.
