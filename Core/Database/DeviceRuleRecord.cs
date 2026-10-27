using System;
using LiteDB;

namespace BetterBluetooth.Core.Database;

/// <summary>
/// Persistent database entity representing automation rules for one device.
/// Stored in LiteDB (%APPDATA%\BetterBluetooth\better_bluetooth.db).
/// </summary>
public sealed class DeviceRuleRecord
{
    [BsonId]
    public string Mac { get; set; } = "";

    public string Name { get; set; } = "";

    public bool AutoForget { get; set; } = true;

    public bool AutoPair { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
