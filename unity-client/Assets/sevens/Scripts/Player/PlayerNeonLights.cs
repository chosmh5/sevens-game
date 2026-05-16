using UnityEngine;

/// <summary>
/// 플레이어 주변에 네온 컬러 포인트 라이트를 배치하여
/// 캐릭터가 항상 네온 분위기 속에 있도록 함
/// - 플레이어 자식으로 붙어서 자동 추적
/// - 색상 펄스 + 부드러운 강도 변화
/// </summary>
public class PlayerNeonLights : MonoBehaviour
{
    [Header("Neon Light Settings")]
    [Tooltip("네온 라이트 색상 1 (파랑 계열)")]
    [SerializeField] private Color neonColor1 = new Color(0.3f, 0.5f, 1f, 1f);
    [Tooltip("네온 라이트 색상 2 (핑크/레드 계열)")]
    [SerializeField] private Color neonColor2 = new Color(1f, 0.3f, 0.6f, 1f);

    [Header("Intensity")]
    [SerializeField] private float baseIntensity = 0.6f;
    [SerializeField] private float pulseAmount = 0.15f;
    [SerializeField] private float pulseSpeed = 1.2f;

    [Header("Light Properties")]
    [SerializeField] private float lightRange = 4f;
    [SerializeField] private float heightOffset = 1.0f;
    [SerializeField] private float spreadRadius = 1.2f;

    private Light neonLight1;
    private Light neonLight2;

    private void Start()
    {
        CreateNeonLights();
    }

    private void CreateNeonLights()
    {
        // 라이트 1: 캐릭터 왼쪽 위
        GameObject light1Obj = new GameObject("PlayerNeon_Blue");
        light1Obj.transform.SetParent(transform);
        light1Obj.transform.localPosition = new Vector3(-spreadRadius, heightOffset, 0.3f);
        neonLight1 = light1Obj.AddComponent<Light>();
        neonLight1.type = LightType.Point;
        neonLight1.color = neonColor1;
        neonLight1.intensity = baseIntensity;
        neonLight1.range = lightRange;
        neonLight1.shadows = LightShadows.None;

        // 라이트 2: 캐릭터 오른쪽 위
        GameObject light2Obj = new GameObject("PlayerNeon_Pink");
        light2Obj.transform.SetParent(transform);
        light2Obj.transform.localPosition = new Vector3(spreadRadius, heightOffset, -0.3f);
        neonLight2 = light2Obj.AddComponent<Light>();
        neonLight2.type = LightType.Point;
        neonLight2.color = neonColor2;
        neonLight2.intensity = baseIntensity;
        neonLight2.range = lightRange;
        neonLight2.shadows = LightShadows.None;
    }

    private void Update()
    {
        if (neonLight1 == null || neonLight2 == null) return;

        // 부드러운 펄스 (서로 반대 위상)
        float pulse1 = baseIntensity + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
        float pulse2 = baseIntensity + Mathf.Sin(Time.time * pulseSpeed + Mathf.PI) * pulseAmount;

        neonLight1.intensity = pulse1;
        neonLight2.intensity = pulse2;

        // 색상도 살짝 교차 블렌드
        float blend = Mathf.Sin(Time.time * pulseSpeed * 0.3f) * 0.5f + 0.5f;
        neonLight1.color = Color.Lerp(neonColor1, neonColor2, blend * 0.2f);
        neonLight2.color = Color.Lerp(neonColor2, neonColor1, blend * 0.2f);
    }

    // === Inspector에서 색상/강도 실시간 조절 가능 ===
    public void SetColors(Color color1, Color color2)
    {
        neonColor1 = color1;
        neonColor2 = color2;
    }

    public void SetIntensity(float intensity)
    {
        baseIntensity = intensity;
    }
}
