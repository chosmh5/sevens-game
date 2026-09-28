using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 플레이어가 이 콜라이더 안에 들어오면 게임 시작 프롬프트를 표시합니다.
/// Collider 컴포넌트를 Is Trigger로 설정해야 합니다.
/// 플레이어 오브젝트에는 "Player" 태그를 붙여야 합니다.
/// </summary>
public class GameZoneTrigger : MonoBehaviour
{
    [Header("게임 설정")]
    [SerializeField] string  gameSceneName  = "GameScene";
    [SerializeField] KeyCode confirmKey     = KeyCode.Return;
    [SerializeField] bool    loadScene      = true;

    [Header("표시 텍스트")]
    [SerializeField] string zoneName        = "게임 존";
    [SerializeField] string promptMessage   = "세븐스 게임을 시작하시겠습니까?";

    [Header("시각 효과")]
    [SerializeField] Color zoneColor        = new Color(0.3f, 0.7f, 1f, 0.25f);

    bool _playerInside;
    bool _loading;

    // UI
    GameObject _promptRoot;
    TMP_Text   _titleLabel;
    TMP_Text   _bodyLabel;

    // ══════════════════════════════════════════════════════════════════

    void Start()
    {
        BuildPromptUI();
        EnsureVisualIndicator();
    }

    void Update()
    {
        if (!_playerInside || _loading) return;

        if (Input.GetKeyDown(confirmKey))
            StartCoroutine(StartGame());
        if (Input.GetKeyDown(KeyCode.Escape))
            HidePrompt();
    }

