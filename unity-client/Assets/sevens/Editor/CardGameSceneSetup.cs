using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>
/// CardGame 씬과 Card 프리팹을 자동 생성하는 에디터 도구.
///
/// 메뉴: Tools → Setup CardGame Scene (카드게임 씬 생성)
///
/// 생성물:
///   - Assets/sevens/Prefabs/CardGame/CardUI.prefab  (Kenney 스프라이트 기반 단일 Image)
///   - Assets/sevens/Scenes/CardGame.unity
///
/// 씬 구성:
///   - Canvas
///     ├─ FieldArea (placeholder)
///     ├─ HandArea (FanLayout)
///     ├─ PassButton
///     ├─ TopProfile / LeftProfile / RightProfile (OpponentProfile 부착)
///     └─ FadeCanvas
///   - EventSystem
///   - FieldManager (+FieldManagerSelfTest)
///   - HandManager
///   - CardGameManager (게임 진행 — 자동 라운드 시작)
///   - GameSceneManager
/// </summary>
public class CardGameSceneSetup : EditorWindow
{
    private const string PrefabFolder      = "Assets/sevens/Prefabs/CardGame";
    private const string ScenesFolder      = "Assets/sevens/Scenes";
    private const string CardPrefabPath    = PrefabFolder + "/CardUI.prefab";
    private const string CardGameScenePath = ScenesFolder + "/CardGame.unity";

    [MenuItem("Tools/Setup CardGame Scene (카드게임 씬 생성)")]
    public static void Run()
    {
        EnsureFolders();

        // 카드 텍스처들이 Sprite로 임포트돼 있도록 보장.
        CardTextureImporter.ReimportAllCards();

        // 1) 프리팹부터 만들고 AssetDatabase에 영구 등록.
        CreateOrUpdateCardPrefab();

        // 2) 씬을 새로 만들고, 그 안에서 프리팹을 다시 로드해 와이어링.
        CreateCardGameScene();

        EditorUtility.DisplayDialog(
            "CardGame 씬 생성 완료",
            "1) File → Build Settings에 CardGame 씬 추가\n" +
            "2) Play 시 CardGameManager가 자동으로 7 깔고 4인 12장씩 분배\n" +
            "3) 내 차례 = 카드 클릭 → 다시 클릭(제출) / 낼 카드 없으면 패스 버튼 활성화\n" +
            "4) AI 차례는 0.8초 후 자동 진행",
            "확인");
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/sevens/Prefabs"))
            AssetDatabase.CreateFolder("Assets/sevens", "Prefabs");
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets/sevens/Prefabs", "CardGame");
        if (!AssetDatabase.IsValidFolder(ScenesFolder))
            AssetDatabase.CreateFolder("Assets/sevens", "Scenes");
    }

    // ─────────────────────────────────────────────────────────────
    //  Card Prefab — Kenney 스프라이트 기반 단일 Image
    // ─────────────────────────────────────────────────────────────

    private static void CreateOrUpdateCardPrefab()
    {
        GameObject root = new GameObject("CardUI");
        RectTransform rootRT = root.AddComponent<RectTransform>();
        rootRT.sizeDelta = new Vector2(120f, 168f); // ~5:7 비율
        rootRT.anchorMin = new Vector2(0.5f, 0.5f);
        rootRT.anchorMax = new Vector2(0.5f, 0.5f);
        rootRT.pivot     = new Vector2(0.5f, 0.5f);

        // 카드 앞면 Image — CardUI가 SetCard 호출 시 스프라이트 교체.
        Image faceImg = root.AddComponent<Image>();
        faceImg.color = Color.white;
        faceImg.raycastTarget = true;
        faceImg.preserveAspect = true;

        // 디밍 / 페이드용 CanvasGroup
        var cg = root.AddComponent<CanvasGroup>();
        cg.alpha = 1f;

        // CardUI 컴포넌트 부착 + 참조 와이어링
        CardUI cardUI = root.AddComponent<CardUI>();
        var so = new SerializedObject(cardUI);
        so.FindProperty("faceImage").objectReferenceValue   = faceImg;
        so.FindProperty("canvasGroup").objectReferenceValue = cg;
        so.ApplyModifiedProperties();

        // 프리팹 저장 (참조는 씬 빌드 시 LoadAssetAtPath로 다시 가져옴)
        PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(CardPrefabPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        Debug.Log("[CardGameSceneSetup] Card 프리팹 생성 완료: " + CardPrefabPath);
    }

    // ─────────────────────────────────────────────────────────────
    //  Scene
    // ─────────────────────────────────────────────────────────────

    private static void CreateCardGameScene()
    {
        var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 디스크의 프리팹을 다시 로드
        var cardPrefabGO = AssetDatabase.LoadAssetAtPath<GameObject>(CardPrefabPath);
        if (cardPrefabGO == null)
        {
            Debug.LogError("[CardGameSceneSetup] 프리팹 로드 실패: " + CardPrefabPath);
            return;
        }
        var cardPrefab = cardPrefabGO.GetComponent<CardUI>();
        if (cardPrefab == null)
        {
            Debug.LogError("[CardGameSceneSetup] 프리팹에 CardUI 컴포넌트가 없습니다: " + CardPrefabPath);
            return;
        }

        // === Camera ===
        GameObject camObj = new GameObject("Main Camera");
        camObj.tag = "MainCamera";
        var cam = camObj.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.10f, 0.14f);
        cam.orthographic = true;
        camObj.AddComponent<AudioListener>();

        // === Main Canvas ===
        GameObject canvasObj = new GameObject("Canvas");
        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObj.AddComponent<GraphicRaycaster>();

        // === EventSystem ===
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        // === FieldArea: 4줄 × 13슬롯 그리드 ===
        var fieldArea = CreatePanel(canvasObj.transform, "FieldArea",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 120), new Vector2(1100, 420),
            new Color(0.18f, 0.20f, 0.26f, 0.85f));

