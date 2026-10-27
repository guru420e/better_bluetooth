using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LiteDB;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BetterBluetooth.Core.Database;

/// <summary>
/// Encapsulates the LiteDB database with ACID transaction support,
/// thread-safe access, and automatic legacy migration.
/// Database location: %APPDATA%\BetterBluetooth\better_bluetooth.db
/// </summary>
public sealed class DatabaseContext : IDisposable
{
    private const string RulesCollectionName = "device_rules";
    private const string SettingsCollectionName = "settings";

    private readonly LiteDatabase _db;
    private readonly object _lock = new();

    public string DbPath { get; }

    public DatabaseContext(string? customDbPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customDbPath))
        {
            DbPath = customDbPath;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "BetterBluetooth");
            Directory.CreateDirectory(dir);
            DbPath = Path.Combine(dir, "better_bluetooth.db");
        }

        // Connection string with Shared mode for thread-safety and robust concurrency
        var connStr = new ConnectionString
        {
            Filename   = DbPath,
            Connection = ConnectionType.Shared
        };

        _db = new LiteDatabase(connStr);
        EnsureIndices();
        MigrateLegacyJsonIfPresent();
    }

    private void EnsureIndices()
    {
        lock (_lock)
        {
            var col = _db.GetCollection<DeviceRuleRecord>(RulesCollectionName);
            col.EnsureIndex(x => x.Mac, unique: true);
        }
    }

    // ── Rules Management ────────────────────────────────────────────────────

    public List<DeviceRuleRecord> GetAllRules()
    {
        lock (_lock)
        {
            var col = _db.GetCollection<DeviceRuleRecord>(RulesCollectionName);
            return col.FindAll().OrderByDescending(x => x.UpdatedAt).ToList();
        }
    }

    public DeviceRuleRecord? GetRule(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return null;
        string norm = NormalizeMac(mac);

        lock (_lock)
        {
            var col = _db.GetCollection<DeviceRuleRecord>(RulesCollectionName);
            return col.FindById(norm);
        }
    }

    public void UpsertRule(DeviceRuleRecord rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Mac)) return;
        rule.Mac = NormalizeMac(rule.Mac);
        rule.UpdatedAt = DateTime.UtcNow;

        lock (_lock)
        {
            var col = _db.GetCollection<DeviceRuleRecord>(RulesCollectionName);
            col.Upsert(rule);
        }
    }

    public bool DeleteRule(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return false;
        string norm = NormalizeMac(mac);

        lock (_lock)
        {
            var col = _db.GetCollection<DeviceRuleRecord>(RulesCollectionName);
            return col.Delete(norm);
        }
    }

    // ── Settings Management ─────────────────────────────────────────────────

    public AppSettingsRecord GetSettings()
    {
        lock (_lock)
        {
            var col = _db.GetCollection<AppSettingsRecord>(SettingsCollectionName);
            var s = col.FindById("global");
            if (s == null)
            {
                s = new AppSettingsRecord();
                col.Insert(s);
            }
            return s;
        }
    }

    public void SaveSettings(AppSettingsRecord settings)
    {
        settings.Id = "global";
        settings.UpdatedAt = DateTime.UtcNow;

        lock (_lock)
        {
            var col = _db.GetCollection<AppSettingsRecord>(SettingsCollectionName);
            col.Upsert(settings);
        }
    }

    // ── Legacy JSON Migration ───────────────────────────────────────────────

    private void MigrateLegacyJsonIfPresent()
    {
        try
        {
            lock (_lock)
            {
                var col = _db.GetCollection<DeviceRuleRecord>(RulesCollectionName);
                if (col.Count() > 0) return; // Already populated

                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string newJson = Path.Combine(appData, "BetterBluetooth", "autoforget.json");
                string oldJson = Path.Combine(appData, "BluetoothRectifier", "autoforget.json");
                string targetJson = File.Exists(newJson) ? newJson : (File.Exists(oldJson) ? oldJson : "");

                if (string.IsNullOrEmpty(targetJson)) return;

                string raw = File.ReadAllText(targetJson).Trim();
                if (raw.StartsWith("["))
                {
                    var macs = JsonSerializer.Deserialize<List<string>>(raw);
                    if (macs != null)
                    {
                        foreach (var m in macs)
                        {
                            if (!string.IsNullOrWhiteSpace(m))
                            {
                                col.Upsert(new DeviceRuleRecord
                                {
                                    Mac         = NormalizeMac(m),
                                    AutoForget  = true,
                                    AutoPair    = true,
                                    UpdatedAt   = DateTime.UtcNow
                                });
                            }
                        }
                    }
                }
                else if (raw.StartsWith("{"))
                {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;

                    var forgetSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (root.TryGetProperty("MacAddresses", out var macProp))
                    {
                        foreach (var el in macProp.EnumerateArray())
                        {
                            string? m = el.GetString();
                            if (!string.IsNullOrWhiteSpace(m)) forgetSet.Add(NormalizeMac(m));
                        }
                    }

                    var pairSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (root.TryGetProperty("AutoPairMacAddresses", out var pairProp))
                    {
                        foreach (var el in pairProp.EnumerateArray())
                        {
                            string? m = el.GetString();
                            if (!string.IsNullOrWhiteSpace(m)) pairSet.Add(NormalizeMac(m));
                        }
                    }

                    var all = forgetSet.Union(pairSet, StringComparer.OrdinalIgnoreCase);
                    foreach (var m in all)
                    {
                        col.Upsert(new DeviceRuleRecord
                        {
                            Mac        = m,
                            AutoForget = forgetSet.Contains(m),
                            AutoPair   = pairSet.Contains(m),
                            UpdatedAt  = DateTime.UtcNow
                        });
                    }

                    if (root.TryGetProperty("AutoFixEnabled", out var fixProp))
                    {
                        var s = GetSettings();
                        s.MasterAutoForget = fixProp.GetBoolean();
                        s.MasterAutoPair   = fixProp.GetBoolean();
                        SaveSettings(s);
                    }
                }
            }
        }
        catch { }
    }

    public static string NormalizeMac(string mac) =>
        mac.Replace("-", ":").Trim().ToUpperInvariant();

    public void Dispose()
    {
        _db.Dispose();
    }
}
