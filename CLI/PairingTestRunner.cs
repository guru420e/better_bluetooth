using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using BetterBluetooth.Core.Models;
using BetterBluetooth.Core.Services;

namespace BetterBluetooth.CLI;

/// <summary>
/// Interactive Milestone 3 Test Harness:
/// Tests programmatic Unpair, Live Discovery, Pair (standard & custom fallback), and Reconnection.
/// </summary>
public static class PairingTestRunner
{
    private const string BlockedMac = "54:46:6E:55:74:01";

    public static async Task<int> RunAsync()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine(" Milestone 3: Bluetooth Unpair, Pair & Reconnect Test");
        Console.WriteLine("================================================================================");
        Console.ResetColor();
        Console.WriteLine("This tool tests WinRT programmatic unpairing and pairing step-by-step.");
        Console.WriteLine("Recommendation: Test on your earbuds or speaker first, then the controller.");
        Console.WriteLine(new string('-', 80));

        using var service = new BluetoothService();
        service.StatusMessage += msg =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [WinRT] {msg}");
            Console.ResetColor();
        };

        var tcsEnum = new TaskCompletionSource<bool>();
        service.EnumerationCompleted += () => tcsEnum.TrySetResult(true);

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("[Status] Scanning for currently paired devices...");
        Console.ResetColor();

        service.StartScan();

        // Wait up to 5s for initial enumeration
        await Task.WhenAny(tcsEnum.Task, Task.Delay(5000));

        var pairedDevices = service.Devices
            .Where(d => d.IsPaired && !string.Equals(d.MacFormatted, BlockedMac, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Name)
            .ToList();

        if (pairedDevices.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[Warning] No paired Bluetooth Classic devices found on this system.");
            Console.ResetColor();
            return 1;
        }

        Console.WriteLine("\nCurrently Paired Devices:");
        for (int i = 0; i < pairedDevices.Count; i++)
        {
            var dev = pairedDevices[i];
            string typeLabel = dev.CodMajor == 5 ? "Gamepad/Peripheral" : (dev.CodMajor == 4 ? "Audio" : "Other");
            string connLabel = dev.IsConnected ? "Connected" : "Not connected";

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"  [{i + 1}] ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{dev.Name,-26} ");
            Console.ResetColor();
            Console.WriteLine($"MAC: {dev.MacFormatted,-17} | {typeLabel,-18} | {connLabel}");
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"Select a device to test (1 - {pairedDevices.Count}, or 'q' to quit): ");
        Console.ResetColor();

        string? input = Console.ReadLine()?.Trim();
        if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase) || !int.TryParse(input, out int choice) || choice < 1 || choice > pairedDevices.Count)
        {
            Console.WriteLine("Test cancelled.");
            return 0;
        }

        var target = pairedDevices[choice - 1];
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[Selected Target] {target.Name} (MAC: {target.MacFormatted}, Type: {target.CodLabel})");
        Console.ResetColor();

        // Safety rule: never touch protected BLE MAC
        if (string.Equals(target.MacFormatted, BlockedMac, StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Blocked] Security rule: target MAC is protected and will not be touched.");
            Console.ResetColor();
            return 1;
        }

        // ══════════════════════════════════════════════════════════════════════
        // STEP 1: TEST UNPAIR
        // ══════════════════════════════════════════════════════════════════════
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine(" STEP 1: Programmatic Unpair Test (UnpairAsync)");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine($"Calling UnpairAsync() on \"{target.Name}\" ({target.MacFormatted})...");

        var sw = Stopwatch.StartNew();
        var unpairResult = await service.UnpairDeviceAsync(target.Id);
        sw.Stop();

        Console.WriteLine($"Unpair finished in {sw.ElapsedMilliseconds} ms. Result: {unpairResult}");

        if (unpairResult == DeviceUnpairingResultStatus.Unpaired || unpairResult == DeviceUnpairingResultStatus.AlreadyUnpaired)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✔ [SUCCESS] \"{target.Name}\" has been cleanly removed from Windows!");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✖ [FAILED] Unpair returned: {unpairResult}");
            Console.ResetColor();
            Console.WriteLine("Stopping test.");
            return 1;
        }

        // Purge old cached reference and wait 2 seconds for stack cleanup
        service.ForgetDevice(target.Id);
        await Task.Delay(2000);

        // ══════════════════════════════════════════════════════════════════════
        // STEP 2: WAIT FOR USER TO ACTIVATE PAIRING MODE
        // ══════════════════════════════════════════════════════════════════════
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine(" STEP 2: Put Device into Pairing Mode");
        Console.WriteLine(new string('=', 60));
        Console.ForegroundColor = ConsoleColor.Yellow;
        if (target.CodMajor == 5)
        {
            Console.WriteLine(">> For Controller: HOLD THE SYNC BUTTON on the controller until its LEDs cycle rapidly back and forth.");
        }
        else
        {
            Console.WriteLine(">> For Audio/Speaker/Earbuds: Put your device into PAIRING MODE now.");
            Console.WriteLine("   (e.g., hold power/Bluetooth button until you hear 'Pairing' or the LED flashes rapidly)");
        }
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("\nPress [ENTER] only AFTER the device is actively in pairing mode...");
        Console.ResetColor();
        Console.ReadLine();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("[Discovery] Starting fresh Bluetooth HCI inquiry scan...");
        Console.ResetColor();

        var tcsFound = new TaskCompletionSource<BluetoothDeviceInfo>();
        string targetMacNorm = AutoForgetStore.NormalizeMac(target.MacFormatted);

        void OnDiscoveredOrUpdated(BluetoothDeviceInfo d)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"    [Nearby Inquiry] {d.Name,-22} | MAC: {d.MacFormatted,-17} | RSSI: {d.Rssi?.ToString() ?? "N/A"} dBm");
            Console.ResetColor();

            if (string.Equals(AutoForgetStore.NormalizeMac(d.MacFormatted), targetMacNorm, StringComparison.OrdinalIgnoreCase))
            {
                tcsFound.TrySetResult(d);
            }
        }

        service.DeviceDiscovered += OnDiscoveredOrUpdated;
        service.DeviceUpdated    += OnDiscoveredOrUpdated;

        // Restart scan to trigger fresh HCI inquiry burst from Windows Bluetooth radio
        service.RestartScan();

        var completedTask = await Task.WhenAny(tcsFound.Task, Task.Delay(35000));
        service.DeviceDiscovered -= OnDiscoveredOrUpdated;
        service.DeviceUpdated    -= OnDiscoveredOrUpdated;

        if (completedTask != tcsFound.Task)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✖ [TIMEOUT] Could not detect \"{target.Name}\" in live pairing mode within 30 seconds.");
            Console.ResetColor();
            Console.WriteLine("Make sure the device is turned on and in pairing mode (LED blinking).");
            return 1;
        }

        var liveDevice = await tcsFound.Task;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✔ [FOUND LIVE] Device advertising detected!");
        Console.ResetColor();
        Console.WriteLine($"    Name: {liveDevice.Name}");
        Console.WriteLine($"    MAC:  {liveDevice.MacFormatted}");
        Console.WriteLine($"    RSSI: {(liveDevice.Rssi.HasValue ? $"{liveDevice.Rssi.Value} dBm" : "N/A")}");
        Console.WriteLine($"    Id:   {liveDevice.Id}");

        // ══════════════════════════════════════════════════════════════════════
        // STEP 3: PROGRAMMATIC PAIRING
        // ══════════════════════════════════════════════════════════════════════
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine(" STEP 3: Programmatic Pairing Test (PairAsync)");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine($"Calling PairDeviceAsync() on \"{liveDevice.Name}\" ({liveDevice.Id})...");

        sw.Restart();
        var pairResult = await service.PairDeviceAsync(liveDevice.Id);
        sw.Stop();

        Console.WriteLine($"Pairing call completed in {sw.ElapsedMilliseconds} ms. Result: {pairResult}");

        if (pairResult == DevicePairingResultStatus.Paired || pairResult == DevicePairingResultStatus.AlreadyPaired)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✔ [SUCCESS] \"{liveDevice.Name}\" is now paired and bonded with Windows!");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✖ [FAILED] Pairing failed with status: {pairResult}");
            Console.ResetColor();
            return 1;
        }

        // ══════════════════════════════════════════════════════════════════════
        // STEP 4: RECONNECTION VERIFICATION
        // ══════════════════════════════════════════════════════════════════════
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine(" STEP 4: Reconnection Verification");
        Console.WriteLine(new string('=', 60));

        if (target.CodMajor == 5)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(">> Controller Reconnect Test:");
            Console.WriteLine("   Press the HOME / GUIDE button on the controller now to wake it.");
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine("Waiting for Windows audio service to establish connection...");
        }

        Console.WriteLine("Monitoring connection status for 15 seconds...");

        var tcsConnected = new TaskCompletionSource<bool>();
        void OnCheckConnected(BluetoothDeviceInfo d)
        {
            if (string.Equals(AutoForgetStore.NormalizeMac(d.MacFormatted), targetMacNorm, StringComparison.OrdinalIgnoreCase) && d.IsConnected)
            {
                tcsConnected.TrySetResult(true);
            }
        }

        service.DeviceDiscovered += OnCheckConnected;
        service.DeviceUpdated    += OnCheckConnected;

        var connTask = await Task.WhenAny(tcsConnected.Task, Task.Delay(15000));
        service.DeviceDiscovered -= OnCheckConnected;
        service.DeviceUpdated    -= OnCheckConnected;

        if (connTask == tcsConnected.Task)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✔ [CONNECTED] Device is actively connected! ConnectionStatus: True");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[NOTE] Device is paired in Windows, but idle/not connected.");
            if (target.CodMajor == 5)
            {
                Console.WriteLine("For Switch Pro controllers: Make sure player indicator LED is solid (not blinking).");
            }
            Console.ResetColor();
        }

        Console.WriteLine("\n" + new string('=', 80));
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(" Milestone 3 Test Completed Successfully!");
        Console.ResetColor();
        Console.WriteLine(new string('=', 80));
        return 0;
    }
}
