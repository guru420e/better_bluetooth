# Better Bluetooth

A lightweight, modern Windows utility designed to solve persistent reconnect failures with Bluetooth Classic peripherals—especially third-party Switch Pro controllers (e.g., Cosmic Byte clones) and Classic audio devices—by automating clean unpairing, live HCI inquiry rediscovery, re-pairing, and event-driven **Auto-Forget & Auto-Pair**.

---

## 🎯 The Problem

Certain Bluetooth Classic HID controllers and peripherals (such as Nintendo Switch Pro controller clones) do not persist or properly present their Bluetooth link keys across power cycles, PC reboots, or sleep/resume transitions. 

When you turn off the controller or reboot your PC:
1. **Windows retains the paired bond** in its registry and driver stack.
2. When the controller powers on or enters pairing mode, **it presents a new or missing bond key**.
3. Windows rejects or ignores the connection because it expects the existing security key.
4. The user is trapped in an infinite reconnect failure loop: Windows reports the device as paired, but clicking "Connect" does nothing until the user manually dives into Windows Settings, deletes the device, and pairs it again from scratch.

**Better Bluetooth eliminates this friction entirely.**

---

## ✨ Features

- **Automated Event-Driven Auto-Forget Engine**:
  - Automatically unpairs problematic devices when they disconnect, leaving Windows clean and ready for instant re-pairing next time.
  - **Grace Period (20 seconds)**: Configurable grace countdown before unpairing triggers, preventing accidental disconnects during transient signal drops.
  - **Live Cancellation**: If the device reconnects or the user cancels during the countdown, the unpair event is instantly aborted.
  - **Startup Crash Recovery**: If the PC or app crashes during an active disconnect or before unpairing finishes, the engine inspects paired devices on next launch and automatically purges leftover stale bonds.
  - **Safety Blackouts**: Auto-forget will *never* fire if the device was not actively connected in the current session, if Bluetooth radio was toggled off, or during system sleep/wake transitions.
- **Hands-Free Auto-Pair Engine (`⚡`)**:
  - Automatically pairs with discoverable devices as soon as they are detected in pairing mode.
  - Easily toggle Auto-Pair for any device in the in-place Rules component.
  - Completes the entire autonomous cycle: **Disconnect -> Auto-Forget -> Hold Sync -> Auto-Pair -> Connected**.
- **React-Style In-Place Rules Component**:
  - Clicking the `⚙ Rules` chip or double-clicking any device card replaces the devices list with the Rules component directly in the flyout.
  - No popup dialogs and zero white scrollbars—uses the existing custom 5px dark scrollbar.
  - Each individual device manages its own **Auto-unpair on disconnect** and **Auto-pair on discovery** state directly.
- **Embedded ACID Database (LiteDB)**:
  - Device automation rules and global settings are persisted safely in an embedded LiteDB database (`%APPDATA%\BetterBluetooth\better_bluetooth.db`), guaranteeing atomic, crash-proof transactions without corrupted JSON files.
- **Live HCI Inquiry Rediscovery**:
  - Classic Bluetooth devices in pairing mode do not advertise like BLE; they enter "Inquiry Scan". The app restarts WinRT inquiry sweeps on demand to instantly pick up devices holding their sync button.
- **Sleek Windows 11 Dark Overlay**:
  - Borderless 440×600 flyout window anchored directly above the system tray.
  - Windows 11 rounded corners via Desktop Window Manager (`DwmSetWindowAttribute`).
  - **Streamlined Edge-to-Edge Layout**: Footer panel removed to maximize vertical card space (510px viewport).
  - **Custom 5px Dark Scrollbar**: Completely replaces legacy OS white scrollbars with a custom-rendered dark thumb matching `#14161A`.
  - **Zero-Blink Repainting**: Double-buffered fine-grained repainting (`ctrl.Invalidate()`) updates countdown timers and active badges smoothly without flickering.
