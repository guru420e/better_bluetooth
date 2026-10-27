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
using BetterBluetooth.Core.Models;
using Timer = System.Threading.Timer;

namespace BetterBluetooth.Core.Services;

/// <summary>
/// Event-driven Auto-Forget & Auto-Pair engine.
/// - Unpairs registered devices when they disconnect after a grace period.
/// - Automatically cleans up stale pairings left over from crashes on startup.
/// - Automatically pairs discoverable devices marked for Auto-Pair.
/// </summary>
public sealed class AutoForgetEngine : IDisposable
{
    public const int DefaultGracePeriodSeconds = 20;
    public const int StartupBlackoutSeconds    = 15;
    public const int ResumeBlackoutSeconds     = 10;
    public const int HeartbeatIntervalSeconds  = 30;

    // ── Events ─────────────────────────────────────────────────────────────
    public event Action<BluetoothDeviceInfo, string>? DeviceAutoForgotten;
    public event Action<BluetoothDeviceInfo>?         DeviceAutoPaired;
    public event Action<string, int>?                 GraceCountdownTick;      // (mac, secondsRemaining)
    public event Action<string>?                      GraceCountdownCancelled; // (mac)
    public event Action<string>?                      StatusMessage;

    // ── Dependencies ───────────────────────────────────────────────────────
    private readonly BluetoothService _service;
    private readonly AutoForgetStore  _store;

    // ── Device State Tracking ──────────────────────────────────────────────
    private readonly ConcurrentDictionary<string, bool> _connectedInSession = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (DateTime Expiration, BluetoothDeviceInfo Device)> _pendingForgets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _pendingAutoPairs = new(StringComparer.OrdinalIgnoreCase);
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

        _service.DeviceDiscovered     += OnDeviceDiscovered;
        _service.DeviceUpdated        += OnDeviceUpdated;
        _service.DeviceRemoved        += OnDeviceRemoved;
        _service.EnumerationCompleted += OnEnumerationCompleted;

        _ = InitRadioAsync();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _graceTimer = new Timer(OnGraceTimerTick, null, 1000, 1000);

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

    // ── Startup Crash Recovery ─────────────────────────────────────────────

    private async void OnEnumerationCompleted()
    {
        await CheckAndCleanupStalePairingsAsync();
    }

    public async Task CheckAndCleanupStalePairingsAsync()
    {
        if (!_store.AutoFixEnabled || _isPaused || !_isRadioOn)
            return;

        var devices = _service.Devices;
        foreach (var dev in devices)
        {
            string normMac = AutoForgetStore.NormalizeMac(dev.MacFormatted);
            if (normMac == "54:46:6E:55:74:01") continue; // Protected MAC safeguard

            // If a device is paired, but NOT connected, and is on the Auto-Forget list:
            // this is a stale leftover bond from a crash or interrupted session!
            if (dev.IsPaired && !dev.IsConnected && _store.Contains(normMac))
            {
                StatusMessage?.Invoke($"[Crash Recovery] Stale pairing found for {dev.Name} ({dev.MacFormatted}). Cleaning up...");
                var result = await _service.UnpairDeviceAsync(dev.Id);
                if (result == DeviceUnpairingResultStatus.Unpaired || result == DeviceUnpairingResultStatus.AlreadyUnpaired)
                {
                    DeviceAutoForgotten?.Invoke(dev, "Startup crash recovery: cleaned stale bond from interrupted session");
                }
            }
        }
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

        // A. NEARBY / UNPAIRED DEVICE -> Check for Auto-Pair
        if (!dev.IsPaired)
        {
            if (_store.AutoFixEnabled && _store.IsAutoPair(normMac))
            {
                _ = TryAutoPairDeviceAsync(dev);
            }
            return;
        }

        // B. PAIRED DEVICE -> Check Auto-Forget rules
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

    private async Task TryAutoPairDeviceAsync(BluetoothDeviceInfo dev)
    {
        string normMac = AutoForgetStore.NormalizeMac(dev.MacFormatted);
        if (normMac == "54:46:6E:55:74:01") return; // Safety guard
        if (!_pendingAutoPairs.TryAdd(normMac, true)) return;

        try
        {
            StatusMessage?.Invoke($"[Auto-Pair] Discoverable device {dev.Name} ({dev.MacFormatted}) found. Pairing automatically...");
            var result = await _service.PairDeviceAsync(dev.Id);
            if (result == DevicePairingResultStatus.Paired || result == DevicePairingResultStatus.AlreadyPaired)
            {
                StatusMessage?.Invoke($"✔ [Auto-Pair] Successfully paired with {dev.Name}!");
                DeviceAutoPaired?.Invoke(dev);
            }
            else
            {
                StatusMessage?.Invoke($"✖ [Auto-Pair] Auto-pairing {dev.Name} returned: {result}");
            }
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[Auto-Pair Error] {ex.Message}");
        }
        finally
        {
            // Delay removing from pending so we don't spam pair calls if inquiry triggers multiple updates
            _ = Task.Delay(10000).ContinueWith(t => _pendingAutoPairs.TryRemove(normMac, out _));
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
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"Failed native connection hook for {dev.Name}: {ex.Message}");
        }
    }

