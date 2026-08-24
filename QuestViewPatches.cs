using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov.Quests;
using Duckov.Quests.Tasks;
using Duckov.Quests.UI;
using Duckov.UI;
using Duckov.Utilities;
using HarmonyLib;
using ItemStatsSystem;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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
            QuestItemHoverForwarder.ScheduleAttach(details);
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

[HarmonyPatch(typeof(RewardEntry))]
public static class RewardEntryPatches
{
    private static readonly FieldInfo? RewardIconField =
        AccessTools.Field(typeof(RewardEntry), "rewardIcon");

    [HarmonyPostfix]
    [HarmonyPatch("Setup")]
    public static void Setup(RewardEntry __instance)
    {
        QuestItemHoverForwarder.SyncReward(__instance, RewardIconField?.GetValue(__instance) as Image);
    }
}

[HarmonyPatch(typeof(TaskEntry))]
public static class TaskEntryPatches
{
    private static readonly FieldInfo? TaskIconField =
        AccessTools.Field(typeof(TaskEntry), "taskIcon");

    [HarmonyPostfix]
    [HarmonyPatch("Setup")]
    public static void Setup(TaskEntry __instance)
    {
        QuestItemHoverForwarder.SyncTask(__instance, TaskIconField?.GetValue(__instance) as Image);
    }
}

[HarmonyPatch(typeof(QuestRequiredItem))]
public static class QuestRequiredItemPatches
{
    private static readonly FieldInfo? IconField =
        AccessTools.Field(typeof(QuestRequiredItem), "icon");

    [HarmonyPostfix]
    [HarmonyPatch(nameof(QuestRequiredItem.Set))]
    public static void Set(QuestRequiredItem __instance, int itemTypeID)
    {
        QuestItemHoverForwarder.SyncRequiredItem(
            __instance,
            IconField?.GetValue(__instance) as Image,
            itemTypeID);
    }
}

/// <summary>
/// 行オブジェクトで Update しつつ、当たり判定はアイコンの画面座標だけを使う。
/// typeID は表示直前にクエストデータから読み直す。表示自体はゲーム本体の ItemHoveringUI に任せる。
/// </summary>
internal class QuestItemHoverForwarder : MonoBehaviour
{
    private const string MetaHostName = "QB_HoverMeta";
    private const string HitPadName = "QB_HoverHit";
    private const float MinHitSize = 48f;

    private enum SourceKind
    {
        None,
        Task,
        Reward,
        RequiredItem
    }

    private static readonly FieldInfo? MetaDisplayDataField =
        AccessTools.Field(typeof(ItemMetaDisplay), "data");

    private static readonly FieldInfo? TaskTargetField =
        AccessTools.Field(typeof(TaskEntry), "target");

    private static readonly FieldInfo? RewardTargetField =
        AccessTools.Field(typeof(RewardEntry), "target");

    private static readonly FieldInfo? RequiredItemField =
        AccessTools.Field(typeof(QuestViewDetails), "requiredItem");

    private static readonly FieldInfo? TaskIconField =
        AccessTools.Field(typeof(TaskEntry), "taskIcon");

    private static readonly FieldInfo? RewardIconField =
        AccessTools.Field(typeof(RewardEntry), "rewardIcon");

    private static readonly FieldInfo? RequiredIconField =
        AccessTools.Field(typeof(QuestRequiredItem), "icon");