- **Global Hotkey & Tray Integration**:
  - Press <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>B</kbd> from anywhere to toggle the overlay.
  - Runs silently in the system tray with a custom teal geometric Bluetooth rune icon and native Windows balloon notifications on auto-forget and auto-pair.
  - **Start with Windows**: Toggle autostart on login via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` directly from the tray context menu.
- **Audit Logging**:
  - Dual CSV audit logs written to `%APPDATA%\BetterBluetooth\discovery.csv` and `./discovery.csv` tracking timestamped phases, MACs, RSSI levels, and pairing transitions.
- **Interactive CLI Testing Harness**:
  - Built-in command-line test suites for discovery, pairing sequences, and disconnect verification.

---

## ⚙️ How Auto-Forget & Auto-Pair Work

The automation operates through an event-driven pipeline coordinated across three distinct layers:

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. UI Layer (MainForm.cs / React-like In-Place Rules Component) │
│    • User clicks "⚙ Rules · X" chip or double-clicks card       │
│    • In-place swap: replaces device list with Rules component   │
│    • Zero popup dialogs, zero white scrollbars (5px Dark Scroll)│
│    • Per-device Auto-Unpair & Auto-Pair toggles with ACID write │
└──────────────────────────────┬──────────────────────────────────┘
                               │ Dispatches state changes
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│ 2. Storage Layer (DatabaseContext.cs & AutoForgetStore.cs)      │
│    • Embedded LiteDB: %APPDATA%\BetterBluetooth\better_bluet... │
│    • Thread-safe ACID collection: device_rules & settings       │
│    • Automatic migration from legacy json stores                │
└──────────────────────────────┬──────────────────────────────────┘
                               │ Fires Changed event
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│ 3. Enforcement Engine (AutoForgetEngine.cs)                     │
│    • Detects disconnect -> starts 20s grace period -> unpairs   │
│    • Detects discoverable device -> auto-pairs via WinRT        │
│    • Cleans stale leftover crash pairings on startup            │
└─────────────────────────────────────────────────────────────────┘
```

### Individual Per-Device Automation
- **Per-Device Rules Component (In-Place Swap)**:
  - Configure **Auto-unpair on disconnect** and **Auto-pair on discovery** directly per device.
  - No global circuit breakers needed—each individual device owns and manages its state.
  - Devices with active rules display a subtle `[⚡ Auto]` teal badge in the main list.
  - All rules are persisted transactionally in embedded LiteDB.

---

## 🏗 System Architecture

The application is modularized into decoupled layers:

```
better_bluetooth/
├── Core/                              # Core domain and hardware integration
│   ├── Database/
│   │   ├── AppSettingsRecord.cs       # App settings document
│   │   ├── DatabaseContext.cs         # Thread-safe LiteDB context & migrations
│   │   └── DeviceRuleRecord.cs        # Per-device automation rules document
│   ├── Interop/
│   │   └── NativeMethods.cs           # Win32 P/Invoke (DWM corners, Hotkey, Console streams)
│   ├── Models/
│   │   └── BluetoothDeviceInfo.cs     # Immutable device record (MAC, RSSI, CoD, Paired, Connected)
│   └── Services/
│       ├── BluetoothService.cs        # WinRT DeviceWatcher (Classic AQS: {e0cbf06c-cd8b-4647-bb8a-263b43f0f974})
│       ├── AutoForgetEngine.cs        # Disconnect tracker, grace timer, session verification & unpairing
│       ├── AutoForgetStore.cs         # In-memory cached facade over LiteDB
│       └── StartupManager.cs          # Windows registry autostart manager (HKCU\...\Run)
├── UI/                                # Graphical user interface (WinForms .NET 8)
│   ├── Theme/
│   │   └── AppTheme.cs                # Design tokens (#14161A, #3DD6B0), fonts, button factories
│   ├── Components/
│   │   ├── DarkScrollBar.cs           # Custom 5px dark scrollbar control with mouse dragging
│   │   ├── HeaderPanel.cs             # Drag header, live status, and scan toggle button
│   │   └── DeviceCard.cs              # Nearby cards, My Devices cards, and inline Rule cards
│   ├── Forms/
│   │   └── MainForm.cs                # Borderless main flyout with in-place React-style view swapping
│   └── Tray/
│       └── AppContext.cs              # ApplicationContext, tray icon drawing & balloon notifications
├── CLI/                               # Interactive command-line test runners
│   ├── PairingTestRunner.cs           # Step-by-step unpair -> inquiry -> pair test (--pair-test)
│   └── AutoForgetTestRunner.cs        # Disconnect & grace countdown test (--autoforget-test)
├── Program.cs                         # Application entry point with CLI / GUI mode routing
├── publish.bat                        # 1-click single-file release build script
└── BetterBluetooth.csproj             # Target net8.0-windows10.0.19041.0 + LiteDB
```

