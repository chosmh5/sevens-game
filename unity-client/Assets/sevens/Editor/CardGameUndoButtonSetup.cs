using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>
/// 기존 CardGame 씬에 '취소(되돌리기)' 버튼을 추가하고 CardGameManager.undoButton에 연결한다.
/// 패스/턴 종료 버튼 옆에 배치된다. 이번 턴에 낸 카드를 한 장씩 되돌릴 수 있다.
///
/// 사용법: 메뉴 Tools ▸ Sevens ▸ Add Undo Button to CardGame 실행.
/// 여러 번 실행해도 기존 취소 버튼만 지우고 다시 만든다.
/// </summary>
public static class CardGameUndoButtonSetup
{
    private const string ScenePath  = "Assets/sevens/Scenes/CardGame.unity";
    private const string ButtonName = "UndoButton";

    [MenuItem("Tools/Sevens/Add Undo Button to CardGame", priority = 13)]
    public static void AddUndoButton()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        CardGameManager gm = Object.FindObjectOfType<CardGameManager>();
        if (gm == null)
        {
            EditorUtility.DisplayDialog("실패",
                "CardGame 씬에서 CardGameManager를 찾지 못했습니다.\n먼저 CardGame 씬을 셋업하세요.", "확인");
            return;
        }

        // 패스 버튼을 찾아 같은 부모(Canvas)에 형제로 배치.
        var passObj = FindInScene("PassButton");
        Transform parent = passObj != null ? passObj.transform.parent : FindCanvas();
        if (parent == null)
        {
            EditorUtility.DisplayDialog("실패", "버튼을 붙일 Canvas를 찾지 못했습니다.", "확인");
            return;
        }

        // 기존 취소 버튼 제거(재실행 대비)
        var existing = parent.Find(ButtonName);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        // 패스 버튼 왼쪽에 배치(패스: anchoredPos (-80,80), size (180,100)).
        Button undo = CreateButton(parent, ButtonName, "취소",
            new Vector2(1, 0), new Vector2(1, 0),
            new Vector2(-280, 80), new Vector2(180, 100),
            new Color(0.30f, 0.34f, 0.42f, 0.95f));
        undo.interactable = false; // 시작 시 비활성(낸 카드 있을 때만 활성)

        SerializedObject so = new SerializedObject(gm);
        var prop = so.FindProperty("undoButton");
        if (prop == null)
        {
            EditorUtility.DisplayDialog("실패",
                "CardGameManager에 undoButton 필드가 없습니다. 스크립트가 최신인지 확인하세요.", "확인");
            return;
        }
        prop.objectReferenceValue = undo;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(gm);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("[CardGame] 취소 버튼 추가 + CardGameManager.undoButton 연결 완료.");
        EditorUtility.DisplayDialog("완료",
            "CardGame 씬에 '취소' 버튼을 추가하고 연결했습니다.\n이번 턴에 낸 카드를 한 장씩 되돌릴 수 있습니다.", "확인");
    }

    private static GameObject FindInScene(string name)
    {
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
            if (t.name == name) return t.gameObject;
        return null;
    }

    private static Transform FindCanvas()
    {
        var c = Object.FindObjectOfType<Canvas>();
        return c != null ? c.transform : null;
    }

    private static Button CreateButton(Transform parent, string name, string label,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size, Color bgColor)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        var img = obj.AddComponent<Image>();
        img.color = bgColor;
        var btn = obj.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor      = Color.white;
        colors.highlightedColor = new Color(1, 1, 1, 0.8f);
        colors.pressedColor     = new Color(0.7f, 0.7f, 0.7f);
        colors.disabledColor    = new Color(0.4f, 0.4f, 0.4f, 0.5f);
        btn.colors = colors;
        btn.targetGraphic = img;

        RectTransform rt = (RectTransform)obj.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        GameObject lbl = new GameObject("Label", typeof(RectTransform));
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
        return btn;
    }
}
