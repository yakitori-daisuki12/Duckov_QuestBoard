using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov.Quests;
using Duckov.Quests.UI;
using Duckov.Utilities;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace QuestBoard;

[HarmonyPatch(typeof(QuestView))]
public static class QuestViewPatches
{
    private static readonly FieldInfo? DetailsField =
        AccessTools.Field(typeof(QuestView), "details");

    [HarmonyPostfix]
    [HarmonyPatch("OnOpen")]
    public static void OnOpen(QuestView __instance)
    {
        QuestBoardPanel.Attach(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch("RefreshDetails")]
    public static void RefreshDetails(QuestView __instance)
    {
        Quest? quest = __instance.SelectedQuest;
        bool interactable = quest != null && (quest.Active || quest.Complete);
        if (DetailsField?.GetValue(__instance) is QuestViewDetails details)
        {
            AccessTools.Field(typeof(QuestViewDetails), "interactable")?.SetValue(details, interactable);
        }

        QuestBoardPanel.Refresh(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(QuestView.SetSelection))]
    public static void SetSelection(QuestView __instance)
    {
        QuestBoardPanel.Refresh(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch("get_ShowingContent")]
    public static void ShowingContent(QuestView __instance, ref IList<Quest>? __result)
    {
        if (__instance.ShowingContentType != QuestView.ShowContent.Active)
        {
            return;
        }

        var combined = new List<Quest>();
        QuestCollection? collection = GameplayDataSettings.QuestCollection;
        if (collection != null)
        {
            foreach (Quest quest in collection)
            {
                if (CanAccept(quest))
                {
                    combined.Add(quest);
                }
            }
        }

        if (__result != null)
        {
            combined.AddRange(__result);
        }

        __result = combined;
    }

    private static readonly MethodInfo? IsQuestAvaliableMethod =
        AccessTools.Method(typeof(QuestManager), "IsQuestAvaliable", new[] { typeof(int) });

    internal static bool CanAccept(Quest? quest)
    {
        if (quest == null || QuestManager.Instance == null)
        {
            return false;
        }

        if (IsQuestAvaliableMethod != null)
        {
            return (bool)(IsQuestAvaliableMethod.Invoke(null, new object[] { quest.ID }) ?? false);
        }

        // フォールバック：ActiveQuests で確認（prefab.Active は常に false のため）
        bool alreadyActive = QuestManager.Instance.ActiveQuests.Any(q => q != null && q.ID == quest.ID);
        return !alreadyActive && !quest.Complete && quest.MeetsPrerequisit();
    }
}

internal class QuestBoardPanel : MonoBehaviour
{
    private static readonly FieldInfo? InteractButtonField =
        AccessTools.Field(typeof(QuestGiverView), "btn_Interact");

    private static readonly FieldInfo? CompletePanelField =
        AccessTools.Field(typeof(QuestGiverView), "questCompletePanel");

    private static readonly FieldInfo? InteractableColorField =
        AccessTools.Field(typeof(QuestGiverView), "interactableBtnImageColor");

    private static readonly FieldInfo? UninteractableColorField =
        AccessTools.Field(typeof(QuestGiverView), "uninteractableBtnImageColor");

    private QuestView _view = null!;
    private Button? _button;
    private Image? _buttonImage;
    private TextMeshProUGUI? _buttonText;
    private bool _acceptMode;
    private bool _completeMode;

    public static void Attach(QuestView view)
    {
        if (view == null)
        {
            return;
        }

        QuestBoardPanel? panel = view.GetComponent<QuestBoardPanel>();
        if (panel == null)
        {
            panel = view.gameObject.AddComponent<QuestBoardPanel>();
            panel._view = view;
            panel.CreateButton();
        }

        panel.RefreshButton();
    }

    public static void Refresh(QuestView view)
    {
        view?.GetComponent<QuestBoardPanel>()?.RefreshButton();
    }

    private void CreateButton()
    {
        Button? original = FindSourceInteractButton();
        if (original == null)
        {
            ModBehaviour.Log("QuestGiverView button not found.");
            return;
        }

        GameObject clone = Object.Instantiate(original.gameObject, _view.transform, false);
        clone.name = "QuestBoard_InteractButton";
        clone.SetActive(false);

        _button = clone.GetComponent<Button>();
        if (_button == null)
        {
            ModBehaviour.Log("btn_Interact clone has no Button.");
            return;
        }

        _button.onClick = new Button.ButtonClickedEvent();
        _button.onClick.AddListener(OnClicked);
        _buttonImage = clone.GetComponent<Image>();
        _buttonText = clone.GetComponentInChildren<TextMeshProUGUI>(true);

        RectTransform? rect = clone.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchorMin = new Vector2(0.55f, 0.04f);
            rect.anchorMax = new Vector2(0.96f, 0.12f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }

    private static Button? FindSourceInteractButton()
    {
        QuestGiverView? giver = QuestGiverView.Instance;
        if (giver == null)
        {
            giver = Resources.FindObjectsOfTypeAll<QuestGiverView>()
                .FirstOrDefault(view => view != null && view.gameObject.scene.isLoaded);
        }

        if (giver == null || InteractButtonField == null)
        {
            return null;
        }

        return InteractButtonField.GetValue(giver) as Button;
    }

    private void RefreshButton()
    {
        if (_button == null)
        {
            CreateButton();
        }

        if (_button == null)
        {
            return;
        }

        _acceptMode = false;
        _completeMode = false;
        Quest? quest = _view != null ? _view.SelectedQuest : null;
        if (quest == null)
        {
            _button.gameObject.SetActive(false);
            return;
        }

        QuestGiverView? giver = QuestGiverView.Instance;
        if (QuestViewPatches.CanAccept(quest))
        {
            _acceptMode = true;
            ShowButton(true, giver != null ? giver.BtnText_AcceptQuest : "Accept");
            return;
        }

        if (quest.Active)
        {
            _completeMode = true;
            bool tasksFinished = quest.AreTasksFinished();
            bool inRaid = IsInRaid();
            bool interactable = tasksFinished && !inRaid;
            string label;
            if (tasksFinished && inRaid)
            {
                label = L.Get(L.Keys.RaidTurnInBlocked);
            }
            else if (giver != null)
            {
                label = giver.BtnText_CompleteQuest;
            }
            else
            {
                label = "Complete";
            }

            ShowButton(interactable, label);
            return;
        }

        _button.gameObject.SetActive(false);
    }

    private void ShowButton(bool interactable, string label)
    {
        _button!.gameObject.SetActive(true);
        _button.interactable = interactable;
        if (_buttonText != null)
        {
            _buttonText.text = label;
        }

        QuestGiverView? giver = QuestGiverView.Instance;
        if (_buttonImage != null && giver != null)
        {
            var color = interactable
                ? InteractableColorField?.GetValue(giver)
                : UninteractableColorField?.GetValue(giver);
            if (color is Color c)
            {
                _buttonImage.color = c;
            }
        }
    }

    private void OnClicked()
    {
        Quest? quest = _view.SelectedQuest;
        if (quest == null || QuestManager.Instance == null)
        {
            return;
        }

        if (_acceptMode)
        {
            if (!QuestViewPatches.CanAccept(quest))
            {
                return;
            }

            QuestManager.Instance.ActivateQuest(quest.ID, quest.QuestGiverID);
            PlaySfx("UI/mission_accept");
            RefreshButton();
            return;
        }

        if (_completeMode)
        {
            if (IsInRaid())
            {
                return;
            }

            if (!quest.AreTasksFinished())
            {
                return;
            }

            if (!quest.TryComplete())
            {
                return;
            }

            PlaySfx("UI/mission_large");
            ShowCompleteUi(quest);
            RefreshButton();
        }
    }

    private static bool IsInRaid()
    {
        return LevelManager.Instance != null && !LevelManager.Instance.IsBaseLevel;
    }

    private static void PlaySfx(string key)
    {
        AccessTools.TypeByName("AudioManager")
            ?.GetMethod("Post", new[] { typeof(string) })
            ?.Invoke(null, new object[] { key });
    }

    private static void ShowCompleteUi(Quest quest)
    {
        QuestGiverView? giver = QuestGiverView.Instance;
        if (giver == null || CompletePanelField == null)
        {
            return;
        }

        if (CompletePanelField.GetValue(giver) is QuestCompletePanel panel)
        {
            panel.Show(quest).Forget();
        }
    }
}
