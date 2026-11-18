// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SonyHeadphonesFlyout;

internal sealed record AmbientSetting(AmbientMode Mode, int Level, bool VoiceFocus);

internal sealed record HeadphoneSettings(AmbientSetting? Ambient = null, int? Vpt = null);

internal sealed class HeadphoneSettingsStore
{
    private readonly string _path;
    private readonly Dictionary<ulong, HeadphoneSettings> _settings;

    public HeadphoneSettingsStore(string? path = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SonyHeadphonesFlyout", "settings.json"));
        _settings = Load();
    }

    public event Action<ulong>? Changed;

    public HeadphoneSettings Get(ulong address) => _settings.GetValueOrDefault(address) ?? new();

    public void ConfirmAmbient(ulong address, AmbientSetting setting) => Save(address, Get(address) with { Ambient = setting });

    public void ConfirmVpt(ulong address, int preset) => Save(address, Get(address) with { Vpt = preset });

    private Dictionary<ulong, HeadphoneSettings> Load()
    {
        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<ulong, HeadphoneSettings>>(File.ReadAllText(_path));
            if (stored is null) return [];

            var valid = new Dictionary<ulong, HeadphoneSettings>();
            foreach (var (address, settings) in stored)
            {
                if (settings is null) continue;
                var ambient = settings.Ambient is { Level: >= 0 and <= 19 } choice && Enum.IsDefined(choice.Mode) ? choice : null;
                var vpt = settings.Vpt is >= 0 and <= 4 ? settings.Vpt : null;
                if (ambient is not null || vpt.HasValue) valid[address] = new(ambient, vpt);
            }
            return valid;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private void Save(ulong address, HeadphoneSettings settings)
    {
        if (Get(address) == settings) return;
        _settings[address] = settings;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_settings));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Keep confirmed settings available during this app session if disk storage is unavailable.
        }

        Changed?.Invoke(address);
    }
}