        // 4줄: ♠ ♥ ♦ ♣ (위에서 아래)
        // FieldManager는 아직 생성 전이므로 임시로 null 전달, 나중에 Bind 호출.
        var fieldLineSpade   = CreateFieldLine(fieldArea.transform, Suit.Spade,   +158f);
        var fieldLineHeart   = CreateFieldLine(fieldArea.transform, Suit.Heart,   +53f);
        var fieldLineDiamond = CreateFieldLine(fieldArea.transform, Suit.Diamond, -53f);
        var fieldLineClub    = CreateFieldLine(fieldArea.transform, Suit.Club,    -158f);

        // === HandArea (Fan) ===
        GameObject handArea = new GameObject("HandArea");
        handArea.transform.SetParent(canvasObj.transform, false);
        RectTransform handRT = handArea.AddComponent<RectTransform>();
        handRT.anchorMin = new Vector2(0.5f, 0f);
        handRT.anchorMax = new Vector2(0.5f, 0f);
        handRT.pivot = new Vector2(0.5f, 0.5f);
        handRT.anchoredPosition = new Vector2(0, 140f);
        handRT.sizeDelta = new Vector2(1400, 280);
        FanLayout fan = handArea.AddComponent<FanLayout>();

        var fanSO = new SerializedObject(fan);
        fanSO.FindProperty("radius").floatValue            = 500f;
        fanSO.FindProperty("anglePerCard").floatValue      = 7f;
        fanSO.FindProperty("maxTotalAngle").floatValue     = 55f;
        fanSO.FindProperty("centerAngleOffset").floatValue = 0f;
        fanSO.FindProperty("rotateChildren").boolValue     = true;
        fanSO.FindProperty("pivotYOffset").floatValue      = -440f;
        fanSO.ApplyModifiedProperties();

        // === Pass Button ===
        GameObject passObj = CreateButton(canvasObj.transform, "PassButton", "패스",
            new Vector2(1, 0), new Vector2(1, 0),
            new Vector2(-80, 80), new Vector2(180, 100),
            new Color(0.55f, 0.20f, 0.20f, 0.95f));

