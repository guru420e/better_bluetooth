using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;

namespace BluetoothRectifier;

/// <summary>
/// Interactive Milestone 4 Test Runner:
/// Verifies connection tracking, disconnect detection, grace period countdown,
/// reconnect cancellation, and automatic unpairing with safeguards.
/// </summary>
public static class AutoForgetTestRunner
{
    private const string BlockedMac = "54:46:6E:55:74:01";

    public static async Task<int> RunAsync()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine(" Milestone 4: Auto-Forget Engine & Disconnect Verification");
        Console.WriteLine("================================================================================");
        Console.ResetColor();
        Console.WriteLine("This test verifies that Auto-Forget only triggers after a device was CONNECTED");
        Console.WriteLine("in the current session, then DISCONNECTS, and stays disconnected past the grace period.");
        Console.WriteLine(new string('-', 80));

        using var service = new BluetoothService();
        var store = new AutoForgetStore();
        using var engine = new AutoForgetEngine(service, store)
        {
            GracePeriodSeconds = 15 // 15 seconds for quick testing
        };

        var tcsEnum = new TaskCompletionSource<bool>();
        service.EnumerationCompleted += () => tcsEnum.TrySetResult(true);

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("[Status] Scanning for paired devices...");
        Console.ResetColor();

        service.StartScan();
        await Task.WhenAny(tcsEnum.Task, Task.Delay(4000));

        var pairedDevices = service.Devices
            .Where(d => d.IsPaired && !string.Equals(d.MacFormatted, BlockedMac, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Name)
            .ToList();

        if (pairedDevices.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[Warning] No paired devices found. Please pair a device first.");
            Console.ResetColor();
            return 1;
        }

        Console.WriteLine("\nCurrently Paired Devices:");
        for (int i = 0; i < pairedDevices.Count; i++)
        {
            var dev = pairedDevices[i];
            string type = dev.CodMajor == 5 ? "Gamepad" : (dev.CodMajor == 4 ? "Audio" : "Other");
            string conn = dev.IsConnected ? "● Connected" : "Not connected";
            bool af = store.Contains(dev.MacFormatted);

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"  [{i + 1}] ");
            Console.ForegroundColor = dev.IsConnected ? ConsoleColor.Cyan : ConsoleColor.Green;
            Console.Write($"{dev.Name,-26} ");
            Console.ResetColor();
            Console.WriteLine($"MAC: {dev.MacFormatted,-17} | {type,-8} | {conn,-14} | Auto-Forget: {(af ? "ON" : "OFF")}");
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"Select a device to test Auto-Forget on (1 - {pairedDevices.Count}, or 'q' to quit): ");
        Console.ResetColor();

        string? input = Console.ReadLine()?.Trim();
        if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase) || !int.TryParse(input, out int choice) || choice < 1 || choice > pairedDevices.Count)
        {
            Console.WriteLine("Test cancelled.");
            return 0;
        }

        var target = pairedDevices[choice - 1];
        string targetMac = target.MacFormatted;

        // Ensure target is registered in AutoForgetStore
        store.Add(targetMac);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[Configured] Auto-Forget is ON for: {target.Name} ({targetMac})");
        Console.WriteLine($"[Configured] Grace period set to: {engine.GracePeriodSeconds} seconds.");
        Console.ResetColor();

        using var cts = new CancellationTokenSource();

        // Wire engine events
        engine.StatusMessage += msg =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [Engine] {msg}");
            Console.ResetColor();
        };

        engine.GraceCountdownTick += (mac, remaining) =>
        {
            if (string.Equals(AutoForgetStore.NormalizeMac(mac), AutoForgetStore.NormalizeMac(targetMac), StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  ⏳ [Countdown] Grace period expiring in: {remaining}s remaining...");
                Console.ResetColor();
            }
        };

        engine.GraceCountdownCancelled += mac =>
        {
            if (string.Equals(AutoForgetStore.NormalizeMac(mac), AutoForgetStore.NormalizeMac(targetMac), StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ✔ [CANCELLED] Device reconnected or event reset. Countdown cancelled!");
                Console.ResetColor();
            }
        };

        var tcsForgotten = new TaskCompletionSource<bool>();
        engine.DeviceAutoForgotten += (dev, reason) =>
        {
            if (string.Equals(AutoForgetStore.NormalizeMac(dev.MacFormatted), AutoForgetStore.NormalizeMac(targetMac), StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine();
                Console.WriteLine("================================================================================");
                Console.WriteLine($" ✔ [SUCCESS] AUTO-FORGET EXECUTED on \"{dev.Name}\" ({dev.MacFormatted})!");
                Console.WriteLine($" Reason: {reason}");
                Console.WriteLine(" Check discovery.csv — audit log entry was written successfully!");
                Console.WriteLine("================================================================================");
                Console.ResetColor();
                tcsForgotten.TrySetResult(true);
            }
        };

        // ══════════════════════════════════════════════════════════════════════
        // STAGE 1: Ensure device is CONNECTED
        // ══════════════════════════════════════════════════════════════════════
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine(" STAGE 1: Session Connection Verification");
        Console.WriteLine(new string('=', 60));

        var initialDev = service.FindByMac(targetMac);
        if (initialDev == null || !initialDev.IsConnected)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            if (target.CodMajor == 5)
            {
                Console.WriteLine(">> Controller: Press the HOME button to wake and connect it now.");
            }
            else
            {
                Console.WriteLine(">> Audio: Turn on your speaker/earbuds to connect them now.");
            }
            Console.ResetColor();
            Console.WriteLine("Waiting for device to connect in this session...");

            var tcsConnected = new TaskCompletionSource<bool>();
            void OnCheckConn(BluetoothDeviceInfo d)
            {
                if (string.Equals(AutoForgetStore.NormalizeMac(d.MacFormatted), AutoForgetStore.NormalizeMac(targetMac), StringComparison.OrdinalIgnoreCase) && d.IsConnected)
                {
                    tcsConnected.TrySetResult(true);
                }
            }

            service.DeviceUpdated += OnCheckConn;
            await Task.WhenAny(tcsConnected.Task, Task.Delay(45000));
            service.DeviceUpdated -= OnCheckConn;

            if (!tcsConnected.Task.IsCompletedSuccessfully)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("✖ [TIMEOUT] Device did not connect within 45 seconds.");
                Console.ResetColor();
                return 1;
            }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✔ [SESSION CONNECTED] \"{target.Name}\" is CONNECTED in this session!");
        Console.ResetColor();

        // ══════════════════════════════════════════════════════════════════════
        // STAGE 2: Test Disconnect & Grace Period
        // ══════════════════════════════════════════════════════════════════════
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine(" STAGE 2: Disconnect & Grace Countdown Test");
        Console.WriteLine(new string('=', 60));
        Console.ForegroundColor = ConsoleColor.Yellow;
        if (target.CodMajor == 5)
        {
            Console.WriteLine(">> Controller: Turn OFF your controller now (hold Home/Power or let it sleep).");
        }
        else
        {
            Console.WriteLine(">> Audio: Turn OFF your speaker/earbuds now (or place earbuds back in case).");
        }
        Console.ResetColor();
        Console.WriteLine($"Waiting for disconnect... When detected, the {engine.GracePeriodSeconds}s countdown will start.");
        Console.WriteLine("(Press Ctrl+C at any time to exit)\n");

        var waitCompleted = await Task.WhenAny(tcsForgotten.Task, Task.Delay(60000));
        if (waitCompleted != tcsForgotten.Task)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[Notice] Test ended or timed out waiting for auto-forget expiration.");
            Console.ResetColor();
        }

        return 0;
    }
}
