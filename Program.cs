using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace BluetoothRectifier;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // ── CLI / Console Test Mode ─────────────────────────────────────────
        if (args.Length > 0 && (args[0].Equals("--autoforget-test", StringComparison.OrdinalIgnoreCase) ||
                                args[0].Equals("-a", StringComparison.OrdinalIgnoreCase)))
        {
            return RunAutoForgetTest();
        }

        if (args.Length > 0 && (args[0].Equals("--pair-test", StringComparison.OrdinalIgnoreCase) ||
                                args[0].Equals("-p", StringComparison.OrdinalIgnoreCase)))
        {
            return RunPairTest();
        }

        if (args.Length > 0 && (args[0].Equals("--scan", StringComparison.OrdinalIgnoreCase) ||
                                args[0].Equals("-s", StringComparison.OrdinalIgnoreCase) ||
                                args[0].Equals("--test", StringComparison.OrdinalIgnoreCase)))
        {
            return RunConsoleScanner(args);
        }

        // ── GUI / Tray Mode (Default) ───────────────────────────────────────
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.Run(new AppContext());
        return 0;
    }

    private static int RunAutoForgetTest()
    {
        bool attached = NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);
        if (!attached)
        {
            NativeMethods.AllocConsole();
        }

        RebindConsoleStreams();

        int exitCode = AutoForgetTestRunner.RunAsync().GetAwaiter().GetResult();
        NativeMethods.FreeConsole();
        return exitCode;
    }

    private static int RunPairTest()
    {
        bool attached = NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);
        if (!attached)
        {
            NativeMethods.AllocConsole();
        }

        RebindConsoleStreams();

        int exitCode = PairingTestRunner.RunAsync().GetAwaiter().GetResult();
        NativeMethods.FreeConsole();
        return exitCode;
    }

    private static int RunConsoleScanner(string[] args)
    {
        bool attached = NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);
        if (!attached)
        {
            NativeMethods.AllocConsole();
        }

        RebindConsoleStreams();

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine(" Bluetooth Classic Rectifier — Milestone 2 Discovery Test");
        Console.WriteLine("================================================================================");
        Console.ResetColor();

        string localCsv = Path.Combine(Directory.GetCurrentDirectory(), "discovery.csv");
        using var service = new BluetoothService(localCsv);

        Console.WriteLine($"[Config] Logging to: {service.CsvPath}");
        Console.WriteLine($"[Config] Protocol:   Classic ({BluetoothService.ClassicProtocolId})");
        Console.WriteLine($"[Config] Press Ctrl+C or Enter to exit.");
        Console.WriteLine(new string('-', 80));

        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        service.StatusMessage += msg =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[Status] {msg}");
            Console.ResetColor();
        };

        service.DeviceDiscovered += dev =>
        {
            if (dev.Phase == "known")
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("[KNOWN] ");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("[LIVE ] ");
            }

            Console.ResetColor();
            Console.WriteLine($"{dev.Name,-28} | MAC: {dev.MacFormatted,-17} | Paired: {dev.IsPaired,-5} | Conn: {dev.IsConnected,-5} | RSSI: {FormatRssi(dev.Rssi),-4} | {dev.CodLabel}");
        };

        service.DeviceUpdated += dev =>
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"  └─ [UPDATE] {dev.Name,-24} | Conn: {dev.IsConnected,-5} | RSSI: {FormatRssi(dev.Rssi),-4}");
            Console.ResetColor();
        };

        service.DeviceRemoved += id =>
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine($"  └─ [REMOVED] Device Id: {id}");
            Console.ResetColor();
        };

        service.EnumerationCompleted += () =>
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(new string('-', 80));
            Console.WriteLine($"[Ready] Enumeration completed! Total known devices: {service.Devices.Count}");
            Console.WriteLine("        Now listening for LIVE discoverable devices nearby...");
            Console.WriteLine("        (Put your earbuds or controller into pairing mode to test live scan)");
            Console.WriteLine(new string('-', 80));
            Console.ResetColor();
        };

        service.StartScan();

        // Wait on user cancel or enter
        var inputTask = Task.Run(() =>
        {
            try { Console.ReadLine(); }
            catch { }
            cts.Cancel();
        });

        cts.Token.WaitHandle.WaitOne();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[Exit] Stopping discovery...");
        Console.ResetColor();

        service.StopScan();
        NativeMethods.FreeConsole();
        return 0;
    }

    private static void RebindConsoleStreams()
    {
        try
        {
            var hOut = NativeMethods.CreateFile("CONOUT$", NativeMethods.GENERIC_WRITE, NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
            if (hOut != IntPtr.Zero && hOut != new IntPtr(-1))
            {
                NativeMethods.SetStdHandle(NativeMethods.STD_OUTPUT_HANDLE, hOut);
                NativeMethods.SetStdHandle(NativeMethods.STD_ERROR_HANDLE, hOut);
                var safeOut = new SafeFileHandle(hOut, ownsHandle: true);
                var writer = new StreamWriter(new FileStream(safeOut, FileAccess.Write), System.Text.Encoding.UTF8) { AutoFlush = true };
                Console.SetOut(writer);
                Console.SetError(writer);
            }

            var hIn = NativeMethods.CreateFile("CONIN$", NativeMethods.GENERIC_READ, NativeMethods.FILE_SHARE_READ, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
            if (hIn != IntPtr.Zero && hIn != new IntPtr(-1))
            {
                NativeMethods.SetStdHandle(NativeMethods.STD_INPUT_HANDLE, hIn);
                var safeIn = new SafeFileHandle(hIn, ownsHandle: true);
                var reader = new StreamReader(new FileStream(safeIn, FileAccess.Read), System.Text.Encoding.UTF8);
                Console.SetIn(reader);
            }
        }
        catch { }
    }

    private static string FormatRssi(int? rssi) => rssi.HasValue ? $"{rssi.Value} dBm" : "N/A";
}
