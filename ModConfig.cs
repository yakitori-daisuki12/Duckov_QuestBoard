using System;
using System.IO;
using UnityEngine;

namespace QuestBoard;

[Serializable]
public class ModSettings
{
    public bool allowTurnInDuringRaid;
}

public static class ModConfig
{
    private const string SettingsFileName = "Settings.json";
    private const string DefaultSettingsFileName = "Settings.default.json";

    public static bool AllowTurnInDuringRaid { get; private set; }

    public static void Load(string modPath)
    {
        AllowTurnInDuringRaid = false;

        if (string.IsNullOrWhiteSpace(modPath))
        {
            ModBehaviour.LogError("Mod path is empty. Using default settings.");
            return;
        }

        string settingsPath = Path.Combine(modPath, SettingsFileName);
        string defaultPath = Path.Combine(modPath, DefaultSettingsFileName);

        try
        {
            if (!File.Exists(settingsPath))
            {
                EnsureUserSettings(settingsPath, defaultPath);
            }

            if (!File.Exists(settingsPath))
            {
                ModBehaviour.Log("Settings file not found. Using default settings.");
                return;
            }

            string json = File.ReadAllText(settingsPath);
            ModSettings? settings = JsonUtility.FromJson<ModSettings>(json);
            if (settings == null)
            {
                ModBehaviour.LogError($"Failed to parse {SettingsFileName}. Using default settings.");
                return;
            }

            AllowTurnInDuringRaid = settings.allowTurnInDuringRaid;
            ModBehaviour.Log(
                $"Settings loaded: allowTurnInDuringRaid={AllowTurnInDuringRaid}");
        }
        catch (Exception ex)
        {
            ModBehaviour.LogError($"Failed to load settings: {ex.Message}");
        }
    }

    public static bool BlocksTurnInDuringRaid()
    {
        return !AllowTurnInDuringRaid && IsInRaid();
    }

    public static bool IsInRaid()
    {
        return LevelManager.Instance != null && !LevelManager.Instance.IsBaseLevel;
    }

    private static void EnsureUserSettings(string settingsPath, string defaultPath)
    {
        if (!File.Exists(defaultPath))
        {
            return;
        }

        File.Copy(defaultPath, settingsPath);
        ModBehaviour.Log($"Created {SettingsFileName} from {DefaultSettingsFileName}.");
    }
}
