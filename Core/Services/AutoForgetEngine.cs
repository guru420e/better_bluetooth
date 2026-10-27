using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using BluetoothRectifier.Core.Models;
using Timer = System.Threading.Timer;

namespace BluetoothRectifier.Core.Services;

/// <summary>
/// Event-driven Auto-Forget engine.
/// Monitors devices registered in AutoForgetStore and unpairs them when they disconnect,
/// provided all safety conditions (session connection, grace period, radio on, sleep guard) are met.
/// </summary>
public sealed class AutoForgetEngine : IDisposable
{
    public const int DefaultGracePeriodSeconds = 20;
    public const int StartupBlackoutSeconds    = 15;
    public const int ResumeBlackoutSeconds     = 10;
    public const int HeartbeatIntervalSeconds  = 30;

    // ── Events ─────────────────────────────────────────────────────────────
    public event Action<BluetoothDeviceInfo, string>? DeviceAutoForgotten;
    public event Action<string, int>?                 GraceCountdownTick;      // (mac, secondsRemaining)
    public event Action<string>?                      GraceCountdownCancelled; // (mac)
    public event Action<string>?                      StatusMessage;

    // ── Dependencies ───────────────────────────────────────────────────────
    private readonly BluetoothService _service;
    private readonly AutoForgetStore  _store;

    // ── Device State Tracking ──────────────────────────────────────────────
    private readonly ConcurrentDictionary<string, bool> _connectedInSession = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (DateTime Expiration, BluetoothDeviceInfo Device)> _pendingForgets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BluetoothDevice> _activeBtDevices = new(StringComparer.OrdinalIgnoreCase);

    // ── Timers & Guards ────────────────────────────────────────────────────
    private readonly Timer _graceTimer;
    private readonly Timer _heartbeatTimer;
    private Radio?         _btRadio;
    private DateTime       _engineStartedTime;
    private DateTime       _resumeBlackoutUntil = DateTime.MinValue;
    private volatile bool  _isRadioOn = true;
    private volatile bool  _isPaused  = false;