---

## 🚀 Getting Started

### Prerequisites
- **Operating System**: Windows 10 (Version 2004 / Build 19041 or higher) or Windows 11.
- **SDK**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- **Hardware**: Bluetooth adapter supporting Classic Bluetooth (BR/EDR).

### Building

Clone the repository and build the project:

```powershell
git clone https://github.com/guru420e/better_bluetooth.git
cd better_bluetooth
dotnet build -c Release
```

### Running the GUI (Default)

Launch the background tray application:

```powershell
dotnet run
```

- Click the tray icon or press <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>B</kbd> to open the flyout.
- Click the **`⚙ Rules`** chip or double-click any device card to view the in-place **DEVICE RULES** component.
- Configure **Auto-unpair on disconnect** (`⏏`) or **Auto-pair on discovery** (`🔗`) using the icon toggle buttons with instant LiteDB persistence.
- Click **`← Devices`** or press <kbd>Esc</kbd> to return to the standard device list.
- Devices with active automation display an **`[⚡ Auto]`** teal badge; double-clicking any device card opens its rules directly.
- Right-click the system tray icon and check **"Start with Windows"** to auto-launch silently on login.
- To exit, right-click the tray icon and choose **Exit**.

### Single-File Standalone Publishing

To produce a self-contained single-file `.exe` that runs on any compatible Windows PC without requiring the .NET SDK:

```powershell
./publish.bat
```

The output executable is generated at:
```
publish\BetterBluetooth.exe
```

---

## 💻 CLI Test Modes

Better Bluetooth includes dedicated console test runners to diagnose and verify hardware pairing behavior without opening the UI:

### 1. Live Discovery Scanner (`--scan` or `-s`)
Runs an active HCI inquiry scan to monitor all nearby discoverable Bluetooth Classic devices and their RSSI signal strengths in real time:

```powershell
dotnet run -- --scan
```

### 2. Interactive Pairing & Reconnect Test (`--pair-test` or `-p`)
Interactively steps through programmatic unpairing, waits for the user to activate pairing mode, triggers an active inquiry burst, bonds via WinRT, and tests reconnection:

```powershell
dotnet run -- --pair-test
```

### 3. Auto-Forget Verification Test (`--autoforget-test` or `-a`)
Verifies session connection tracking, detects disconnection when you turn off the controller, displays a live 15-second grace countdown in the terminal, and executes automatic unpairing with audit log output:

```powershell
dotnet run -- --autoforget-test
```

---

## 🛡 Safety & Safeguards

- **Blacklist / Protection**: A safety guard prevents specific MAC addresses (e.g. bonded BLE mice and keyboards) from being unpinned or uncoupled.
- **Session Connection Verification**: Auto-forget only triggers for devices that were **actively connected** during the current session. Offline or out-of-range paired devices are never removed unexpectedly.
- **Radio & Power State Safeguards**:
  - Disconnections triggered by the user toggling off Bluetooth radio cancel all pending auto-forget timers.
  - Windows sleep / resume events (`SystemEvents.PowerModeChanged`) initiate a blackout window, preventing devices that drop during PC standby from being forgotten.

---

## 📂 Configuration & Data Paths

| File | Description | Location |
| :--- | :--- | :--- |
| **`better_bluetooth.db`** | ACID-compliant embedded LiteDB database (rules & settings) | `%APPDATA%\BetterBluetooth\better_bluetooth.db` |
| **`discovery.csv`** | CSV audit log containing timestamp, phase, MAC, RSSI, and state (dev) | `%APPDATA%\BetterBluetooth\discovery.csv`<br>`.\discovery.csv` |

```json
// Database Collections in %APPDATA%\BetterBluetooth\better_bluetooth.db:
// 1. device_rules: { "_id": "D0:56:80:B1:F0:03", "Name": "Pro Controller", "AutoForget": true, "AutoPair": true }
// 2. settings:     { "_id": "global", "MasterAutoForget": true, "MasterAutoPair": true, "StartWithWindows": true }
```

---

## 📜 License

Licensed under the [MIT License](LICENSE).
