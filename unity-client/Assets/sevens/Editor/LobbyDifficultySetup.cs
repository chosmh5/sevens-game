using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>
/// 기존 Lobby 씬에 'AI 난이도 선택 팝업'을 생성하고 LobbyUI 필드를 자동 연결하는 도구.
///
/// 흐름: 게임하기 → 난이도 팝업(쉬움/보통/어려움) → 시작 → 카드게임.
///
/// 사용법: 메뉴 Tools ▸ Add Difficulty Popup to Lobby (난이도 팝업 추가) 실행.
/// 여러 번 실행해도 기존 팝업을 지우고 다시 만든다.
/// </summary>
public static class LobbyDifficultySetup
{
    private const string LobbyScenePath = "Assets/sevens/Scenes/Lobby.unity";
    private const string PanelName      = "DifficultyPanel";

    // Easy / Normal / Hard 색상
    private static readonly Color[] DiffColors =
    {
        new Color(0.30f, 0.70f, 0.40f, 0.95f), // Easy  - 초록
        new Color(0.85f, 0.65f, 0.25f, 0.95f), // Normal- 주황
        new Color(0.80f, 0.35f, 0.35f, 0.95f), // Hard  - 빨강
    };
    private static readonly string[] DiffNames = { "쉬움", "보통", "어려움" };
    private static readonly string[] DiffSub   = { "Easy", "Normal", "Hard" };

    [MenuItem("Tools/Add Difficulty Popup to Lobby (난이도 팝업 추가)")]
    public static void AddDifficultyPopup()
    {
        var scene = EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Single);

        LobbyUI lobbyUI = Object.FindObjectOfType<LobbyUI>();
        if (lobbyUI == null)
        {
            EditorUtility.DisplayDialog("실패",
                "Lobby 씬에서 LobbyUI를 찾지 못했습니다.\n먼저 Tools ▸ Setup Lobby Scene 을 실행하세요.", "확인");
            return;
        }

        Transform canvas = lobbyUI.transform; // LobbyUI는 LobbyCanvas에 붙어 있음

