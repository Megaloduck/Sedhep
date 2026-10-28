// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Sedhep.Infrastructure;

/// <summary>
/// The few preferences the app keeps beside the cookbook: who this device is, and how the user
/// likes the app to look. Kept in a small JSON file so it survives restarts without a database.
/// </summary>
public sealed class AppSettings
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");

    public string DeviceName { get; set; } = DefaultDeviceName();

    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";

    /// <summary>1.0 is the default type size; larger values make the whole cookbook easier to read.</summary>
    public double TextScale { get; set; } = 1.0;

    /// <summary>
    /// Where the cook left the window on a desktop. Null means "use the size the window asks for",
    /// which is what every phone and older settings file gets.
    /// </summary>
    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    public static string DefaultDeviceName()
    {
        var machine = Environment.MachineName;
        return string.IsNullOrWhiteSpace(machine) ? "My phone" : machine;
    }
}

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly ILogger<AppSettingsStore> _logger;
    private readonly object _lock = new();
    private AppSettings? _current;

    public AppSettingsStore(string path, ILogger<AppSettingsStore> logger)
    {
        _path = path;
        _logger = logger;
    }

    public AppSettings Current
    {
        get
        {
            lock (_lock)
            {
                return _current ??= Load();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            _current = settings;
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not save app settings to {Path}", _path);
            }
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions);
                if (loaded is not null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.DeviceId)) loaded.DeviceId = Guid.NewGuid().ToString("N");
                    if (string.IsNullOrWhiteSpace(loaded.DeviceName)) loaded.DeviceName = AppSettings.DefaultDeviceName();
                    if (loaded.TextScale < 1.0) loaded.TextScale = 1.0;
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read app settings; starting from defaults");
        }

        var fresh = new AppSettings();
        Save(fresh);
        return fresh;
    }
}
