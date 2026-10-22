using UnityEngine;
using VRM;

/// <summary>
/// 미연시처럼 서 있는 캐릭터의 대기 동작(애니메이션 파일 없이 코드로).
/// - T포즈 팔을 내리고 팔꿈치를 살짝 굽힌다. 아래팔을 안쪽으로 살짝 돌려 손등이 바깥을 보게.
/// - 손가락: 평소엔 쭉 펴서 붙인 손(IdleShape), 동작 중엔 동작별 모양(VnHand).
/// - 숨쉬기(가슴), 몸 흔들림, 고개 갸웃.
/// - 눈 깜빡임, 말할 때 입 움직임, 짧은 표정(기쁨/화남 등), 찔렀을 때 살짝 튀기.
/// - 시선은 카메라(VRMLookAtHead.Target).
///
/// VRM 은 T포즈가 기준이라 '기준 자세에서 몇 도'로 계산한다. 머리카락 흔들림(SpringBone)이
/// 이 자세를 따라가도록 SpringBone(기본 순서 0)보다 먼저 LateUpdate 를 돈다.
/// </summary>
[DefaultExecutionOrder(-100)]
public class VnCharacterPose : MonoBehaviour
{
    [Header("자세")]
    public float armDown   = 76f;   // T포즈에서 팔을 내리는 각도
    public float elbowBend = 18f;
    [Tooltip("아래팔을 안쪽으로 돌리는 각도 — 손등이 바깥, 엄지가 앞을 보게")]
    public float forearmTwist = 25f;
    [Tooltip("손목을 손바닥 쪽으로 살짝 꺾는 각도")]
    public float wristFlex = 3f;
    public float headTilt  = 4f;

    [Header("손")]
    [Tooltip("평소 손가락 굽힘. 0 = 쭉 편 손, 1 = 힘 뺀 손. 제스처가 잠깐 바꾼다.")]
    public float idleCurl = 0.2f;
    [Tooltip("평소 엄지 굽힘(0 = 편 엄지)")]
    public float idleThumb = 0.45f;
    [Tooltip("평소 손가락 벌림(음수 = 붙임)")]
    public float idleSpread = -0.3f;

    /// <summary>평소(서 있을 때) 손 모양 — 손가락을 쭉 펴서 붙인 손.</summary>
    public VnHand.Shape IdleShape => new VnHand.Shape(idleCurl, idleThumb, idleSpread);

    [Header("움직임")]
    public float breathSpeed = 1.5f;
    public float breathAngle = 1.6f;
    public float swaySpeed   = 0.42f;
    public float swayAngle   = 1.4f;

    [Header("시선")]
    public Transform lookTarget;

    private Animator _anim;
    private VRMBlendShapeProxy _proxy;
    private Transform _spine, _chest, _upperChest, _neck, _head, _lUpper, _rUpper, _lLower, _rLower, _lHand, _rHand;
    private Quaternion _rSpine, _rChest, _rUpperChest, _rNeck, _rHead, _rLUpper, _rRUpper, _rLLower, _rRLower, _rLHand, _rRHand;
    private Vector3 _fwdLocal, _rightLocal;   // 캐릭터 기준 앞/오른쪽(루트 로컬)
    private Vector3 _basePos;
    private bool _ready;
    private VnHand _leftHand, _rightHand;

    private float _nextBlink, _blinkT = -1f;
    private float _talkUntil;
    private BlendShapePreset _expr = BlendShapePreset.Neutral;
    private float _exprWeight, _exprTarget, _exprUntil;
    private float _hopT = -1f;
    private float _phase;

    /// <summary>캐릭터가 보는 방향(월드). 카메라 배치에 쓴다.</summary>
    public Vector3 Forward => transform.TransformDirection(_fwdLocal);
    public Transform Head => _head;
    public Transform Hips => _anim != null ? _anim.GetBoneTransform(HumanBodyBones.Hips) : transform;
    public bool Ready => _ready;
    /// <summary>side: +1 오른손, -1 왼손.</summary>
    public VnHand Hand(int side) => side > 0 ? _rightHand : _leftHand;

    private void Awake() => Init();