        // 기존 팝업 제거(재실행 대비)
        Transform existing = canvas.Find(PanelName);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        // ── 팝업 루트(전체 화면 딤 + 레이캐스트 차단) ──
        GameObject panel = new GameObject(PanelName, typeof(RectTransform));
        panel.transform.SetParent(canvas, false);
        Image dim = panel.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.78f);
        StretchFull(panel.GetComponent<RectTransform>());

        // ── 중앙 카드 ──
        GameObject card = new GameObject("Card", typeof(RectTransform));
        card.transform.SetParent(panel.transform, false);
        Image cardBg = card.AddComponent<Image>();
        cardBg.color = new Color(0.12f, 0.13f, 0.17f, 0.98f);
        RectTransform cardRT = card.GetComponent<RectTransform>();
        cardRT.anchorMin = new Vector2(0.5f, 0.5f);
        cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        cardRT.pivot     = new Vector2(0.5f, 0.5f);
        cardRT.sizeDelta = new Vector2(900, 560);

        // 제목
        CreateText(card.transform, "Title", "난이도 선택", 48, FontStyles.Bold,
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f),
            new Vector2(0, -40), new Vector2(0, 80), Color.white);

        // ── 난이도 버튼 3개 (가로 배치) ──
        GameObject row = new GameObject("DiffRow", typeof(RectTransform));
        row.transform.SetParent(card.transform, false);
        RectTransform rowRT = row.GetComponent<RectTransform>();
        rowRT.anchorMin = new Vector2(0.5f, 0.5f);
        rowRT.anchorMax = new Vector2(0.5f, 0.5f);
        rowRT.pivot     = new Vector2(0.5f, 0.5f);
        rowRT.anchoredPosition = new Vector2(0, 40);
        rowRT.sizeDelta = new Vector2(820, 240);
        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 30;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        var diffButtons    = new Button[3];
        var diffHighlights = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            CreateDifficultyButton(row.transform, i, out diffButtons[i], out diffHighlights[i]);
        }

        // ── 시작 / 취소 버튼 ──
        Button startBtn  = CreateActionButton(card.transform, "StartButton", "시작",
            new Color(0.20f, 0.55f, 0.85f, 0.97f),
            new Vector2(0.5f, 0f), new Vector2(0.30f, 0.10f), new Vector2(-150, 50), new Vector2(260, 80));
        Button cancelBtn = CreateActionButton(card.transform, "CancelButton", "취소",
            new Color(0.45f, 0.45f, 0.50f, 0.95f),
            new Vector2(0.5f, 0f), new Vector2(0.70f, 0.10f), new Vector2(150, 50), new Vector2(260, 80));

        panel.SetActive(false);

        // ── LobbyUI 필드 연결 ──
        SerializedObject so = new SerializedObject(lobbyUI);
        so.FindProperty("difficultyPanel").objectReferenceValue       = panel;
        so.FindProperty("startGameButton").objectReferenceValue       = startBtn;
        so.FindProperty("difficultyCancelButton").objectReferenceValue = cancelBtn;
        SetArray(so.FindProperty("difficultyButtons"), diffButtons);
        SetArray(so.FindProperty("difficultyHighlights"), diffHighlights);
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(lobbyUI);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("[Lobby] 난이도 팝업 생성 + LobbyUI 연결 완료.");
        EditorUtility.DisplayDialog("완료",
            "난이도 팝업을 추가하고 LobbyUI에 연결했습니다.\n'게임하기' → 난이도 선택 → '시작' 흐름으로 동작합니다.", "확인");
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────

    private static void CreateDifficultyButton(Transform parent, int index, out Button button, out Image highlight)
    {
        GameObject obj = new GameObject($"Diff_{DiffSub[index]}", typeof(RectTransform));
        obj.transform.SetParent(parent, false);

        // 배경 Image = 선택 강조용(highlight). 초기색은 LobbyUI가 Refresh 시 덮어씀.
        highlight = obj.AddComponent<Image>();
        highlight.color = DiffColors[index];

        button = obj.AddComponent<Button>();
        ColorBlock cb = button.colors;
        cb.highlightedColor = new Color(1f, 1f, 1f, 1f);
        button.colors = cb;

        // 메인 라벨(쉬움/보통/어려움)
        CreateText(obj.transform, "Name", DiffNames[index], 40, FontStyles.Bold,
            new Vector2(0, 0.45f), new Vector2(1, 1), new Vector2(0.5f, 1f),
            Vector2.zero, Vector2.zero, Color.white, stretch: true);

        // 보조 라벨(영문)
        GameObject sub = CreateText(obj.transform, "Sub", DiffSub[index], 24, FontStyles.Normal,
            new Vector2(0, 0), new Vector2(1, 0.45f), new Vector2(0.5f, 0f),
            Vector2.zero, Vector2.zero, new Color(1, 1, 1, 0.8f), stretch: true);
        sub.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Top;
    }

    private static Button CreateActionButton(Transform parent, string name, string label, Color color,
        Vector2 anchor, Vector2 anchorPivotPos, Vector2 anchoredPos, Vector2 size)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        Image img = obj.AddComponent<Image>();
        img.color = color;
        Button btn = obj.AddComponent<Button>();

        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = anchorPivotPos;
        rt.anchorMax = anchorPivotPos;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        GameObject t = new GameObject("Label", typeof(RectTransform));
        t.transform.SetParent(obj.transform, false);
        TextMeshProUGUI tmp = t.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 32;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        StretchFull(t.GetComponent<RectTransform>());
        return btn;
    }

    private static GameObject CreateText(Transform parent, string name, string text, int fontSize,
        FontStyles style, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPos, Vector2 sizeDelta, Color color, bool stretch = false)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;

        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot     = pivot;
        if (stretch)
        {
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
        else
        {
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
        }
        return obj;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void SetArray(SerializedProperty prop, Object[] values)
    {
        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}
