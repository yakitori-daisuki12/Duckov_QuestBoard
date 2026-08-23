using HarmonyLib;
using UnityEngine;

namespace QuestBoard;

public class ModBehaviour : Duckov.Modding.ModBehaviour
{
    public const string ModName = "QuestBoard";

    public static void Log(object message)
    {
        Debug.Log($"[{ModName}] {message}");
    }

    public static void LogError(object message)
    {
        Debug.LogError($"[{ModName}] {message}");
    }

    void Awake()
    {
        var harmony = new Harmony("com.yakitori.questboard");
        harmony.PatchAll();
        L.Initialize();
    }

    protected override void OnAfterSetup()
    {
        ModConfig.Load(info.path);
        Log($"Loaded. allowTurnInDuringRaid={ModConfig.AllowTurnInDuringRaid}");
    }
}