    public int GracePeriodSeconds { get; set; } = DefaultGracePeriodSeconds;
    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            _isPaused = value;
            StatusMessage?.Invoke(value ? "Auto-forget engine paused." : "Auto-forget engine resumed.");
        }
    }

    public AutoForgetEngine(BluetoothService service, AutoForgetStore store)
    {
        _service = service;
        _store   = store;

        _engineStartedTime = DateTime.UtcNow;

        _service.DeviceDiscovered += OnDeviceDiscovered;
        _service.DeviceUpdated    += OnDeviceUpdated;
        _service.DeviceRemoved    += OnDeviceRemoved;

        _ = InitRadioAsync();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _graceTimer     = new Timer(OnGraceTimerTick, null, 1000, 1000);
        _store.Changed += () =>
        {
            if (!_store.AutoFixEnabled)
            {
                CancelAllPendingCountdowns("Auto-fix disabled by user");
            }
        };

        _heartbeatTimer = new Timer(OnHeartbeatTick, null, HeartbeatIntervalSeconds * 1000, HeartbeatIntervalSeconds * 1000);
    }

    // ── Radio & Power Safeguards ───────────────────────────────────────────

    private async Task InitRadioAsync()
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            _btRadio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            if (_btRadio != null)
            {
                _isRadioOn = _btRadio.State == RadioState.On;
                _btRadio.StateChanged += (sender, _) =>
                {
                    bool wasOn = _isRadioOn;
                    _isRadioOn = sender.State == RadioState.On;
                    StatusMessage?.Invoke($"Bluetooth Radio State: {sender.State}");

                    if (!_isRadioOn && wasOn)
                    {
                        CancelAllPendingCountdowns("Bluetooth radio was turned off");
                    }
                };
            }
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"Radio monitoring init: {ex.Message}");
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            StatusMessage?.Invoke("System suspending to sleep. Cancelling all pending countdowns.");
            CancelAllPendingCountdowns("System entered sleep");
        }
        else if (e.Mode == PowerModes.Resume)
        {
            _resumeBlackoutUntil = DateTime.UtcNow.AddSeconds(ResumeBlackoutSeconds);
            StatusMessage?.Invoke($"System resumed from sleep. Safeguard blackout active for {ResumeBlackoutSeconds}s.");
        }
    }

    private bool AreSafeguardsPassing()
    {
        if (_isPaused) return false;
        if (!_isRadioOn) return false;
        if (!_store.AutoFixEnabled) return false;

        if (DateTime.UtcNow < _engineStartedTime.AddSeconds(StartupBlackoutSeconds))
            return false;

        if (DateTime.UtcNow < _resumeBlackoutUntil)
            return false;

        return true;
    }

    // ── Device Event Handling ──────────────────────────────────────────────

    private void OnDeviceDiscovered(BluetoothDeviceInfo dev) => ProcessDeviceState(dev);
    private void OnDeviceUpdated(BluetoothDeviceInfo dev)    => ProcessDeviceState(dev);

    private void OnDeviceRemoved(string deviceId)
    {
        var norm = AutoForgetStore.NormalizeMac(deviceId);
        _pendingForgets.TryRemove(norm, out _);
    }

    private void ProcessDeviceState(BluetoothDeviceInfo dev)
    {
        string normMac = AutoForgetStore.NormalizeMac(dev.MacFormatted);

        if (!_store.Contains(normMac))
        {
            if (_pendingForgets.TryRemove(normMac, out _))
            {
                GraceCountdownCancelled?.Invoke(normMac);
            }
            return;
        }

        // 1. Device is CONNECTED
        if (dev.IsConnected)
        {
            _connectedInSession[normMac] = true;

            if (_pendingForgets.TryRemove(normMac, out _))
            {
                StatusMessage?.Invoke($"Device {dev.Name} reconnected within grace period. Countdown cancelled.");
                GraceCountdownCancelled?.Invoke(normMac);
            }

            _ = HookNativeConnectionStatusAsync(dev);
            return;
        }

        // 2. Device is DISCONNECTED
        if (!dev.IsConnected)
        {
            if (!_connectedInSession.TryGetValue(normMac, out bool sawConnected) || !sawConnected)
            {
                return;
            }

            if (!AreSafeguardsPassing())
            {
                return;
            }

            if (!_pendingForgets.ContainsKey(normMac))
            {
                var expiration = DateTime.UtcNow.AddSeconds(GracePeriodSeconds);
                _pendingForgets[normMac] = (expiration, dev);
                StatusMessage?.Invoke($"Device {dev.Name} disconnected. Starting {GracePeriodSeconds}s auto-forget grace period...");
                GraceCountdownTick?.Invoke(normMac, GracePeriodSeconds);
            }
        }
    }

    private async Task HookNativeConnectionStatusAsync(BluetoothDeviceInfo dev)
    {
        string normMac = AutoForgetStore.NormalizeMac(dev.MacFormatted);
        if (_activeBtDevices.ContainsKey(normMac)) return;

        try
        {
            var btDev = await BluetoothDevice.FromIdAsync(dev.Id);
            if (btDev != null)
            {
                _activeBtDevices[normMac] = btDev;
                btDev.ConnectionStatusChanged += (sender, _) =>
                {
                    bool isConnected = sender.ConnectionStatus == BluetoothConnectionStatus.Connected;
                    StatusMessage?.Invoke($"[NativeEvent] {sender.Name} ConnectionStatus -> {sender.ConnectionStatus}");

                    var updated = dev with { IsConnected = isConnected };
                    ProcessDeviceState(updated);
                };
            }
        }
        catch { }
    }

    // ── 1-Second Grace Timer Tick ──────────────────────────────────────────

    private void OnGraceTimerTick(object? state)
    {
        if (!AreSafeguardsPassing())
        {
            if (!_isRadioOn)
            {
                CancelAllPendingCountdowns("Bluetooth radio turned off");
            }
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var kvp in _pendingForgets.ToList())
        {
            string mac = kvp.Key;
            var (expiration, dev) = kvp.Value;

            int remaining = (int)Math.Ceiling((expiration - now).TotalSeconds);

            if (remaining > 0)
            {
                GraceCountdownTick?.Invoke(mac, remaining);
            }
            else
            {
                _pendingForgets.TryRemove(mac, out _);
                _ = ExecuteAutoForgetAsync(dev);
            }
        }
    }

    private async Task ExecuteAutoForgetAsync(BluetoothDeviceInfo dev)
    {
        string mac = dev.MacFormatted;
        StatusMessage?.Invoke($"[Auto-Forget] Grace period expired for {dev.Name} ({mac}). Unpairing...");

        var result = await _service.UnpairDeviceAsync(dev.Id);
        string reason = $"Auto-forget executed after disconnect > {GracePeriodSeconds}s (Result: {result})";

        _connectedInSession.TryRemove(AutoForgetStore.NormalizeMac(mac), out _);

        LogAuditToCsv(dev, reason);

        DeviceAutoForgotten?.Invoke(dev, reason);
        StatusMessage?.Invoke($"[Auto-Forget] {dev.Name} ({mac}) successfully unpaired.");
    }

    // ── 30-Second Backup Heartbeat ─────────────────────────────────────────

    private void OnHeartbeatTick(object? state)
    {
        if (_store.Count == 0 || !AreSafeguardsPassing())
            return;

        foreach (string mac in _store.MacAddresses)
        {
            var dev = _service.FindByMac(mac);
            if (dev != null)
            {
                ProcessDeviceState(dev);
            }
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void CancelAllPendingCountdowns(string reason)
    {
        foreach (var mac in _pendingForgets.Keys.ToList())
        {
            if (_pendingForgets.TryRemove(mac, out _))
            {
                GraceCountdownCancelled?.Invoke(mac);
                StatusMessage?.Invoke($"Cancelled countdown for {mac}: {reason}");
            }
        }
    }

    private void LogAuditToCsv(BluetoothDeviceInfo dev, string reason)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss},AUTO-FORGET," +
                       $"\"{dev.Name.Replace("\"", "\"\"")}\"," +
                       $"{dev.MacFormatted},{dev.IsPaired},{dev.IsConnected}," +
                       $"{(dev.Rssi.HasValue ? dev.Rssi.Value.ToString() : "")}," +
                       $"\"{reason.Replace("\"", "\"\"")}\"\r\n";

            _service.LogRawLine(line);
        }
        catch { }
    }

    public void Dispose()
    {
        _graceTimer.Dispose();
        _heartbeatTimer.Dispose();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        foreach (var dev in _activeBtDevices.Values)
        {
            try { dev.Dispose(); } catch { }
        }
        _activeBtDevices.Clear();
    }
}
