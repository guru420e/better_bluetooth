using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Windows.Devices.Enumeration;

namespace BluetoothRectifier;

/// <summary>
/// Bluetooth Classic discovery and device management service.
/// Wraps WinRT DeviceWatcher with AEP (AssociationEndpoint) filter.
/// Completely decoupled from UI.
/// </summary>
public sealed class BluetoothService : IDisposable
{
    // Bluetooth Classic Protocol GUID
    public const string ClassicProtocolId = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
    public const string ClassicAqsFilter = $"System.Devices.Aep.ProtocolId:=\"{ClassicProtocolId}\"";

    public static readonly string[] RequestedProperties = new[]
    {
        "System.Devices.Aep.DeviceAddress",
        "System.Devices.Aep.IsPaired",
        "System.Devices.Aep.IsConnected",
        "System.Devices.Aep.SignalStrength",
        "System.Devices.Aep.Bluetooth.Cod.Major",
        "System.ItemNameDisplay",
        "System.Devices.Aep.IsPresent"
    };

    // ── Events ─────────────────────────────────────────────────────────────
    public event Action<BluetoothDeviceInfo>? DeviceDiscovered;
    public event Action<BluetoothDeviceInfo>? DeviceUpdated;
    public event Action<string>?              DeviceRemoved;
    public event Action?                      EnumerationCompleted;
    public event Action<string>?              StatusMessage;

    // ── State ──────────────────────────────────────────────────────────────
    private DeviceWatcher? _watcher;
    private readonly ConcurrentDictionary<string, BluetoothDeviceInfo> _devices = new();
    private volatile bool _isEnumerationCompleted;
    private readonly object _csvLock = new();
    private readonly List<string> _csvPaths = new();

    public IReadOnlyList<string> CsvPaths => _csvPaths;
    public string CsvPath => _csvPaths.Count > 0 ? _csvPaths[0] : "";

    public bool IsScanning => _watcher != null && 
        (_watcher.Status == DeviceWatcherStatus.Started || _watcher.Status == DeviceWatcherStatus.EnumerationCompleted);

    public bool HasEnumerationCompleted => _isEnumerationCompleted;

    public IReadOnlyCollection<BluetoothDeviceInfo> Devices => _devices.Values.ToList();

    public BluetoothService(string? customCsvPath = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "BluetoothRectifier");
        Directory.CreateDirectory(dir);
        _csvPaths.Add(Path.Combine(dir, "discovery.csv"));

        // Also log to current directory discovery.csv for easy terminal inspection
        string local = Path.Combine(Directory.GetCurrentDirectory(), "discovery.csv");
        if (!_csvPaths.Contains(local, StringComparer.OrdinalIgnoreCase))
            _csvPaths.Add(local);

        if (!string.IsNullOrWhiteSpace(customCsvPath) && !_csvPaths.Contains(customCsvPath, StringComparer.OrdinalIgnoreCase))
        {
            _csvPaths.Insert(0, customCsvPath);
        }

