using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BluetoothRectifier.Core.Services;

/// <summary>
/// Persists the list of MAC addresses marked for Auto-Forget, the list of MACs
/// marked for Auto-Pair, and the global Auto-Fix master toggle.
/// Saved to %APPDATA%\BluetoothRectifier\autoforget.json.
/// </summary>
public sealed class AutoForgetStore
{
    private readonly string _filePath;
    private readonly HashSet<string> _macs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _autoPairMacs = new(StringComparer.OrdinalIgnoreCase);
    private bool _autoFixEnabled = true;
    private readonly object _lock = new();

    public event Action? Changed;

    public int Count
    {
        get { lock (_lock) return _macs.Count; }
    }

    public int AutoPairCount
    {
        get { lock (_lock) return _autoPairMacs.Count; }
    }

    public IReadOnlyCollection<string> MacAddresses
    {
        get { lock (_lock) return new List<string>(_macs); }
    }

    public IReadOnlyCollection<string> AutoPairMacAddresses
    {
        get { lock (_lock) return new List<string>(_autoPairMacs); }
    }

    public bool AutoFixEnabled
    {
        get { lock (_lock) return _autoFixEnabled; }
        set
        {
            lock (_lock)
            {
                if (_autoFixEnabled == value) return;
                _autoFixEnabled = value;
                Save();
            }
            Changed?.Invoke();
        }
    }

    public AutoForgetStore(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _filePath = customPath;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "BluetoothRectifier");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "autoforget.json");
        }

        Load();
    }

    // ── Auto-Forget Management ──────────────────────────────────────────────

    public bool Contains(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return false;
        lock (_lock) return _macs.Contains(NormalizeMac(mac));
    }

    public bool Toggle(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return false;
        string norm = NormalizeMac(mac);
        bool nowEnabled;
        lock (_lock)
        {
            if (_macs.Contains(norm))
            {
                _macs.Remove(norm);
                _autoPairMacs.Remove(norm);
                nowEnabled = false;
            }
            else
            {
                _macs.Add(norm);
                _autoPairMacs.Add(norm); // By default, auto-forget also enables auto-pair
                nowEnabled = true;
            }
            Save();
        }
        Changed?.Invoke();
        return nowEnabled;
    }

    public void Add(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return;
        string norm = NormalizeMac(mac);
        lock (_lock)
        {
            bool added = _macs.Add(norm);
            _autoPairMacs.Add(norm);
            if (added) Save();
        }
        Changed?.Invoke();
    }

    public void Remove(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return;
        string norm = NormalizeMac(mac);
        lock (_lock)
        {
            bool removed = _macs.Remove(norm);
            _autoPairMacs.Remove(norm);
            if (removed) Save();
        }
        Changed?.Invoke();
    }

    // ── Auto-Pair Management ────────────────────────────────────────────────

    public bool IsAutoPair(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return false;
        lock (_lock) return _autoPairMacs.Contains(NormalizeMac(mac));
    }

    public bool ToggleAutoPair(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return false;
        string norm = NormalizeMac(mac);
        bool nowEnabled;
        lock (_lock)
        {
            if (_autoPairMacs.Contains(norm))
            {
                _autoPairMacs.Remove(norm);
                nowEnabled = false;
            }
            else
            {
                _autoPairMacs.Add(norm);
                nowEnabled = true;
            }
            Save();
        }
        Changed?.Invoke();
        return nowEnabled;
    }

    public void AddAutoPair(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return;
        lock (_lock)
        {
            if (_autoPairMacs.Add(NormalizeMac(mac)))
                Save();
        }
        Changed?.Invoke();
    }

    public void RemoveAutoPair(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return;
        lock (_lock)
        {
            if (_autoPairMacs.Remove(NormalizeMac(mac)))
                Save();
        }
        Changed?.Invoke();
    }

    // ── Persistence ─────────────────────────────────────────────────────────

    private void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath).Trim();
                    if (json.StartsWith("["))
                    {
                        // Backward compatibility: raw list of strings
                        var list = JsonSerializer.Deserialize<List<string>>(json);
                        if (list != null)
                        {
                            _macs.Clear();
                            _autoPairMacs.Clear();
                            foreach (var m in list)
                            {
                                if (!string.IsNullOrWhiteSpace(m))
                                {
                                    string norm = NormalizeMac(m);
                                    _macs.Add(norm);
                                    _autoPairMacs.Add(norm);
                                }
                            }
                        }
                    }
                    else if (json.StartsWith("{"))
                    {
                        // Structured store with settings, auto-forget, and auto-pair lists
                        var doc = JsonSerializer.Deserialize<StoreData>(json);
                        if (doc != null)
                        {
                            _autoFixEnabled = doc.AutoFixEnabled;
                            _macs.Clear();
                            _autoPairMacs.Clear();

                            if (doc.MacAddresses != null)
                            {
                                foreach (var m in doc.MacAddresses)
                                {
                                    if (!string.IsNullOrWhiteSpace(m))
                                        _macs.Add(NormalizeMac(m));
                                }
                            }

                            if (doc.AutoPairMacAddresses != null)
                            {
                                foreach (var m in doc.AutoPairMacAddresses)
                                {
                                    if (!string.IsNullOrWhiteSpace(m))
                                        _autoPairMacs.Add(NormalizeMac(m));
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }

    private void Save()
    {
        try
        {
            var data = new StoreData
            {
                AutoFixEnabled       = _autoFixEnabled,
                MacAddresses         = new List<string>(_macs),
                AutoPairMacAddresses = new List<string>(_autoPairMacs),
            };

            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch { }
    }

    public static string NormalizeMac(string mac) =>
        mac.Replace("-", ":").Trim().ToUpperInvariant();

    private sealed class StoreData
    {
        public bool AutoFixEnabled { get; set; } = true;
        public List<string> MacAddresses { get; set; } = new();
        public List<string> AutoPairMacAddresses { get; set; } = new();
    }
}
