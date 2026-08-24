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

    private static readonly MethodInfo? SetEverInspectedMethod =
        AccessTools.Method(typeof(QuestManager), "SetEverInspected", new[] { typeof(int) });

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
        // 未受注クエストでは詳細UIを表示しても、納品などの操作系は無効のままにする
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
        Quest? quest = __instance.SelectedQuest;
        if (quest != null)
        {
            SetEverInspectedMethod?.Invoke(null, new object[] { quest.ID });
        }

        QuestBoardPanel.Refresh(__instance);
    }

    // RefreshEntryList の末尾に受注可能クエストのエントリを追加する
    // ShowingContent プロパティを汚染しないため RefreshEntryList をパッチする
    private static readonly FieldInfo? QuestEntryField =
        AccessTools.Field(typeof(QuestView), "questEntry");

    private static readonly FieldInfo? QuestEntryParentField =
        AccessTools.Field(typeof(QuestView), "questEntryParent");

    private static readonly FieldInfo? EntryListPlaceHolderField =
        AccessTools.Field(typeof(QuestView), "entryListPlaceHolder");

    private static readonly MethodInfo? PoolGetMethod =
        AccessTools.Method(typeof(QuestView).Assembly.GetType("Duckov.Utilities.PrefabPool`1")
            ?.MakeGenericType(typeof(QuestEntry)) ?? typeof(object), "Get",
            new[] { typeof(Transform) });

    private static readonly FieldInfo? QuestEntryPoolField =
        AccessTools.Field(typeof(QuestView), "_questEntryPool");

    private static readonly MethodInfo? SetMenuMethod =
        AccessTools.Method(typeof(QuestEntry), "SetMenu");

    [HarmonyPostfix]
    [HarmonyPatch("RefreshEntryList")]
    public static void RefreshEntryList(QuestView __instance)
    {
        if (__instance.ShowingContentType != QuestView.ShowContent.Active)
        {
            return;
        }

        // すでに Active なクエスト ID セット
        var activeIds = new HashSet<int>(
            QuestManager.Instance?.ActiveQuests
                .Where(q => q != null).Select(q => q.ID) ?? Enumerable.Empty<int>());

        QuestCollection? collection = GameplayDataSettings.QuestCollection;
        if (collection == null) return;

        // QuestEntryPool を取得
        object? pool = QuestEntryPoolField?.GetValue(__instance);
        if (pool == null) return;
        Transform? parent = QuestEntryParentField?.GetValue(__instance) as Transform;
        if (parent == null) return;

        // Pool.Get(parent) を呼び出すメソッドを動的取得
        MethodInfo? poolGet = pool.GetType().GetMethod("Get", new[] { typeof(Transform) });
        if (poolGet == null) return;

        MethodInfo? setupMethod = AccessTools.Method(typeof(QuestEntry), "Setup");
        // SetMenu は internal void SetMenu(ISingleSelectionMenu<QuestEntry>)
        MethodInfo? setMenuMethod = typeof(QuestEntry)
            .GetMethod("SetMenu",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

        bool addedAny = false;
        foreach (Quest quest in collection)
        {
            if (!CanAccept(quest)) continue;
            if (activeIds.Contains(quest.ID)) continue;

            QuestEntry? entry = poolGet.Invoke(pool, new object[] { parent }) as QuestEntry;
            if (entry == null) continue;
            setMenuMethod?.Invoke(entry, new object[] { __instance });
            setupMethod?.Invoke(entry, new object[] { quest });
            entry.transform.SetAsLastSibling();
            addedAny = true;
        }

        if (addedAny && EntryListPlaceHolderField?.GetValue(__instance) is GameObject placeHolder)
        {
            placeHolder.SetActive(false);
        }
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

    internal static Quest? GetSelectedActiveQuest(Quest? selectedQuest)
    {
        if (selectedQuest == null || QuestManager.Instance == null)
        {
            return null;
        }

        return QuestManager.Instance.ActiveQuests
            .FirstOrDefault(q => q != null && ReferenceEquals(q, selectedQuest));
    }
}

internal class QuestBoardPanel : MonoBehaviour
{
    private static readonly FieldInfo? DetailsField =
        AccessTools.Field(typeof(QuestView), "details");

    private static readonly FieldInfo? ContentFadeGroupField =
        AccessTools.Field(typeof(QuestViewDetails), "contentFadeGroup");

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
    private QuestCompletePanel? _completePanel;
    private bool _acceptMode;
    private bool _completeMode;
    private int _lastQuestId = -1;
    private bool _lastQuestActive;
    private bool _lastQuestComplete;
    private bool _lastTasksFinished;
    private bool _lastCanAccept;

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
            panel.EnsureCompletePanel();
            panel.CreateButton();
        }

        panel.RefreshButton();
    }

    public static void Refresh(QuestView view)
    {
        view?.GetComponent<QuestBoardPanel>()?.RefreshButton();
    }

    private void Start()
    {
        AlignToDetailsPanel();
        CaptureQuestState();
    }

    private void Update()
    {
        if (_view == null)
        {
            return;
        }

        Quest? quest = _view.SelectedQuest;
        int questId = quest?.ID ?? -1;
        bool questActive = QuestViewPatches.GetSelectedActiveQuest(quest) != null;
        bool questComplete = quest?.Complete ?? false;
        bool tasksFinished = quest?.AreTasksFinished() ?? false;
        bool canAccept = QuestViewPatches.CanAccept(quest);

        if (questId == _lastQuestId &&
            questActive == _lastQuestActive &&
            questComplete == _lastQuestComplete &&
            tasksFinished == _lastTasksFinished &&
            canAccept == _lastCanAccept)
        {
            return;
        }

        RefreshButton();
        CaptureQuestState();
    }

    private void CaptureQuestState()
    {
        Quest? quest = _view != null ? _view.SelectedQuest : null;
        _lastQuestId = quest?.ID ?? -1;
        _lastQuestActive = QuestViewPatches.GetSelectedActiveQuest(quest) != null;
        _lastQuestComplete = quest?.Complete ?? false;
        _lastTasksFinished = quest?.AreTasksFinished() ?? false;
        _lastCanAccept = QuestViewPatches.CanAccept(quest);
    }

    private void AlignToDetailsPanel()
    {
        if (_button == null) return;
        RectTransform? viewRect = _view?.GetComponent<RectTransform>();
        if (viewRect == null) return;

        RectTransform? alignRect = null;
        if (DetailsField?.GetValue(_view) is QuestViewDetails details)
        {
            if (ContentFadeGroupField?.GetValue(details) is Component fade)
                alignRect = fade.GetComponent<RectTransform>();
            alignRect ??= details.GetComponent<RectTransform>();
        }
        if (alignRect == null) return;

        Vector3[] corners = new Vector3[4];
        alignRect.GetWorldCorners(corners);
        // corners[0]=左下, corners[1]=左上, corners[2]=右上, corners[3]=右下
        Vector2 localMin = viewRect.InverseTransformPoint(corners[0]); // 左下
        Vector2 localMax = viewRect.InverseTransformPoint(corners[2]); // 右上
        Rect area = viewRect.rect;
        if (area.width < 1f || area.height < 1f) return;

        float aMinX = Mathf.Clamp01((localMin.x - area.xMin) / area.width);
        float aMaxX = Mathf.Clamp01((localMax.x - area.xMin) / area.width);
        // details 幅そのままだと少し左にはみ出して見えるため、左端だけ内側に寄せる
        aMinX = Mathf.Clamp01(aMinX + 0.05f);
        if (aMaxX - aMinX < 0.05f) return;

        RectTransform rect = _button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(aMinX, rect.anchorMin.y);
        rect.anchorMax = new Vector2(aMaxX, rect.anchorMax.y);
        rect.anchoredPosition = new Vector2(0f, rect.anchoredPosition.y);
    }

    private void CreateButton()
    {
        // クローンではなくゼロから作成（元の btn_Interact と重なってクリックを横取りされるのを防ぐ）
        var go = new GameObject("QuestBoard_InteractButton",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(_view.transform, false);
        go.SetActive(false);

        _button = go.GetComponent<Button>();
        _buttonImage = go.GetComponent<Image>();

        // NPC ボタンからビジュアルだけコピー
        Button? original = FindSourceInteractButton();
        if (original != null)
        {
            Image? srcImg = original.GetComponent<Image>();
            if (srcImg != null)
            {
                _buttonImage.sprite = srcImg.sprite;
                _buttonImage.type   = srcImg.type;
                _buttonImage.color  = srcImg.color;
            }
            _button.transition  = original.transition;
            _button.colors      = original.colors;
            _button.spriteState = original.spriteState;

            // テキストをゼロから追加してフォントだけコピー
            TextMeshProUGUI? srcTmp = original.GetComponentInChildren<TextMeshProUGUI>(true);
            var textGo = new GameObject("Text",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            _buttonText = textGo.GetComponent<TextMeshProUGUI>();
            _buttonText.alignment = TMPro.TextAlignmentOptions.Center;
            if (srcTmp != null)
            {
                _buttonText.font     = srcTmp.font;
                _buttonText.fontSize = srcTmp.fontSize;
                _buttonText.color    = srcTmp.color;
            }
        }
        else
        {
            _buttonImage.color = new Color(0.25f, 0.6f, 0.3f, 1f);
        }

        _button.targetGraphic = _buttonImage;
        _button.onClick.AddListener(OnClicked);

        var le = go.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin        = new Vector2(0.42f, 0.08f);
        rect.anchorMax        = new Vector2(0.97f, 0.08f);
        rect.pivot            = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 0f);
        rect.sizeDelta        = new Vector2(0f, 80f);
    }

    private void EnsureCompletePanel()
    {
        if (_completePanel != null)
        {
            return;
        }

        QuestCompletePanel? sourcePanel = null;
        QuestGiverView? giver = QuestGiverView.Instance
            ?? Resources.FindObjectsOfTypeAll<QuestGiverView>()
                .FirstOrDefault(v => v != null && v.gameObject.scene.isLoaded);

        if (giver != null && CompletePanelField != null)
        {
            sourcePanel = CompletePanelField.GetValue(giver) as QuestCompletePanel;
        }

        sourcePanel ??= Resources.FindObjectsOfTypeAll<QuestCompletePanel>()
            .FirstOrDefault(p => p != null);

        if (sourcePanel == null)
        {
            return;
        }

        Transform parent = _view.transform.parent != null ? _view.transform.parent : _view.transform;
        GameObject clone = Instantiate(sourcePanel.gameObject, parent, false);
        clone.name = "QuestBoard_CompletePanel";
        clone.SetActive(true);
        _completePanel = clone.GetComponent<QuestCompletePanel>();
    }

    // 受注できた固定レイアウトを基準にしつつ、x方向だけ詳細パネル幅へ寄せる。
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
            CaptureQuestState();
            return;
        }

        QuestGiverView? giver = QuestGiverView.Instance;
        if (QuestViewPatches.CanAccept(quest))
        {
            _acceptMode = true;
            ShowButton(true, giver != null ? giver.BtnText_AcceptQuest : "Accept");
            CaptureQuestState();
            return;
        }

        Quest? activeQuest = QuestViewPatches.GetSelectedActiveQuest(quest);
        if (activeQuest != null)
        {
            _completeMode = true;
            bool tasksFinished = activeQuest.AreTasksFinished();
            bool blockedInRaid = ModConfig.BlocksTurnInDuringRaid();
            bool interactable = tasksFinished && !blockedInRaid;
            string label;
            if (tasksFinished && blockedInRaid)
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
            CaptureQuestState();
            return;
        }

        _button.gameObject.SetActive(false);
        CaptureQuestState();
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
            Quest? activeQuest = QuestViewPatches.GetSelectedActiveQuest(quest);
            if (activeQuest == null)
            {
                return;
            }

            if (ModConfig.BlocksTurnInDuringRaid())
            {
                return;
            }

            if (!activeQuest.AreTasksFinished())
            {
                return;
            }

            if (!activeQuest.TryComplete())
            {
                return;
            }

            PlaySfx("UI/mission_large");
            ShowCompleteUi(activeQuest);
            RefreshButton();
        }
    }

    private static void PlaySfx(string key)
    {
        AccessTools.TypeByName("AudioManager")
            ?.GetMethod("Post", new[] { typeof(string) })
            ?.Invoke(null, new object[] { key });
    }

    private void ShowCompleteUi(Quest quest)
    {
        EnsureCompletePanel();
        _completePanel?.Show(quest).Forget();
    }
}
