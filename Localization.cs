using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace QuestBoard;

public static class L
{
    public static class Keys
    {
        public const string RaidTurnInBlocked = "QuestBoard.RaidTurnInBlocked";
    }

    private static readonly Dictionary<SystemLanguage, Dictionary<string, string>> Tables =
        new()
        {
            [SystemLanguage.English] = new Dictionary<string, string>
            {
                [Keys.RaidTurnInBlocked] = "Cannot turn in during raid"
            },
            [SystemLanguage.Japanese] = new Dictionary<string, string>
            {
                [Keys.RaidTurnInBlocked] = "出撃中は報告できない"
            },
            [SystemLanguage.ChineseSimplified] = new Dictionary<string, string>
            {
                [Keys.RaidTurnInBlocked] = "出击中无法交付"
            },
            [SystemLanguage.Korean] = new Dictionary<string, string>
            {
                [Keys.RaidTurnInBlocked] = "레이드 중에는 보고할 수 없습니다"
            }
        };

    private static bool _initialized;
    private static SystemLanguage _currentLanguage = SystemLanguage.English;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _currentLanguage = DetectLanguage();
        SubscribeLanguageChanged();
        _initialized = true;
    }

    public static string Get(string key)
    {
        if (Tables.TryGetValue(_currentLanguage, out var table) &&
            table.TryGetValue(key, out var value))
        {
            return value;
        }

        if (Tables.TryGetValue(SystemLanguage.English, out var englishTable) &&
            englishTable.TryGetValue(key, out var englishValue))
        {
            return englishValue;
        }

        return key;
    }

    private static SystemLanguage DetectLanguage()
    {
        SystemLanguage? gameLanguage = TryGetGameLanguage();
        if (gameLanguage.HasValue)
        {
            return NormalizeLanguage(gameLanguage.Value);
        }

        return NormalizeLanguage(Application.systemLanguage);
    }

    private static SystemLanguage NormalizeLanguage(SystemLanguage language)
    {
        return language switch
        {
            SystemLanguage.Japanese => SystemLanguage.Japanese,
            SystemLanguage.Korean => SystemLanguage.Korean,
            SystemLanguage.Chinese or SystemLanguage.ChineseSimplified or SystemLanguage.ChineseTraditional =>
                SystemLanguage.ChineseSimplified,
            _ => SystemLanguage.English
        };
    }

    private static Type? FindLocalizationManager()
    {
        Type? managerType = Type.GetType("SodaCraft.Localizations.LocalizationManager, TeamSoda.Localization");
        if (managerType != null)
        {
            return managerType;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            managerType = assembly.GetType("SodaCraft.Localizations.LocalizationManager");
            if (managerType != null)
            {
                return managerType;
            }
        }

        return null;
    }

    private static SystemLanguage? TryGetGameLanguage()
    {
        try
        {
            Type? managerType = FindLocalizationManager();
            if (managerType == null)
            {
                return null;
            }

            foreach (var memberName in new[] { "CurrentLanguage", "Language", "currentLanguage" })
            {
                PropertyInfo? property = managerType.GetProperty(memberName,
                    BindingFlags.Public | BindingFlags.Static);
                if (property?.PropertyType == typeof(SystemLanguage))
                {
                    return (SystemLanguage)property.GetValue(null)!;
                }

                FieldInfo? field = managerType.GetField(memberName,
                    BindingFlags.Public | BindingFlags.Static);
                if (field?.FieldType == typeof(SystemLanguage))
                {
                    return (SystemLanguage)field.GetValue(null)!;
                }
            }
        }
        catch (Exception e)
        {
            ModBehaviour.Log($"Failed to detect game language: {e.Message}");
        }

        return null;
    }

    private static void SubscribeLanguageChanged()
    {
        try
        {
            Type? managerType = FindLocalizationManager();
            EventInfo? languageChangedEvent = managerType?.GetEvent("OnSetLanguage",
                BindingFlags.Public | BindingFlags.Static);
            MethodInfo? addMethod = languageChangedEvent?.GetAddMethod(false);
            if (addMethod == null)
            {
                return;
            }

            Type actionType = typeof(Action<>).MakeGenericType(typeof(SystemLanguage));
            Delegate handler = Delegate.CreateDelegate(actionType, typeof(L), nameof(OnGameLanguageChanged));
            addMethod.Invoke(null, new object[] { handler });
        }
        catch (Exception e)
        {
            ModBehaviour.Log($"Failed to subscribe language change: {e.Message}");
        }
    }

    private static void OnGameLanguageChanged(SystemLanguage language)
    {
        _currentLanguage = NormalizeLanguage(language);
    }
}
