using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

/// <summary>
/// 모바일 UI를 자동으로 생성하는 에디터 도구
/// - Canvas + 조이스틱 + 인터랙션 버튼 + 대화 UI
/// </summary>
public class MobileUISetup : EditorWindow
{
    [MenuItem("Tools/Setup Mobile Village UI")]
    public static void SetupUI()
    {
        // 기존 캔버스 확인
        Canvas existingCanvas = Object.FindAnyObjectByType<Canvas>();
        GameObject canvasObj;

        if (existingCanvas != null)
        {
            canvasObj = existingCanvas.gameObject;
        }
        else
        {
            canvasObj = new GameObject("VillageCanvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObj.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            canvasObj.AddComponent<GraphicRaycaster>();
        }

        // EventSystem 확인
        if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // === 조이스틱 ===
        CreateJoystick(canvasObj.transform);

        // === 인터랙션 버튼 ===
        CreateInteractButton(canvasObj.transform);

        // === 대화 UI ===
        CreateDialogueUI(canvasObj.transform);

        Debug.Log("[MobileUI] 모바일 마을 UI 생성 완료!");
    }

    private static void CreateJoystick(Transform parent)
    {
        // 조이스틱 루트
        GameObject joystickRoot = new GameObject("Joystick");
        joystickRoot.transform.SetParent(parent, false);
        RectTransform rootRT = joystickRoot.AddComponent<RectTransform>();
        rootRT.anchorMin = new Vector2(0, 0);
        rootRT.anchorMax = new Vector2(0, 0);
        rootRT.pivot = new Vector2(0.5f, 0.5f);
        rootRT.anchoredPosition = new Vector2(200, 200);

        // 배경
        GameObject bg = new GameObject("Background");
        bg.transform.SetParent(joystickRoot.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(1, 1, 1, 0.3f);
        RectTransform bgRT = bg.GetComponent<RectTransform>();
        bgRT.sizeDelta = new Vector2(250, 250);

        // 핸들
        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(bg.transform, false);
        Image handleImg = handle.AddComponent<Image>();
        handleImg.color = new Color(1, 1, 1, 0.7f);
        RectTransform handleRT = handle.GetComponent<RectTransform>();
        handleRT.sizeDelta = new Vector2(80, 80);

        // VirtualJoystick 컴포넌트
        VirtualJoystick joystick = joystickRoot.AddComponent<VirtualJoystick>();
        SerializedObject so = new SerializedObject(joystick);
        so.FindProperty("background").objectReferenceValue = bgRT;
        so.FindProperty("handle").objectReferenceValue = handleRT;
        so.ApplyModifiedProperties();
    }

    private static void CreateInteractButton(Transform parent)
    {
        // 버튼 루트
        GameObject btnRoot = new GameObject("InteractButton");
        btnRoot.transform.SetParent(parent, false);
        RectTransform btnRootRT = btnRoot.AddComponent<RectTransform>();
        btnRootRT.anchorMin = new Vector2(1, 0);
        btnRootRT.anchorMax = new Vector2(1, 0);
        btnRootRT.pivot = new Vector2(0.5f, 0.5f);
        btnRootRT.anchoredPosition = new Vector2(-180, 200);
        btnRootRT.sizeDelta = new Vector2(120, 120);

        // 버튼
        Image btnImg = btnRoot.AddComponent<Image>();
        btnImg.color = new Color(0.3f, 0.8f, 0.4f, 0.8f);
        Button btn = btnRoot.AddComponent<Button>();

        // 텍스트
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnRoot.transform, false);
        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = "말걸기";
        tmp.fontSize = 28;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        RectTransform textRT = textObj.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.sizeDelta = Vector2.zero;

        // MobileInteractButton 컴포넌트
        MobileInteractButton mib = btnRoot.AddComponent<MobileInteractButton>();
        SerializedObject so = new SerializedObject(mib);
        so.FindProperty("button").objectReferenceValue = btn;
        so.FindProperty("buttonRoot").objectReferenceValue = btnRoot;
        so.ApplyModifiedProperties();
    }

    private static void CreateDialogueUI(Transform parent)
    {
        // 대화 패널 (하단)
        GameObject panel = new GameObject("DialoguePanel");
        panel.transform.SetParent(parent, false);
        Image panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0, 0, 0, 0.75f);
        RectTransform panelRT = panel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.05f, 0.02f);
        panelRT.anchorMax = new Vector2(0.95f, 0.25f);
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;

        // 이름 텍스트
        GameObject nameObj = new GameObject("NameText");
        nameObj.transform.SetParent(panel.transform, false);
        TextMeshProUGUI nameTmp = nameObj.AddComponent<TextMeshProUGUI>();
        nameTmp.text = "NPC 이름";
        nameTmp.fontSize = 32;
        nameTmp.fontStyle = FontStyles.Bold;
        nameTmp.color = new Color(0.4f, 0.9f, 1f);
        RectTransform nameRT = nameObj.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0, 0.65f);
        nameRT.anchorMax = new Vector2(1, 1);
        nameRT.offsetMin = new Vector2(25, 0);
        nameRT.offsetMax = new Vector2(-25, -10);

        // 대사 텍스트
        GameObject dialogObj = new GameObject("DialogueText");
        dialogObj.transform.SetParent(panel.transform, false);
        TextMeshProUGUI dialogTmp = dialogObj.AddComponent<TextMeshProUGUI>();
        dialogTmp.text = "";
        dialogTmp.fontSize = 26;
        dialogTmp.color = Color.white;
        RectTransform dialogRT = dialogObj.GetComponent<RectTransform>();
        dialogRT.anchorMin = new Vector2(0, 0);
        dialogRT.anchorMax = new Vector2(1, 0.65f);
        dialogRT.offsetMin = new Vector2(25, 15);
        dialogRT.offsetMax = new Vector2(-25, 0);

        // "탭하여 계속" 안내
        GameObject continueObj = new GameObject("ContinuePrompt");
        continueObj.transform.SetParent(panel.transform, false);
        TextMeshProUGUI continueTmp = continueObj.AddComponent<TextMeshProUGUI>();
        continueTmp.text = "탭하여 계속 ▼";
        continueTmp.fontSize = 20;
        continueTmp.alignment = TextAlignmentOptions.BottomRight;
        continueTmp.color = new Color(1, 1, 1, 0.5f);
        RectTransform contRT = continueObj.GetComponent<RectTransform>();
        contRT.anchorMin = new Vector2(0.6f, 0);
        contRT.anchorMax = new Vector2(1, 0.3f);
        contRT.offsetMin = new Vector2(0, 5);
        contRT.offsetMax = new Vector2(-15, 0);

        // DialogueUI 컴포넌트
        DialogueUI dialogueUI = panel.AddComponent<DialogueUI>();
        SerializedObject so = new SerializedObject(dialogueUI);
        so.FindProperty("dialoguePanel").objectReferenceValue = panel;
        so.FindProperty("nameText").objectReferenceValue = nameTmp;
        so.FindProperty("dialogueText").objectReferenceValue = dialogTmp;
        so.FindProperty("continuePrompt").objectReferenceValue = continueObj;
        so.ApplyModifiedProperties();
    }
}
