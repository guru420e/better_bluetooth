using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BluetoothRectifier.Core.Services;

/// <summary>
/// Persists the list of MAC addresses marked for Auto-Forget and the global
/// Auto-Fix master toggle. Saved to %APPDATA%\BluetoothRectifier\autoforget.json.
/// </summary>
public sealed class AutoForgetStore
{
    private readonly string _filePath;
    private readonly HashSet<string> _macs = new(StringComparer.OrdinalIgnoreCase);
    private bool _autoFixEnabled = true;
    private readonly object _lock = new();

    public event Action? Changed;

    public int Count
    {
        get { lock (_lock) return _macs.Count; }
    }

    public IReadOnlyCollection<string> MacAddresses
    {
        get { lock (_lock) return new List<string>(_macs); }
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
                nowEnabled = false;
            }
            else
            {
                _macs.Add(norm);
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
        lock (_lock)
        {
            if (_macs.Add(NormalizeMac(mac)))
                Save();
        }
        Changed?.Invoke();
    }

    public void Remove(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return;
        lock (_lock)
        {
            if (_macs.Remove(NormalizeMac(mac)))
                Save();
        }
        Changed?.Invoke();
    }

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
                            foreach (var m in list)
                            {
                                if (!string.IsNullOrWhiteSpace(m))
                                    _macs.Add(NormalizeMac(m));
                            }
                        }
                    }
                    else if (json.StartsWith("{"))
                    {
                        // Structured store with settings and mac list
                        var doc = JsonSerializer.Deserialize<StoreData>(json);
                        if (doc != null)
                        {
                            _autoFixEnabled = doc.AutoFixEnabled;
                            _macs.Clear();
                            if (doc.MacAddresses != null)
                            {
                                foreach (var m in doc.MacAddresses)
                                {
                                    if (!string.IsNullOrWhiteSpace(m))
                                        _macs.Add(NormalizeMac(m));
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
                AutoFixEnabled = _autoFixEnabled,
                MacAddresses   = new List<string>(_macs),
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
    }
}