    // ══════════════════════════════════════════════════════════════════
    //  트리거
    // ══════════════════════════════════════════════════════════════════

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInside = true;
        ShowPrompt();
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInside = false;
        HidePrompt();
    }

    // ══════════════════════════════════════════════════════════════════
    //  게임 시작
    // ══════════════════════════════════════════════════════════════════

    IEnumerator StartGame()
    {
        _loading = true;
        _bodyLabel.text = "불러오는 중...";

        yield return new WaitForSeconds(0.5f);

        if (loadScene && !string.IsNullOrEmpty(gameSceneName))
            SceneManager.LoadScene(gameSceneName);
        else
            gameObject.SendMessage("OnGameStart", SendMessageOptions.DontRequireReceiver);
    }

    // ══════════════════════════════════════════════════════════════════
    //  프롬프트 UI
    // ══════════════════════════════════════════════════════════════════

    void ShowPrompt()
    {
        _loading = false;
        _titleLabel.text = zoneName;
        _bodyLabel.text  = promptMessage;
        _promptRoot.SetActive(true);
    }

    void HidePrompt()
    {
        _promptRoot.SetActive(false);
    }

    void BuildPromptUI()
    {
        // Canvas
        var go = new GameObject("ZonePromptCanvas");
        var c  = go.AddComponent<Canvas>();
        c.renderMode   = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 20;
        var cs = go.AddComponent<CanvasScaler>();
        cs.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920f, 1080f);
        go.AddComponent<GraphicRaycaster>();
        _promptRoot = go;

        // 패널
        var panel = new GameObject("Panel");
        panel.transform.SetParent(go.transform, false);
        panel.AddComponent<Image>().color = new Color(0.02f, 0.05f, 0.15f, 0.9f);
        var pRT             = panel.GetComponent<RectTransform>();
        pRT.anchorMin       = new Vector2(0.5f, 0.5f);
        pRT.anchorMax       = new Vector2(0.5f, 0.5f);
        pRT.sizeDelta       = new Vector2(480f, 180f);
        pRT.anchoredPosition = Vector2.zero;

        // 테두리
        var border = new GameObject("Border");
        border.transform.SetParent(panel.transform, false);
        var bImg = border.AddComponent<Image>();
        bImg.color = new Color(0.3f, 0.6f, 1f, 0.6f);
        var bRT        = border.GetComponent<RectTransform>();
        bRT.anchorMin  = Vector2.zero;
        bRT.anchorMax  = Vector2.one;
        bRT.offsetMin  = new Vector2(-2f, -2f);
        bRT.offsetMax  = new Vector2(2f, 2f);
        border.transform.SetAsFirstSibling();

        // 타이틀
        var titleGO  = new GameObject("Title");
        titleGO.transform.SetParent(panel.transform, false);
        _titleLabel  = titleGO.AddComponent<TextMeshProUGUI>();
        _titleLabel.text      = zoneName;
        _titleLabel.fontSize  = 18f;
        _titleLabel.color     = new Color(0.4f, 0.8f, 1f);
        _titleLabel.fontStyle = FontStyles.Bold;
        _titleLabel.alignment = TextAlignmentOptions.Center;
        var tRT          = titleGO.GetComponent<RectTransform>();
        tRT.anchorMin    = new Vector2(0f, 1f);
        tRT.anchorMax    = new Vector2(1f, 1f);
        tRT.pivot        = new Vector2(0.5f, 1f);
        tRT.offsetMin    = new Vector2(12f, -46f);
        tRT.offsetMax    = new Vector2(-12f, -10f);

        // 본문
        var bodyGO   = new GameObject("Body");
        bodyGO.transform.SetParent(panel.transform, false);
        _bodyLabel   = bodyGO.AddComponent<TextMeshProUGUI>();
        _bodyLabel.fontSize          = 14f;
        _bodyLabel.color             = Color.white;
        _bodyLabel.alignment         = TextAlignmentOptions.Center;
        _bodyLabel.enableWordWrapping = true;
        var bBodyRT      = bodyGO.GetComponent<RectTransform>();
        bBodyRT.anchorMin = Vector2.zero;
        bBodyRT.anchorMax = Vector2.one;
        bBodyRT.offsetMin = new Vector2(16f, 60f);   // 하단 버튼 자리 확보
        bBodyRT.offsetMax = new Vector2(-16f, -50f);

        // 탭 버튼(모바일) — 시작 / 취소. 키보드 Enter/Esc 도 그대로 동작.
        MakeZoneButton(panel.transform, "시작", new Color(0.2f, 0.5f, 0.85f, 1f),
                       new Vector2(-70f, 14f), () => { if (!_loading) StartCoroutine(StartGame()); });
        MakeZoneButton(panel.transform, "취소", new Color(0.3f, 0.3f, 0.38f, 1f),
                       new Vector2(70f, 14f), HidePrompt);

        _promptRoot.SetActive(false);
    }

    void MakeZoneButton(Transform parent, string text, Color bg, Vector2 anchoredPos,
                        UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject($"Btn_{text}");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = bg;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(120f, 40f);
        rt.anchoredPosition = anchoredPos;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        var lblGO = new GameObject("Label");
        lblGO.transform.SetParent(go.transform, false);
        var tmp = lblGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = 16f;
        tmp.color     = Color.white;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        var lrt = lblGO.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
    }

    // ══════════════════════════════════════════════════════════════════
    //  시각 인디케이터 (씬에서 존 위치 확인용 반투명 박스)
    // ══════════════════════════════════════════════════════════════════

    void EnsureVisualIndicator()
    {
        // Collider가 없으면 기본 BoxCollider 추가
        if (GetComponent<Collider>() == null)
        {
            var box     = gameObject.AddComponent<BoxCollider>();
            box.size    = new Vector3(4f, 3f, 4f);
            box.isTrigger = true;
        }
        else
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = zoneColor;
        var col = GetComponent<BoxCollider>();
        if (col != null)
            Gizmos.DrawCube(transform.position + col.center, col.size);
        else
            Gizmos.DrawCube(transform.position, new Vector3(4f, 3f, 4f));

        Gizmos.color = new Color(zoneColor.r, zoneColor.g, zoneColor.b, 1f);
        var col2 = GetComponent<BoxCollider>();
        if (col2 != null)
            Gizmos.DrawWireCube(transform.position + col2.center, col2.size);
        else
            Gizmos.DrawWireCube(transform.position, new Vector3(4f, 3f, 4f));

        UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, zoneName);
    }
#endif
}