        EnsureCsvHeader();
    }

    // ── Scanner lifecycle ──────────────────────────────────────────────────

    public void StartScan()
    {
        if (IsScanning)
            return;

        _devices.Clear();
        _isEnumerationCompleted = false;

        _watcher = DeviceInformation.CreateWatcher(
            ClassicAqsFilter,
            RequestedProperties,
            DeviceInformationKind.AssociationEndpoint);

        _watcher.Added                += OnWatcherAdded;
        _watcher.Updated              += OnWatcherUpdated;
        _watcher.Removed              += OnWatcherRemoved;
        _watcher.EnumerationCompleted += OnWatcherEnumerationCompleted;
        _watcher.Stopped              += OnWatcherStopped;

        StatusMessage?.Invoke("Starting Bluetooth Classic discovery watcher...");
        _watcher.Start();
    }

    public void RestartScan()
    {
        if (_watcher != null)
        {
            try
            {
                _watcher.Added                -= OnWatcherAdded;
                _watcher.Updated              -= OnWatcherUpdated;
                _watcher.Removed              -= OnWatcherRemoved;
                _watcher.EnumerationCompleted -= OnWatcherEnumerationCompleted;
                _watcher.Stopped              -= OnWatcherStopped;

                if (_watcher.Status == DeviceWatcherStatus.Started ||
                    _watcher.Status == DeviceWatcherStatus.EnumerationCompleted)
                {
                    _watcher.Stop();
                }
            }
            catch { }
            _watcher = null;
        }

        StartScan();
    }

    public void StopScan()
    {
        if (_watcher == null)
            return;

        try
        {
            if (_watcher.Status == DeviceWatcherStatus.Started ||
                _watcher.Status == DeviceWatcherStatus.EnumerationCompleted)
            {
                StatusMessage?.Invoke("Stopping discovery watcher...");
                _watcher.Stop();
            }
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"Error stopping watcher: {ex.Message}");
        }
    }

    // ── Pairing & Unpairing Operations ─────────────────────────────────────

    public void ForgetDevice(string deviceId)
    {
        _devices.TryRemove(deviceId, out _);
    }

    public async Task<DeviceUnpairingResultStatus> UnpairDeviceAsync(string deviceId)
    {
        try
        {
            StatusMessage?.Invoke($"Unpairing device {deviceId}...");
            var dev = await DeviceInformation.CreateFromIdAsync(
                deviceId, 
                RequestedProperties, 
                DeviceInformationKind.AssociationEndpoint);

            if (dev == null)
            {
                StatusMessage?.Invoke("Device not found for unpairing.");
                return DeviceUnpairingResultStatus.Failed;
            }

            var res = await dev.Pairing.UnpairAsync();
            StatusMessage?.Invoke($"Unpair result: {res.Status}");

            if (res.Status == DeviceUnpairingResultStatus.Unpaired ||
                res.Status == DeviceUnpairingResultStatus.AlreadyUnpaired)
            {
                _devices.TryRemove(deviceId, out _);
                DeviceRemoved?.Invoke(deviceId);
            }

            return res.Status;
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"Unpair error: {ex.Message}");
            return DeviceUnpairingResultStatus.Failed;
        }
    }

    public async Task<DevicePairingResultStatus> PairDeviceAsync(string deviceId)
    {
        try
        {
            StatusMessage?.Invoke($"Fetching device {deviceId} for pairing...");
            var dev = await DeviceInformation.CreateFromIdAsync(
                deviceId, 
                RequestedProperties, 
                DeviceInformationKind.AssociationEndpoint);

            if (dev == null)
            {
                StatusMessage?.Invoke("Device not found for pairing.");
                return DevicePairingResultStatus.Failed;
            }

            StatusMessage?.Invoke($"Device ready: CanPair={dev.Pairing.CanPair}, IsPaired={dev.Pairing.IsPaired}");

            // If already paired, nothing to do
            if (dev.Pairing.IsPaired)
                return DevicePairingResultStatus.AlreadyPaired;

            // Handle pairing ceremonies (Just Works, Numeric Comparison, PIN 0000/1234)
            var custom = dev.Pairing.Custom;
            void OnPairingRequested(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
            {
                StatusMessage?.Invoke($"PairingRequested: Kind={args.PairingKind}, Pin={args.Pin}");
                switch (args.PairingKind)
                {
                    case DevicePairingKinds.ConfirmOnly:
                        args.Accept();
                        break;
                    case DevicePairingKinds.ProvidePin:
                        args.Accept("0000"); // default PIN for Classic audio
                        break;
                    case DevicePairingKinds.DisplayPin:
                        args.Accept();
                        break;
                    default:
                        args.Accept();
                        break;
                }
            }

            custom.PairingRequested += OnPairingRequested;
            try
            {
                var supportedKinds = DevicePairingKinds.ConfirmOnly |
                                     DevicePairingKinds.DisplayPin |
                                     DevicePairingKinds.ProvidePin;

                StatusMessage?.Invoke("Executing custom pairing...");
                var res = await custom.PairAsync(supportedKinds, DevicePairingProtectionLevel.None);
                StatusMessage?.Invoke($"Pair result: {res.Status}");

                if (res.Status != DevicePairingResultStatus.Paired &&
                    res.Status != DevicePairingResultStatus.AlreadyPaired)
                {
                    // Fallback to basic PairAsync
                    StatusMessage?.Invoke("Custom pairing returned non-success, attempting standard PairAsync...");
                    res = await dev.Pairing.PairAsync(DevicePairingProtectionLevel.None);
                    StatusMessage?.Invoke($"Standard PairAsync result: {res.Status}");
                }

                return res.Status;
            }
            finally
            {
                custom.PairingRequested -= OnPairingRequested;
            }
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"Pair error: {ex.Message}");
            return DevicePairingResultStatus.Failed;
        }
    }

    public BluetoothDeviceInfo? FindByMac(string mac)
    {
        string norm = AutoForgetStore.NormalizeMac(mac);
        return _devices.Values.FirstOrDefault(d => string.Equals(d.MacFormatted, norm, StringComparison.OrdinalIgnoreCase));
    }

    // ── Watcher Event Handlers ─────────────────────────────────────────────

    private void OnWatcherAdded(DeviceWatcher sender, DeviceInformation info)
    {
        string phase = _isEnumerationCompleted ? "live" : "known";
        var dev = ParseDeviceInfo(info, phase);

        _devices[dev.Id] = dev;
        LogToCsv(dev);
        DeviceDiscovered?.Invoke(dev);
    }

    private void OnWatcherUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        if (!_devices.TryGetValue(update.Id, out var existing))
            return;

        var updated = MergeUpdate(existing, update);
        _devices[update.Id] = updated;

        // Log if RSSI, connection or pairing status changed
        if (existing.IsConnected != updated.IsConnected ||
            existing.IsPaired != updated.IsPaired ||
            existing.Rssi != updated.Rssi)
        {
            LogToCsv(updated);
        }

        DeviceUpdated?.Invoke(updated);
    }

    private void OnWatcherRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        _devices.TryRemove(update.Id, out _);
        DeviceRemoved?.Invoke(update.Id);
    }

    private void OnWatcherEnumerationCompleted(DeviceWatcher sender, object args)
    {
        _isEnumerationCompleted = true;
        StatusMessage?.Invoke("Initial enumeration complete. Live discovery active...");
        EnumerationCompleted?.Invoke();
    }

    private void OnWatcherStopped(DeviceWatcher sender, object args)
    {
        StatusMessage?.Invoke("Watcher stopped.");
    }

    // ── Parsing & Property Extraction ──────────────────────────────────────

    private static BluetoothDeviceInfo ParseDeviceInfo(DeviceInformation info, string phase)
    {
        string mac = ExtractProperty<string>(info.Properties, "System.Devices.Aep.DeviceAddress") ?? "";
        bool isPaired = ExtractProperty<bool>(info.Properties, "System.Devices.Aep.IsPaired");
        bool isConnected = ExtractProperty<bool>(info.Properties, "System.Devices.Aep.IsConnected");
        int? rssi = ExtractNullableInt(info.Properties, "System.Devices.Aep.SignalStrength");
        int codMajor = ExtractInt(info.Properties, "System.Devices.Aep.Bluetooth.Cod.Major");

        string name = info.Name;
        if (string.IsNullOrWhiteSpace(name))
            name = ExtractProperty<string>(info.Properties, "System.ItemNameDisplay") ?? "Unknown Device";

        return new BluetoothDeviceInfo
        {
            Id = info.Id,
            Name = name,
            Mac = mac,
            IsPaired = isPaired,
            IsConnected = isConnected,
            Rssi = rssi,
            CodMajor = codMajor,
            Phase = phase
        };
    }

    private static BluetoothDeviceInfo MergeUpdate(BluetoothDeviceInfo current, DeviceInformationUpdate update)
    {
        bool isPaired = current.IsPaired;
        if (update.Properties.TryGetValue("System.Devices.Aep.IsPaired", out var pPaired) && pPaired != null)
            isPaired = ConvertToBool(pPaired);

        bool isConnected = current.IsConnected;
        if (update.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var pConn) && pConn != null)
            isConnected = ConvertToBool(pConn);

        int? rssi = current.Rssi;
        if (update.Properties.TryGetValue("System.Devices.Aep.SignalStrength", out var pRssi) && pRssi != null)
            rssi = ConvertToInt(pRssi);

        string name = current.Name;
        if (update.Properties.TryGetValue("System.ItemNameDisplay", out var pName) && pName is string s && !string.IsNullOrWhiteSpace(s))
            name = s;

        int cod = current.CodMajor;
        if (update.Properties.TryGetValue("System.Devices.Aep.Bluetooth.Cod.Major", out var pCod) && pCod != null)
            cod = ConvertToInt(pCod);

        return current with
        {
            IsPaired = isPaired,
            IsConnected = isConnected,
            Rssi = rssi,
            Name = name,
            CodMajor = cod
        };
    }

    private static T? ExtractProperty<T>(IReadOnlyDictionary<string, object> props, string key)
    {
        if (props.TryGetValue(key, out var val) && val != null)
        {
            if (typeof(T) == typeof(bool))
                return (T)(object)ConvertToBool(val);

            if (val is T typed)
                return typed;

            try { return (T)Convert.ChangeType(val, typeof(T)); }
            catch { }
        }
        return default;
    }

    private static int? ExtractNullableInt(IReadOnlyDictionary<string, object> props, string key)
    {
        if (props.TryGetValue(key, out var val) && val != null)
            return ConvertToInt(val);
        return null;
    }

    private static int ExtractInt(IReadOnlyDictionary<string, object> props, string key)
    {
        if (props.TryGetValue(key, out var val) && val != null)
            return ConvertToInt(val);
        return 0;
    }

    private static bool ConvertToBool(object val)
    {
        if (val is bool b) return b;
        try { return Convert.ToBoolean(val); } catch { return false; }
    }

    private static int ConvertToInt(object val)
    {
        if (val is int i) return i;
        if (val is short s) return s;
        if (val is uint u) return (int)u;
        if (val is byte by) return by;
        try { return Convert.ToInt32(val); } catch { return 0; }
    }

    // ── CSV Logging ────────────────────────────────────────────────────────

    private void EnsureCsvHeader()
    {
        try
        {
            lock (_csvLock)
            {
                foreach (var path in _csvPaths)
                {
                    if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    {
                        File.WriteAllText(path, "Timestamp,Phase,Name,MAC,IsPaired,IsConnected,RSSI,Type\r\n", Encoding.UTF8);
                    }
                }
            }
        }
        catch { }
    }

    private void LogToCsv(BluetoothDeviceInfo dev)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}," +
                       $"{EscapeCsv(dev.Phase)}," +
                       $"{EscapeCsv(dev.Name)}," +
                       $"{dev.MacFormatted}," +
                       $"{dev.IsPaired}," +
                       $"{dev.IsConnected}," +
                       $"{(dev.Rssi.HasValue ? dev.Rssi.Value.ToString() : "")}," +
                       $"{EscapeCsv(dev.CodLabel)}\r\n";

            lock (_csvLock)
            {
                foreach (var path in _csvPaths)
                {
                    try { File.AppendAllText(path, line, Encoding.UTF8); } catch { }
                }
            }
        }
        catch { }
    }

    public void LogRawLine(string line)
    {
        try
        {
            lock (_csvLock)
            {
                foreach (var path in _csvPaths)
                {
                    try { File.AppendAllText(path, line, Encoding.UTF8); } catch { }
                }
            }
        }
        catch { }
    }

    private static string EscapeCsv(string val)
    {
        if (string.IsNullOrEmpty(val)) return "";
        if (val.Contains(',') || val.Contains('"') || val.Contains('\n') || val.Contains('\r'))
        {
            return $"\"{val.Replace("\"", "\"\"")}\"";
        }
        return val;
    }

    // ── Cleanup ────────────────────────────────────────────────────────────

    public void Dispose()
    {
        StopScan();
        _watcher = null;
    }
}
