using System;
using System.Collections.Generic;
using System.Linq;
using BetterBluetooth.Core.Database;

namespace BetterBluetooth.Core.Services;

/// <summary>
/// Domain store backed by LiteDB (%APPDATA%\BetterBluetooth\better_bluetooth.db).
/// Provides true ACID transactional atomicity for device rules and master settings.
/// </summary>
public sealed class AutoForgetStore : IDisposable
{
    private readonly DatabaseContext _db;
    private readonly HashSet<string> _forgetMacs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pairMacs   = new(StringComparer.OrdinalIgnoreCase);
    private bool _masterAutoForget = true;
    private bool _masterAutoPair   = true;
    private readonly object _lock = new();

    public event Action? Changed;

    public int Count
    {
        get { lock (_lock) return _forgetMacs.Count; }
    }

    public int AutoPairCount
    {
        get { lock (_lock) return _pairMacs.Count; }
    }

    public IReadOnlyCollection<string> MacAddresses
    {
        get { lock (_lock) return new List<string>(_forgetMacs); }
    }

    public IReadOnlyCollection<string> AutoPairMacAddresses
    {
        get { lock (_lock) return new List<string>(_pairMacs); }
    }

    public bool AutoFixEnabled
    {
        get => MasterAutoForget;
        set => MasterAutoForget = value;
    }

    public bool MasterAutoForget
    {
        get { lock (_lock) return _masterAutoForget; }
        set
        {
            lock (_lock)
            {
                if (_masterAutoForget == value) return;
                _masterAutoForget = value;
                var s = _db.GetSettings();
                s.MasterAutoForget = value;
                _db.SaveSettings(s);
            }
            Changed?.Invoke();
        }
    }

    public bool MasterAutoPair
    {
        get { lock (_lock) return _masterAutoPair; }
        set
        {
            lock (_lock)
            {
                if (_masterAutoPair == value) return;
                _masterAutoPair = value;
                var s = _db.GetSettings();
                s.MasterAutoPair = value;
                _db.SaveSettings(s);
            }
            Changed?.Invoke();
        }
    }

    public AutoForgetStore(string? customDbPath = null)
    {
        _db = new DatabaseContext(customDbPath);
        LoadFromDb();
    }

    private void LoadFromDb()
    {
        lock (_lock)
        {
            var s = _db.GetSettings();
            _masterAutoForget = s.MasterAutoForget;
            _masterAutoPair   = s.MasterAutoPair;

            _forgetMacs.Clear();
            _pairMacs.Clear();

            var rules = _db.GetAllRules();
            foreach (var r in rules)
            {
                string norm = NormalizeMac(r.Mac);
                if (r.AutoForget) _forgetMacs.Add(norm);
                if (r.AutoPair)   _pairMacs.Add(norm);
            }
        }
    }

    // ── Queries ─────────────────────────────────────────────────────────────

    public bool Contains(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return false;
        lock (_lock) return _forgetMacs.Contains(NormalizeMac(mac));
    }

    public bool IsAutoPair(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return false;
        lock (_lock) return _pairMacs.Contains(NormalizeMac(mac));
    }

    public List<DeviceRuleRecord> GetAllRules() => _db.GetAllRules();

    public DeviceRuleRecord? GetRule(string mac) => _db.GetRule(mac);

    // ── Mutations ───────────────────────────────────────────────────────────

    public void SetRule(string mac, string name, bool autoForget, bool autoPair)
    {
        if (string.IsNullOrWhiteSpace(mac)) return;
        string norm = NormalizeMac(mac);

        lock (_lock)
        {
            if (!autoForget && !autoPair)
            {
                _forgetMacs.Remove(norm);
                _pairMacs.Remove(norm);
                _db.DeleteRule(norm);
            }
            else
            {
                if (autoForget) _forgetMacs.Add(norm); else _forgetMacs.Remove(norm);
                if (autoPair)   _pairMacs.Add(norm);   else _pairMacs.Remove(norm);

                _db.UpsertRule(new DeviceRuleRecord
                {
                    Mac        = norm,
                    Name       = name,
                    AutoForget = autoForget,
                    AutoPair   = autoPair
                });
            }
        }
        Changed?.Invoke();
    }

    public bool Toggle(string mac, string name = "")
    {
        if (string.IsNullOrWhiteSpace(mac)) return false;
        string norm = NormalizeMac(mac);
        bool nowEnabled;

        lock (_lock)
        {
            var existing = _db.GetRule(norm);
            nowEnabled = existing == null || !existing.AutoForget;

            SetRule(norm, string.IsNullOrWhiteSpace(name) ? (existing?.Name ?? "") : name,
                autoForget: nowEnabled,
                autoPair: nowEnabled); // By default, auto-forget arms auto-pair
        }

        return nowEnabled;
    }

    public bool ToggleAutoPair(string mac, string name = "")
    {
        if (string.IsNullOrWhiteSpace(mac)) return false;
        string norm = NormalizeMac(mac);
        bool nowEnabled;

        lock (_lock)
        {
            var existing = _db.GetRule(norm);
            nowEnabled = existing == null || !existing.AutoPair;

            SetRule(norm, string.IsNullOrWhiteSpace(name) ? (existing?.Name ?? "") : name,
                autoForget: existing?.AutoForget ?? false,
                autoPair: nowEnabled);
        }

        return nowEnabled;
    }

    public void Add(string mac, string name = "") => SetRule(mac, name, autoForget: true, autoPair: true);

    public void Remove(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return;
        string norm = NormalizeMac(mac);

        lock (_lock)
        {
            _forgetMacs.Remove(norm);
            _pairMacs.Remove(norm);
            _db.DeleteRule(norm);
        }
        Changed?.Invoke();
    }

    public static string NormalizeMac(string mac) => DatabaseContext.NormalizeMac(mac);

    public void Dispose()
    {
        _db.Dispose();
    }
}