    public void Init()
    {
        if (_ready) return;
        _anim = GetComponentInChildren<Animator>();
        if (_anim == null || !_anim.isHuman)
        {
            Debug.LogWarning("[VnCharacterPose] 휴머노이드 Animator 가 없어 대기 동작을 끕니다.");
            enabled = false;
            return;
        }
        _spine  = _anim.GetBoneTransform(HumanBodyBones.Spine);
        _chest  = _anim.GetBoneTransform(HumanBodyBones.Chest);
        _upperChest = _anim.GetBoneTransform(HumanBodyBones.UpperChest);
        _lHand  = _anim.GetBoneTransform(HumanBodyBones.LeftHand);
        _rHand  = _anim.GetBoneTransform(HumanBodyBones.RightHand);
        _neck   = _anim.GetBoneTransform(HumanBodyBones.Neck);
        _head   = _anim.GetBoneTransform(HumanBodyBones.Head);
        _lUpper = _anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        _rUpper = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        _lLower = _anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        _rLower = _anim.GetBoneTransform(HumanBodyBones.RightLowerArm);

        _rSpine  = Rest(_spine);  _rChest  = Rest(_chest);  _rNeck = Rest(_neck); _rHead = Rest(_head);
        _rLUpper = Rest(_lUpper); _rRUpper = Rest(_rUpper); _rLLower = Rest(_lLower); _rRLower = Rest(_rLower);
        _rUpperChest = Rest(_upperChest); _rLHand = Rest(_lHand); _rRHand = Rest(_rHand);

        // 기준(T포즈)에서 캐릭터의 오른쪽/앞 방향을 뼈 위치로 구한다(모델마다 +Z/-Z 가 달라도 맞게).
        Vector3 right = (_rUpper != null && _lUpper != null)
            ? (_rUpper.position - _lUpper.position)
            : transform.right;
        right.y = 0f;
        if (right.sqrMagnitude < 1e-4f) right = transform.right;
        right.Normalize();
        Vector3 fwd = Vector3.Cross(right, Vector3.up).normalized;
        _rightLocal = transform.InverseTransformDirection(right);
        _fwdLocal   = transform.InverseTransformDirection(fwd);

        _leftHand  = VnHand.Create(_anim, -1);
        _rightHand = VnHand.Create(_anim, +1);

        _proxy = GetComponentInChildren<VRMBlendShapeProxy>();
        var look = GetComponentInChildren<VRMLookAtHead>();
        if (look != null && lookTarget != null) look.Target = lookTarget;

        _basePos = transform.localPosition;
        _phase = Random.value * 10f;
        _nextBlink = Time.time + Random.Range(1.5f, 4f);
        _ready = true;
    }

    public void SetLookTarget(Transform t)
    {
        lookTarget = t;
        var look = GetComponentInChildren<VRMLookAtHead>();
        if (look != null) look.Target = t;
    }

    private static Quaternion Rest(Transform t) => t != null ? t.localRotation : Quaternion.identity;

    // ── 외부에서 부르는 것 ───────────────────────────────────────────

    /// <summary>seconds 동안 입을 움직인다(타자 효과와 함께).</summary>
    public void Talk(float seconds) => _talkUntil = Mathf.Max(_talkUntil, Time.time + seconds);
    public void StopTalking() => _talkUntil = 0f;

    /// <summary>짧은 표정. weight 0~1.</summary>
    public void Express(BlendShapePreset preset, float seconds, float weight = 0.8f)
    {
        if (_proxy == null) return;
        if (preset != _expr && _exprWeight > 0.01f)
            SetBlend(_expr, 0f);
        _expr = preset;
        _exprTarget = weight;
        _exprUntil = Time.time + seconds;
    }

    /// <summary>찔렀을 때 살짝 튄다.</summary>
    public void Hop() => _hopT = 0f;

    // ── 매 프레임 ───────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (!_ready) return;
        float t = Time.time + _phase;
        Vector3 fwd   = transform.TransformDirection(_fwdLocal);
        Vector3 right = transform.TransformDirection(_rightLocal);

        // 기준 자세로 되돌린 뒤 덧씌운다(누적 방지)
        Reset(_spine, _rSpine); Reset(_chest, _rChest); Reset(_neck, _rNeck); Reset(_head, _rHead);
        Reset(_lUpper, _rLUpper); Reset(_rUpper, _rRUpper); Reset(_lLower, _rLLower); Reset(_rLower, _rRLower);
        Reset(_upperChest, _rUpperChest); Reset(_lHand, _rLHand); Reset(_rHand, _rRHand);   // 동작(제스처)이 건드릴 수 있는 뼈도 매 프레임 원위치

