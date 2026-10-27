using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BluetoothRectifier.Core.Services;

/// <summary>
/// Persists the list of MAC addresses marked for Auto-Forget.
/// Saved to %APPDATA%\BluetoothRectifier\autoforget.json.
/// </summary>
public sealed class AutoForgetStore
{
    private readonly string _filePath;
    private readonly HashSet<string> _macs = new(StringComparer.OrdinalIgnoreCase);
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
                    string json = File.ReadAllText(_filePath);
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
            }
            catch { }
        }
    }

    private void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(new List<string>(_macs), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch { }
    }

    public static string NormalizeMac(string mac) =>
        mac.Replace("-", ":").Trim().ToUpperInvariant();
}