    private static readonly MethodInfo? SetupAndShowMetaMethod =
        typeof(ItemHoveringUI)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "SetupAndShowMeta" && m.IsGenericMethodDefinition);

    private static readonly MethodInfo? HideMethod =
        AccessTools.Method(typeof(ItemHoveringUI), "Hide");

    private static readonly MethodInfo? RefreshPositionMethod =
        AccessTools.Method(typeof(ItemHoveringUI), "RefreshPosition");

    private static readonly FieldInfo? FadeGroupField =
        AccessTools.Field(typeof(ItemHoveringUI), "fadeGroup");

    private static readonly Vector3[] WorldCorners = new Vector3[4];

    private static int _attachGeneration;
    private static int _selectFrame = -1;
    private static QuestItemHoverForwarder? _bestThisFrame;
    private static float _bestArea = float.MaxValue;

    private SourceKind _kind;
    private TaskEntry? _taskEntry;
    private RewardEntry? _rewardEntry;
    private QuestRequiredItem? _requiredItem;
    private RectTransform? _hitRect;
    private ItemMetaDisplay? _metaDisplay;
    private bool _hovering;
    private int _shownTypeId = -1;

    public static void ScheduleAttach(QuestViewDetails details)
    {
        int generation = ++_attachGeneration;
        AttachAfterRefresh(details, generation).Forget();
    }

    private static async UniTaskVoid AttachAfterRefresh(QuestViewDetails details, int generation)
    {
        await UniTask.DelayFrame(3);
        if (details == null || generation != _attachGeneration)
        {
            return;
        }

        // プール済み（非アクティブ）は触らない。アクティブな行だけ同期する。
        foreach (TaskEntry entry in details.GetComponentsInChildren<TaskEntry>(false))
        {
            SyncTask(entry, TaskIconField?.GetValue(entry) as Image);
        }

        foreach (RewardEntry entry in details.GetComponentsInChildren<RewardEntry>(false))
        {
            SyncReward(entry, RewardIconField?.GetValue(entry) as Image);
        }

        if (RequiredItemField?.GetValue(details) is QuestRequiredItem required)
        {
            int typeID = details.Target != null ? details.Target.RequiredItemID : 0;
            SyncRequiredItem(required, RequiredIconField?.GetValue(required) as Image, typeID);
        }
    }

    public static void SyncTask(TaskEntry entry, Image? icon)
    {
        if (entry == null)
        {
            return;
        }

        CleanupOldBindings(entry.gameObject, icon);
        int typeID = TaskTargetField?.GetValue(entry) is SubmitItems submit ? submit.ItemTypeID : 0;
        QuestItemHoverForwarder hover = GetOrAdd(entry.gameObject);
        if (typeID <= 0 || icon == null || !icon.gameObject.activeSelf)
        {
            hover.Shutdown();
            return;
        }

        hover.Configure(SourceKind.Task, EnsureHitPad(icon), entry, null, null);
    }

    public static void SyncReward(RewardEntry entry, Image? icon)
    {
        if (entry == null)
        {
            return;
        }

        CleanupOldBindings(entry.gameObject, icon);
        int typeID = GetItemTypeId(RewardTargetField?.GetValue(entry));
        QuestItemHoverForwarder hover = GetOrAdd(entry.gameObject);
        if (typeID <= 0 || icon == null || !icon.gameObject.activeSelf)
        {
            hover.Shutdown();
            return;
        }

        hover.Configure(SourceKind.Reward, EnsureHitPad(icon), null, entry, null);
    }

    public static void SyncRequiredItem(QuestRequiredItem required, Image? icon, int itemTypeID)
    {
        if (required == null)
        {
            return;
        }

        CleanupOldBindings(required.gameObject, icon);
        QuestItemHoverForwarder hover = GetOrAdd(required.gameObject);
        if (itemTypeID <= 0 || icon == null || !required.gameObject.activeInHierarchy)
        {
            hover.Shutdown();
            return;
        }

        // 必要アイテムの Image が非アクティブでも、親が有効ならヒットパッドを付ける
        icon.gameObject.SetActive(true);
        hover.Configure(SourceKind.RequiredItem, EnsureHitPad(icon), null, null, required);
    }

    private static QuestItemHoverForwarder GetOrAdd(GameObject host)
    {
        return host.GetComponent<QuestItemHoverForwarder>()
            ?? host.AddComponent<QuestItemHoverForwarder>();
    }

    private static RectTransform EnsureHitPad(Image icon)
    {
        Transform? existing = icon.transform.Find(HitPadName);
        GameObject padGo;
        if (existing != null)
        {
            padGo = existing.gameObject;
        }
        else
        {
            padGo = new GameObject(HitPadName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            padGo.transform.SetParent(icon.transform, false);
        }

        RectTransform rt = padGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(MinHitSize, MinHitSize);

        Image padImage = padGo.GetComponent<Image>();
        padImage.color = new Color(1f, 1f, 1f, 0f);
        padImage.raycastTarget = false;
        padGo.SetActive(true);
        return rt;
    }

    // 以前アイコン側に付けていたコンポーネントを外し、本体のポインターイベントが誤発火しないようにする
    private static void CleanupOldBindings(GameObject root, Image? icon)
    {
        if (icon != null && icon.gameObject != root)
        {
            foreach (QuestItemHoverForwarder hover in icon.GetComponents<QuestItemHoverForwarder>())
            {
                hover.Shutdown();
                UnityEngine.Object.Destroy(hover);
            }

            foreach (ItemMetaDisplay meta in icon.GetComponents<ItemMetaDisplay>())
            {
                UnityEngine.Object.Destroy(meta);
            }
        }

        foreach (ItemMetaDisplay meta in root.GetComponents<ItemMetaDisplay>())
        {
            UnityEngine.Object.Destroy(meta);
        }
    }

    private void Configure(
        SourceKind kind,
        RectTransform hitRect,
        TaskEntry? task,
        RewardEntry? reward,
        QuestRequiredItem? required)
    {
        _kind = kind;
        _hitRect = hitRect;
        _taskEntry = task;
        _rewardEntry = reward;
        _requiredItem = required;
        _metaDisplay = EnsureMetaHost();
        _shownTypeId = -1;
        enabled = true;
    }

    private ItemMetaDisplay EnsureMetaHost()
    {
        Transform? existing = transform.Find(MetaHostName);
        GameObject host;
        if (existing != null)
        {
            host = existing.gameObject;
        }
        else
        {
            host = new GameObject(MetaHostName, typeof(RectTransform));
            host.transform.SetParent(transform, false);
        }

        host.SetActive(true);
        return host.GetComponent<ItemMetaDisplay>() ?? host.AddComponent<ItemMetaDisplay>();
    }

    public static int GetItemTypeId(object? source)
    {
        if (source == null)
        {
            return 0;
        }

        foreach (string fieldName in new[] { "itemTypeID", "unlockItem" })
        {
            FieldInfo? field = AccessTools.Field(source.GetType(), fieldName);
            if (field?.GetValue(source) is int value && value > 0)
            {
                return value;
            }
        }

        foreach (string propertyName in new[] { "ItemTypeID", "UnlockItem" })
        {
            PropertyInfo? property = AccessTools.Property(source.GetType(), propertyName);
            if (property?.GetValue(source) is int value && value > 0)
            {
                return value;
            }
        }

        return 0;
    }

    private int ResolveTypeId()
    {
        switch (_kind)
        {
            case SourceKind.Task:
                return _taskEntry != null &&
                       TaskTargetField?.GetValue(_taskEntry) is SubmitItems submit
                    ? submit.ItemTypeID
                    : 0;

            case SourceKind.Reward:
                return _rewardEntry != null
                    ? GetItemTypeId(RewardTargetField?.GetValue(_rewardEntry))
                    : 0;

            case SourceKind.RequiredItem:
                if (_requiredItem == null)
                {
                    return 0;
                }

                QuestViewDetails? details = _requiredItem.GetComponentInParent<QuestViewDetails>();
                return details?.Target != null ? details.Target.RequiredItemID : 0;

            default:
                return 0;
        }
    }

    private void Shutdown()
    {
        if (_hovering)
        {
            _hovering = false;
            HideHover();
        }

        _kind = SourceKind.None;
        _taskEntry = null;
        _rewardEntry = null;
        _requiredItem = null;
        _hitRect = null;
        _shownTypeId = -1;
        enabled = false;
    }

    private void OnDisable()
    {
        if (_hovering)
        {
            _hovering = false;
            _shownTypeId = -1;
            HideHover();
        }
    }

    private void Update()
    {
        if (_selectFrame != Time.frameCount)
        {
            _selectFrame = Time.frameCount;
            _bestThisFrame = null;
            _bestArea = float.MaxValue;
        }

        if (Mouse.current == null || !isActiveAndEnabled || _kind == SourceKind.None || _hitRect == null)
        {
            return;
        }

        if (ResolveTypeId() <= 0)
        {
            return;
        }

        Vector2 mouse = Mouse.current.position.value;
        if (!TryGetScreenHitRect(_hitRect, out Rect screenRect))
        {
            return;
        }

        if (!screenRect.Contains(mouse))
        {
            return;
        }

        float area = screenRect.width * screenRect.height;
        if (area < _bestArea)
        {
            _bestArea = area;
            _bestThisFrame = this;
        }
    }

    // Canvas のカメラ設定に依存しにくいよう、ワールド角→画面座標で判定する
    private static bool TryGetScreenHitRect(RectTransform rect, out Rect screenRect)
    {
        screenRect = default;
        if (rect == null)
        {
            return false;
        }

        Canvas? canvas = rect.GetComponentInParent<Canvas>()?.rootCanvas;
        Camera? camera = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            camera = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        }

        rect.GetWorldCorners(WorldCorners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, WorldCorners[0]);
        Vector2 max = min;
        for (int i = 1; i < 4; i++)
        {
            Vector2 p = RectTransformUtility.WorldToScreenPoint(camera, WorldCorners[i]);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        float width = Mathf.Max(MinHitSize, max.x - min.x);
        float height = Mathf.Max(MinHitSize, max.y - min.y);
        float cx = (min.x + max.x) * 0.5f;
        float cy = (min.y + max.y) * 0.5f;
        screenRect = new Rect(cx - width * 0.5f, cy - height * 0.5f, width, height);
        return true;
    }

    private void LateUpdate()
    {
        if (_bestThisFrame == this && isActiveAndEnabled)
        {
            int typeID = ResolveTypeId();
            if (typeID <= 0)
            {
                if (_hovering)
                {
                    _hovering = false;
                    _shownTypeId = -1;
                    HideHover();
                }

                return;
            }

            // 毎フレーム呼ぶと DuckovFishingInfo の Postfix 例外で LateUpdate が壊れるため、進入時だけ
            if (!_hovering || _shownTypeId != typeID)
            {
                _hovering = true;
                _shownTypeId = typeID;
                ShowHover();
            }

            return;
        }

        if (_hovering)
        {
            _hovering = false;
            _shownTypeId = -1;
            HideHover();
        }
    }

    private void ShowHover()
    {
        int typeID = ResolveTypeId();
        if (typeID <= 0 || SetupAndShowMetaMethod == null)
        {
            return;
        }

        ItemHoveringUI? hoverUi = ItemHoveringUI.Instance
            ?? Resources.FindObjectsOfTypeAll<ItemHoveringUI>().FirstOrDefault(ui => ui != null);
        if (hoverUi == null)
        {
            return;
        }

        ItemMetaData meta = ItemAssetsCollection.GetMetaData(typeID);
        if (meta.id <= 0)
        {
            return;
        }

        if (!hoverUi.gameObject.activeSelf)
        {
            hoverUi.gameObject.SetActive(true);
        }

        // クエストUIより手前に出す
        Canvas? canvas = hoverUi.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvas.overrideSorting = true;
            if (canvas.sortingOrder < 5000)
            {
                canvas.sortingOrder = 5000;
            }
        }

        _metaDisplay ??= EnsureMetaHost();
        _metaDisplay.gameObject.SetActive(true);
        MetaDisplayDataField?.SetValue(_metaDisplay, meta);

        try
        {
            SetupAndShowMetaMethod
                .MakeGenericMethod(typeof(ItemMetaDisplay))
                .Invoke(hoverUi, new object[] { _metaDisplay });
        }
        catch (TargetInvocationException)
        {
            // DuckovFishingInfo が非魚アイテムで Postfix 例外を投げる。本体の表示処理は完了済み。
        }
        catch (Exception ex)
        {
            ModBehaviour.LogError($"Hover show failed: {ex.Message}");
            return;
        }

        // 例外後でも表示を確定させる
        RefreshPositionMethod?.Invoke(hoverUi, null);
        object? fadeGroup = FadeGroupField?.GetValue(hoverUi);
        if (fadeGroup != null)
        {
            AccessTools.Method(fadeGroup.GetType(), "Show")?.Invoke(fadeGroup, null);
        }
    }

    private void HideHover()
    {
        ItemHoveringUI? hoverUi = ItemHoveringUI.Instance;
        if (hoverUi != null)
        {
            HideMethod?.Invoke(hoverUi, null);
        }
    }
}
