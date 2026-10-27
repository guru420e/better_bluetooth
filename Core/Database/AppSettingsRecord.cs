using System;
using LiteDB;

namespace BetterBluetooth.Core.Database;

/// <summary>
/// Persistent database entity representing global application configuration.
/// Stored in LiteDB (%APPDATA%\BetterBluetooth\better_bluetooth.db).
/// </summary>
public sealed class AppSettingsRecord
{
    [BsonId]
    public string Id { get; set; } = "global";

    public bool MasterAutoForget { get; set; } = true;

    public bool MasterAutoPair { get; set; } = true;

    public bool StartWithWindows { get; set; } = false;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