    // ── Grace Period Timer Tick ────────────────────────────────────────────

    private void OnGraceTimerTick(object? state)
    {
        if (_pendingForgets.IsEmpty) return;

        var now = DateTime.UtcNow;

        foreach (var kvp in _pendingForgets)
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
                if (_pendingForgets.TryRemove(mac, out _))
                {
                    _ = ExecuteAutoForgetAsync(dev);
                }
            }
        }
    }

    private async Task ExecuteAutoForgetAsync(BluetoothDeviceInfo dev)
    {
        string normMac = AutoForgetStore.NormalizeMac(dev.MacFormatted);

        if (normMac == "54:46:6E:55:74:01")
        {
            StatusMessage?.Invoke($"[Blocked] Security rule: target MAC {normMac} is protected.");
            return;
        }

        if (!AreSafeguardsPassing())
        {
            StatusMessage?.Invoke($"[Safeguard] Aborting auto-forget for {dev.Name}: safeguard conditions failed.");
            return;
        }

        StatusMessage?.Invoke($"[Auto-Forget] Executing automatic unpair for \"{dev.Name}\" ({dev.MacFormatted})...");

        try
        {
            var result = await _service.UnpairDeviceAsync(dev.Id);

            if (result == DeviceUnpairingResultStatus.Unpaired || result == DeviceUnpairingResultStatus.AlreadyUnpaired)
            {
                StatusMessage?.Invoke($"✔ [Auto-Forget] Cleanly unpaired \"{dev.Name}\" from Windows.");
                DeviceAutoForgotten?.Invoke(dev, "Disconnect grace period expired");
            }
            else
            {
                StatusMessage?.Invoke($"✖ [Auto-Forget] Failed to unpair \"{dev.Name}\": {result}");
            }
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[Auto-Forget Error] {ex.Message}");
        }
        finally
        {
            _connectedInSession.TryRemove(normMac, out _);
            if (_activeBtDevices.TryRemove(normMac, out var btDev))
            {
                try { btDev.Dispose(); } catch { }
            }
        }
    }

    public void CancelAllPendingCountdowns(string reason)
    {
        var keys = _pendingForgets.Keys.ToList();
        foreach (var k in keys)
        {
            if (_pendingForgets.TryRemove(k, out _))
            {
                GraceCountdownCancelled?.Invoke(k);
            }
        }
        if (keys.Count > 0)
        {
            StatusMessage?.Invoke($"Cancelled all pending auto-forgets: {reason}");
        }
    }

    private void OnHeartbeatTick(object? state)
    {
        if (!_pendingForgets.IsEmpty)
        {
            StatusMessage?.Invoke($"[Heartbeat] Active pending auto-forgets: {_pendingForgets.Count}");
        }
    }

    public void Dispose()
    {
        _graceTimer.Dispose();
        _heartbeatTimer.Dispose();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _service.DeviceDiscovered     -= OnDeviceDiscovered;
        _service.DeviceUpdated        -= OnDeviceUpdated;
        _service.DeviceRemoved        -= OnDeviceRemoved;
        _service.EnumerationCompleted -= OnEnumerationCompleted;

        foreach (var kvp in _activeBtDevices)
        {
            try { kvp.Value.Dispose(); } catch { }
        }
        _activeBtDevices.Clear();
    }
}