        float breath = Mathf.Sin(t * breathSpeed);
        float sway   = Mathf.Sin(t * swaySpeed);
        Rotate(_spine, right, -breath * breathAngle * 0.4f);
        Rotate(_spine, fwd, sway * swayAngle * 0.6f);
        Rotate(_chest, right, -breath * breathAngle * 0.6f);
        Rotate(_neck, fwd, -sway * swayAngle * 0.3f);
        Rotate(_head, fwd, headTilt + Mathf.Sin(t * 0.31f) * 1.2f);
        Rotate(_head, right, Mathf.Sin(t * 0.53f) * 1.0f + (Talking ? Mathf.Sin(Time.time * 7f) * 1.2f : 0f));

        // 팔: 왼팔은 앞축 +, 오른팔은 앞축 - 로 돌리면 아래로 내려온다.
        float armBreath = breath * 1.2f;
        Rotate(_lUpper, fwd,  armDown + armBreath);
        Rotate(_rUpper, fwd, -(armDown + armBreath));
        Rotate(_lUpper, right, -4f);
        Rotate(_rUpper, right, -4f);
        Rotate(_lLower, right, -elbowBend);
        Rotate(_rLower, right, -elbowBend);
        // 아래팔을 팔뚝 축으로 안쪽으로(손등이 바깥) — 왼팔 -, 오른팔 +
        if (_lLower != null && _lHand != null) Rotate(_lLower, _lHand.position - _lLower.position, -forearmTwist);
        if (_rLower != null && _rHand != null) Rotate(_rLower, _rHand.position - _rLower.position,  forearmTwist);

        // 손: 손목 살짝 + 힘 뺀 손가락
        var idle = IdleShape;
        _leftHand?.Apply(idle, wristFlex);
        _rightHand?.Apply(idle, wristFlex);

        // 튀기
        float hop = 0f;
        if (_hopT >= 0f)
        {
            _hopT += Time.deltaTime;
            hop = Mathf.Sin(Mathf.Clamp01(_hopT / 0.32f) * Mathf.PI) * 0.025f;
            if (_hopT > 0.32f) _hopT = -1f;
        }
        transform.localPosition = _basePos + Vector3.up * hop;

        UpdateFace();
    }

    private bool Talking => Time.time < _talkUntil;

    private void UpdateFace()
    {
        if (_proxy == null) return;

        // 표정
        float target = Time.time < _exprUntil ? _exprTarget : 0f;
        _exprWeight = Mathf.MoveTowards(_exprWeight, target, Time.deltaTime * 3f);
        if (_expr != BlendShapePreset.Neutral) SetBlend(_expr, _exprWeight);

        // 깜빡임 (웃는 눈 표정일 땐 쉰다)
        bool eyesBusy = _exprWeight > 0.3f && (_expr == BlendShapePreset.Joy || _expr == BlendShapePreset.Fun);
        float blink = 0f;
        if (_blinkT >= 0f)
        {
            _blinkT += Time.deltaTime;
            blink = 1f - Mathf.Abs(_blinkT / 0.08f - 1f);
            if (_blinkT >= 0.16f) { _blinkT = -1f; blink = 0f; }
        }
        else if (Time.time >= _nextBlink)
        {
            _blinkT = 0f;
            _nextBlink = Time.time + Random.Range(2.2f, 5.5f);
        }
        SetBlend(BlendShapePreset.Blink, eyesBusy ? 0f : Mathf.Clamp01(blink));

        // 입
        float mouth = 0f;
        if (Talking)
            mouth = Mathf.Clamp01(0.35f + 0.45f * Mathf.Sin(Time.time * 17f) + 0.2f * Mathf.Sin(Time.time * 29f));
        SetBlend(BlendShapePreset.A, mouth * 0.75f);
    }

    private void SetBlend(BlendShapePreset p, float v)
    {
        try { _proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(p), v); }
        catch { /* 이 모델에 없는 표정 */ }
    }

    private static void Reset(Transform t, Quaternion rest) { if (t != null) t.localRotation = rest; }

    private static void Rotate(Transform t, Vector3 worldAxis, float degrees)
    {
        if (t == null || Mathf.Abs(degrees) < 1e-4f) return;
        t.rotation = Quaternion.AngleAxis(degrees, worldAxis) * t.rotation;
    }
}
