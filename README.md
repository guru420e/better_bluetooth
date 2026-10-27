# Bluetooth Rectifier

A lightweight, modern Windows utility designed to solve persistent reconnect failures with Bluetooth Classic peripherals—especially third-party Switch Pro controllers (e.g., Cosmic Byte clones) and Classic audio devices—by automating clean unpairing, live HCI inquiry rediscovery, re-pairing, and event-driven **Auto-Forget**.

---

## 🎯 The Problem

Certain Bluetooth Classic HID controllers and peripherals (such as Nintendo Switch Pro controller clones) do not persist or properly present their Bluetooth link keys across power cycles, PC reboots, or sleep/resume transitions. 

When you turn off the controller or reboot your PC:
1. **Windows retains the paired bond** in its registry and driver stack.
2. When the controller powers on or enters pairing mode, **it presents a new or missing bond key**.
3. Windows rejects or ignores the connection because it expects the existing security key.
4. The user is trapped in an infinite reconnect failure loop: Windows reports the device as paired, but clicking "Connect" does nothing until the user manually dives into Windows Settings, deletes the device, and pairs it again from scratch.

**Bluetooth Rectifier eliminates this friction entirely.**

---

## ✨ Features

- **Automated Event-Driven Auto-Forget Engine**:
  - Automatically unpairs problematic devices when they disconnect, leaving Windows clean and ready for instant re-pairing next time.
  - **Grace Period (20 seconds)**: Configurable grace countdown before unpairing triggers, preventing accidental disconnects during transient signal drops.
  - **Cancellation Safeguards**: If the device reconnects during the countdown, the unpair event is instantly cancelled.
  - **Safety Blackouts**: Auto-forget will *never* fire if the device was not actively connected in the current session, if Bluetooth radio was toggled off, or during system sleep/wake transitions.
- **Live HCI Inquiry Rediscovery**:
  - Classic Bluetooth devices in pairing mode do not advertise like BLE; they enter "Inquiry Scan". The app restarts WinRT inquiry sweeps on demand to instantly pick up devices holding their sync button.
- **Sleek Windows 11 Dark Overlay**:
  - Borderless 440×600 flyout window anchored directly above the system tray.
  - Windows 11 rounded corners via Desktop Window Manager (`DwmSetWindowAttribute`).
  - **Custom 5px Dark Scrollbar**: Completely replaces legacy OS white scrollbars with a custom-rendered dark thumb matching `#14161A`.
  - **Zero-Blink Countdown UI**: Double-buffered fine-grained repainting (`ctrl.Invalidate()`) updates countdown timers without redrawing controls or flickering.
- **Global Hotkey & Tray Integration**:
  - Press <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>B</kbd> from anywhere to toggle the overlay.
  - Runs silently in the system tray with a custom teal geometric Bluetooth rune icon and native Windows balloon notifications on auto-forget.
- **Audit Logging**:
  - Dual CSV audit logs written to `%APPDATA%\BluetoothRectifier\discovery.csv` and `./discovery.csv` tracking timestamped phases, MACs, RSSI levels, and pairing transitions.
- **Interactive CLI Testing Harness**:
  - Built-in command-line test suites for discovery, pairing sequences, and disconnect verification.

---

## 🏗 System Architecture

The application is modularized into three decoupled layers:

```
bluetooth_rectifier/
├── Core/                              # Core domain and hardware integration
│   ├── Interop/
│   │   └── NativeMethods.cs           # Win32 P/Invoke (DWM corners, Hotkey, Console streams)
│   ├── Models/
│   │   └── BluetoothDeviceInfo.cs     # Immutable device record (MAC, RSSI, CoD, Paired, Connected)
│   └── Services/
│       ├── BluetoothService.cs        # WinRT DeviceWatcher (Classic AQS: {e0cbf06c-cd8b-4647-bb8a-263b43f0f974})
│       ├── AutoForgetEngine.cs        # Disconnect tracker, grace timer, session verification & unpairing
│       └── AutoForgetStore.cs         # Thread-safe persistent JSON store (%APPDATA%\autoforget.json)
├── UI/                                # Graphical user interface (WinForms .NET 8)
│   ├── Theme/
│   │   └── AppTheme.cs                # Design tokens (#14161A, #3DD6B0), fonts, button factories
│   ├── Components/
│   │   ├── DarkScrollBar.cs           # Custom 5px dark scrollbar control with mouse dragging
│   │   ├── HeaderPanel.cs             # Drag-enabled header, live status, and scan toggle button
│   │   ├── FooterPanel.cs             # Bottom divider, status bar & auto-fix checkbox
│   │   └── DeviceCard.cs              # Double-buffered device card with ellipsis text & toggle
│   ├── Forms/
│   │   └── MainForm.cs                # Clean orchestrator form managing layout & events
│   └── Tray/
│       └── AppContext.cs              # ApplicationContext, tray icon drawing & balloon notifications
├── CLI/                               # Interactive command-line test runners
│   ├── PairingTestRunner.cs           # Step-by-step unpair -> inquiry -> pair test (--pair-test)
│   └── AutoForgetTestRunner.cs        # Disconnect & grace countdown test (--autoforget-test)
├── Program.cs                         # Application entry point with CLI / GUI mode routing
└── BluetoothRectifier.csproj          # Target net8.0-windows10.0.19041.0
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
git clone https://github.com/your-username/bluetooth_rectifier.git
cd bluetooth_rectifier
dotnet build -c Release
```

### Running the GUI (Default)

Launch the background tray application:

```powershell
dotnet run
```

- Click the tray icon or press <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>B</kbd> to open the flyout.
- Click the **`≡×`** button on any paired device card to enable **Auto-Forget**.
- Check **"Fix stale pairings automatically"** in the footer to toggle the master engine.
- Right-click the system tray icon and check **"Start with Windows"** to auto-launch silently on login.
- To exit, right-click the tray icon and choose **Exit**.

### Single-File Publishing

To build a standalone single-file `.exe` without needing the .NET SDK to run it:

```powershell
./publish.bat
```
The output executable will be created at `./publish/BluetoothRectifier.exe`.

---

## 💻 CLI Test Modes

Bluetooth Rectifier includes dedicated console test runners to diagnose and verify hardware pairing behavior without opening the UI:

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
| **`autoforget.json`** | Persistent list of MAC addresses marked for auto-forget | `%APPDATA%\BluetoothRectifier\autoforget.json` |
| **`discovery.csv`** | CSV audit log containing timestamp, phase, MAC, RSSI, and state | `%APPDATA%\BluetoothRectifier\discovery.csv`<br>`.\discovery.csv` |

---

## 📜 License

Licensed under the [MIT License](LICENSE).