        // === Opponent profile placeholders + OpponentProfile 컴포넌트 ===
        OpponentProfile topProfile   = CreateOpponentProfile(canvasObj.transform, "TopProfile",   "상대 2",
            new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -120), new Vector2(280, 160));
        OpponentProfile leftProfile  = CreateOpponentProfile(canvasObj.transform, "LeftProfile",  "상대 3",
            new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(140, 0), new Vector2(220, 200));
        OpponentProfile rightProfile = CreateOpponentProfile(canvasObj.transform, "RightProfile", "상대 1",
            new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-140, 0), new Vector2(220, 200));

        // === FadeCanvas ===
        GameObject fadeObj = new GameObject("FadeCanvas");
        var fadeCvs = fadeObj.AddComponent<Canvas>();
        fadeCvs.renderMode = RenderMode.ScreenSpaceOverlay;
        fadeCvs.sortingOrder = 999;
        fadeObj.AddComponent<CanvasScaler>();
        GameObject fadeImage = new GameObject("FadeImage");
        fadeImage.transform.SetParent(fadeObj.transform, false);
        Image fi = fadeImage.AddComponent<Image>();
        fi.color = Color.black;
        RectTransform fiRT = (RectTransform)fadeImage.transform;
        fiRT.anchorMin = Vector2.zero;
        fiRT.anchorMax = Vector2.one;
        fiRT.sizeDelta = Vector2.zero;
        CanvasGroup fadeCG = fadeObj.AddComponent<CanvasGroup>();
        fadeCG.alpha = 0;
        fadeCG.blocksRaycasts = false;

        // === Managers ===
        GameObject fieldMgrObj = new GameObject("FieldManager");
        FieldManager fieldMgr = fieldMgrObj.AddComponent<FieldManager>();
        fieldMgrObj.AddComponent<FieldManagerSelfTest>();

        // FieldLineView에 FieldManager 참조 와이어링 (생성 시점에 없어서 지금 한다)
        WireFieldLine(fieldLineSpade,   fieldMgr);
        WireFieldLine(fieldLineHeart,   fieldMgr);
        WireFieldLine(fieldLineDiamond, fieldMgr);
        WireFieldLine(fieldLineClub,    fieldMgr);

        GameObject handMgrObj = new GameObject("HandManager");
        HandManager handMgr = handMgrObj.AddComponent<HandManager>();
        var hmSO = new SerializedObject(handMgr);
        hmSO.FindProperty("cardPrefab").objectReferenceValue   = cardPrefab;
        hmSO.FindProperty("handArea").objectReferenceValue     = handRT;
        hmSO.FindProperty("fanLayout").objectReferenceValue    = fan;
        hmSO.FindProperty("fieldManager").objectReferenceValue = fieldMgr;
        hmSO.ApplyModifiedProperties();

        // === CardGameManager (전체 게임 진행) ===
        var passLabel = passObj.GetComponentInChildren<TextMeshProUGUI>();
        GameObject gmObj = new GameObject("CardGameManager");
        CardGameManager gm = gmObj.AddComponent<CardGameManager>();
        var gmSO = new SerializedObject(gm);
        gmSO.FindProperty("fieldManager").objectReferenceValue    = fieldMgr;
        gmSO.FindProperty("handManager").objectReferenceValue     = handMgr;
        gmSO.FindProperty("passButton").objectReferenceValue      = passObj.GetComponent<Button>();
        gmSO.FindProperty("passButtonLabel").objectReferenceValue = passLabel;
        gmSO.FindProperty("rightProfile").objectReferenceValue    = rightProfile;
        gmSO.FindProperty("topProfile").objectReferenceValue      = topProfile;
        gmSO.FindProperty("leftProfile").objectReferenceValue     = leftProfile;
        gmSO.ApplyModifiedProperties();

        // === GameSceneManager (페이드 전환용) ===
        GameObject sceneMgrObj = new GameObject("GameSceneManager");
        GameSceneManager gsm = sceneMgrObj.AddComponent<GameSceneManager>();
        var gsmSO = new SerializedObject(gsm);
        gsmSO.FindProperty("fadeCanvasGroup").objectReferenceValue = fadeCG;
        gsmSO.ApplyModifiedProperties();

        EditorSceneManager.SaveScene(newScene, CardGameScenePath);
        Debug.Log("[CardGameSceneSetup] CardGame 씬 생성 완료: " + CardGameScenePath);
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private static GameObject CreatePanel(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size, Color bg)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var img = obj.AddComponent<Image>();
        img.color = bg;
        img.raycastTarget = false;
        RectTransform rt = (RectTransform)obj.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        return obj;
    }

    private static GameObject CreateText(Transform parent, string name,
        string text, float size, FontStyles style,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        RectTransform rt = (RectTransform)obj.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(800, 80);
        return obj;
    }

    private static GameObject CreateButton(Transform parent, string name, string label,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size, Color bgColor)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var img = obj.AddComponent<Image>();
        img.color = bgColor;
        var btn = obj.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor      = Color.white;
        colors.highlightedColor = new Color(1, 1, 1, 0.8f);
        colors.pressedColor     = new Color(0.7f, 0.7f, 0.7f);
        colors.disabledColor    = new Color(0.4f, 0.4f, 0.4f, 0.6f);
        btn.colors = colors;
        btn.targetGraphic = img;

        RectTransform rt = (RectTransform)obj.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        GameObject lbl = new GameObject("Label");
        lbl.transform.SetParent(obj.transform, false);
        var tmp = lbl.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 36;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        RectTransform lblRT = (RectTransform)lbl.transform;
        lblRT.anchorMin = Vector2.zero;
        lblRT.anchorMax = Vector2.one;
        lblRT.sizeDelta = Vector2.zero;
        return obj;
    }

    // ─────────────────────────────────────────────────────────────
    //  Field row (한 문양 줄)
    // ─────────────────────────────────────────────────────────────

    private const float FieldRowWidth  = 1080f;
    private const float FieldRowHeight = 95f;
    private const float SlotWidth      = 68f;
    private const float SlotHeight     = 95f;

    /// <summary>
    /// 한 문양 줄(13슬롯) 생성. FieldManager 참조는 생성 시점에 없을 수 있어
    /// 일단 슬롯·suit만 세팅하고, FieldManager 만든 후 WireFieldLine에서 연결한다.
    /// </summary>
    private static FieldLineView CreateFieldLine(Transform parent, Suit suit, float yOffset)
    {
        GameObject row = new GameObject($"FieldLine_{suit}");
        row.transform.SetParent(parent, false);
        RectTransform rt = row.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0, yOffset);
        rt.sizeDelta = new Vector2(FieldRowWidth, FieldRowHeight);

        FieldLineView view = row.AddComponent<FieldLineView>();
        var slots = new Image[13];

        // 13슬롯 균등 배치. 가운데(index 6 = 7) 기준 좌우로 펼침.
        float gap = (FieldRowWidth - 13 * SlotWidth) / 12f;
        float step = SlotWidth + gap;
        float firstX = -((SlotWidth + step * 12) - SlotWidth) * 0.5f;
        // firstX = -(step * 12)/2 와 같다. 슬롯 13개의 중앙이 row 중앙.

        for (int i = 0; i < 13; i++)
        {
            GameObject slot = new GameObject($"Slot_{i + 1}");
            slot.transform.SetParent(row.transform, false);
            Image img = slot.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.05f);
            img.raycastTarget = false;
            img.preserveAspect = true;

            RectTransform srt = (RectTransform)slot.transform;
            srt.anchorMin = new Vector2(0.5f, 0.5f);
            srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = new Vector2(firstX + i * step, 0f);
            srt.sizeDelta = new Vector2(SlotWidth, SlotHeight);
            slots[i] = img;
        }

        // suit와 slots 배열을 SerializedObject로 할당
        var so = new SerializedObject(view);
        so.FindProperty("suit").enumValueIndex = (int)suit;
        var slotsProp = so.FindProperty("slots");
        slotsProp.arraySize = 13;
        for (int i = 0; i < 13; i++)
        {
            slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
        }
        so.ApplyModifiedProperties();

        return view;
    }

    /// <summary>FieldLineView에 FieldManager 참조 늦은-바인딩.</summary>
    private static void WireFieldLine(FieldLineView view, FieldManager fm)
    {
        if (view == null) return;
        var so = new SerializedObject(view);
        so.FindProperty("fieldManager").objectReferenceValue = fm;
        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// 상대 프로필 박스 생성:
    ///   - 배경 패널
    ///   - 이름 TMP (상단)
    ///   - 카드 수 TMP (하단)
    ///   - OpponentProfile 컴포넌트 + 와이어링
    /// </summary>
    private static OpponentProfile CreateOpponentProfile(Transform parent, string name, string labelText,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size)
    {
        // 배경 패널
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0.22f, 0.24f, 0.30f, 0.9f);
        bg.raycastTarget = false;
        RectTransform rt = (RectTransform)panel.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        // 이름 텍스트 (위쪽)
        GameObject nameObj = new GameObject("NameText");
        nameObj.transform.SetParent(panel.transform, false);
        var nameTMP = nameObj.AddComponent<TextMeshProUGUI>();
        nameTMP.text = labelText;
        nameTMP.fontSize = 28;
        nameTMP.fontStyle = FontStyles.Bold;
        nameTMP.alignment = TextAlignmentOptions.Center;
        nameTMP.color = Color.white;
        nameTMP.raycastTarget = false;
        RectTransform nameRT = (RectTransform)nameObj.transform;
        nameRT.anchorMin = new Vector2(0, 0.5f);
        nameRT.anchorMax = new Vector2(1, 1);
        nameRT.offsetMin = new Vector2(8, 4);
        nameRT.offsetMax = new Vector2(-8, -4);

        // 카드 수 텍스트 (아래쪽)
        GameObject countObj = new GameObject("CardCountText");
        countObj.transform.SetParent(panel.transform, false);
        var countTMP = countObj.AddComponent<TextMeshProUGUI>();
        countTMP.text = "12장";
        countTMP.fontSize = 24;
        countTMP.alignment = TextAlignmentOptions.Center;
        countTMP.color = new Color(1f, 1f, 1f, 0.85f);
        countTMP.raycastTarget = false;
        RectTransform countRT = (RectTransform)countObj.transform;
        countRT.anchorMin = new Vector2(0, 0);
        countRT.anchorMax = new Vector2(1, 0.5f);
        countRT.offsetMin = new Vector2(8, 4);
        countRT.offsetMax = new Vector2(-8, -4);

        // OpponentProfile 컴포넌트 부착 + 와이어링
        OpponentProfile prof = panel.AddComponent<OpponentProfile>();
        var profSO = new SerializedObject(prof);
        profSO.FindProperty("nameText").objectReferenceValue        = nameTMP;
        profSO.FindProperty("cardCountText").objectReferenceValue   = countTMP;
        profSO.FindProperty("backgroundImage").objectReferenceValue = bg;
        profSO.ApplyModifiedProperties();

        return prof;
    }
}
