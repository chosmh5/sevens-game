using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using VRM;

/// <summary>캐릭터가 이따금 하는 동작.</summary>
public enum VnGesture
{
    None,
    Wave,          // 손 흔들기 (인사)
    HandsOnHips,   // 허리에 손 (자신만만·허세)
    HairTuck,      // 귀 옆 머리 넘기기 (수줍음·호감)
    ThinkChin,     // 턱에 손 (궁금·생각)
    HandsClasped,  // 앞에서 두 손 모으기 (공손·시무룩)
    ArmsCrossed,   // 팔짱 (불만·긴장)
    Cheer,         // 두 주먹 불끈 + 콩 (기쁨)
    Nod,           // 끄덕
    ShakeHead,     // 도리도리
    LookAway,      // 흥, 고개 돌리기
    Slump,         // 축 처짐 (슬픔)
    BehindBack,    // 뒷짐 (삐짐·수줍음)
}

/// <summary>
/// 감정에 따라 이따금 동작을 한다(애니메이션 파일 없이 코드로).
///
/// - 기분(mood): 서버 관계의 감정(friendly/neutral/curious/tense/hostile) + 방금 한 대사로 잠깐 바뀌는 기분.
///   기분마다 자주 하는 동작이 다르다(예: 긴장 → 팔짱·고개 돌리기, 호감 → 머리 넘기기·두 손 모으기).
/// - 대사 반응(React): 대사·플레이어 말의 내용에 맞는 동작 + 표정.
/// - 손은 '목표 위치'로 보내는 2본 IK 로 움직인다 → 모델 체형이 달라도 손이 허리·귀·턱에 대략 맞는다.
/// - VnCharacterPose(대기 자세, -100) 다음, SpringBone(0) 전에 돈다.
/// </summary>
[DefaultExecutionOrder(-90)]
public class VnGesturePlayer : MonoBehaviour
{
    public enum Mood { Neutral, Friendly, Curious, Tense, Hostile, Happy, Sad }

    [Header("가끔 하는 동작")]
    public bool idleGestures = true;
    [Tooltip("동작 사이 간격(초) 최소~최대")]
    public Vector2 idleInterval = new Vector2(8f, 16f);
    [Tooltip("서버 관계 감정에서 오는 기본 기분")]
    public Mood mood = Mood.Neutral;

    private const float FadeIn = 0.4f, FadeOutTime = 0.5f, CrossFade = 0.3f;

    private VnCharacterPose _pose;
    private Transform _hips, _lLeg, _rLeg, _spine, _chest, _neck, _head;
    private Transform _lU, _lL, _lH, _rU, _rL, _rH;
    private bool _ready;

    private class Layer
    {
        public VnGesture G;
        public float T, Hold;
        public float FadeOut = -1f, OutFrom;
        public int Variant;
    }
    private readonly List<Layer> _layers = new List<Layer>();
    private float _nextIdle;
    // 이번 프레임 손 모양(레이어를 섞은 결과) — 동작이 건드린 손만 다시 적용
    private VnHand.Shape _shapeL, _shapeR;
    private bool _touchedL, _touchedR;
    private VnGesture _last;
    private Mood _transient;
    private float _transientUntil;

    public VnGesture Current => _layers.Count > 0 ? _layers[_layers.Count - 1].G : VnGesture.None;
    public Mood EffectiveMood => Time.time < _transientUntil ? _transient : mood;

    private void Start()
    {
        _pose = GetComponent<VnCharacterPose>();
        var anim = GetComponentInChildren<Animator>();
        if (_pose == null || !_pose.Ready || anim == null || !anim.isHuman) { enabled = false; return; }
        _hips  = anim.GetBoneTransform(HumanBodyBones.Hips);
        _lLeg  = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        _rLeg  = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        _spine = anim.GetBoneTransform(HumanBodyBones.Spine);
        // ※ 매 프레임 기준 자세로 되돌려지는 뼈만 돌린다(VnCharacterPose 가 되돌리는 Chest/Spine).
        //   되돌려지지 않는 뼈(예: UpperChest)를 돌리면 각도가 프레임마다 쌓여 몸이 빙글빙글 돈다.
        _chest = anim.GetBoneTransform(HumanBodyBones.Chest) ?? _spine;
        _neck  = anim.GetBoneTransform(HumanBodyBones.Neck);
        _head  = anim.GetBoneTransform(HumanBodyBones.Head);
        _lU = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        _lL = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        _lH = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        _rU = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        _rL = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        _rH = anim.GetBoneTransform(HumanBodyBones.RightHand);
        _ready = _head != null && _lU != null && _lL != null && _lH != null && _rU != null && _rL != null && _rH != null;
        _nextIdle = Time.time + Random.Range(4f, 8f);
    }

    // ══════════════════════════════════════════════════════════════════
    //  외부에서 부르는 것
    // ══════════════════════════════════════════════════════════════════

    /// <summary>동작 시작. 하던 동작은 부드럽게 넘어간다. hold &lt;= 0 이면 기본 길이.</summary>
    public void Play(VnGesture g, float hold = -1f, bool withExpression = true)
    {
        if (!_ready || g == VnGesture.None) return;
        foreach (var l in _layers)
            if (l.FadeOut < 0f) { l.OutFrom = Weight(l); l.FadeOut = 0f; }
        var layer = new Layer { G = g, Hold = hold > 0f ? hold : DefaultHold(g), Variant = Random.Range(0, 2) };
        _layers.Add(layer);
        _last = g;
        if (withExpression) ExpressFor(g, layer.Hold + FadeIn);
        _nextIdle = Time.time + FadeIn + layer.Hold + FadeOutTime + Random.Range(idleInterval.x, idleInterval.y);
    }

    /// <summary>서버 관계 감정(friendly/neutral/curious/tense/hostile)으로 기본 기분을 정한다.</summary>
    public void SetMood(string serverEmotion)
    {
        switch ((serverEmotion ?? "").ToLowerInvariant())
        {
            case "friendly": mood = Mood.Friendly; break;
            case "curious":  mood = Mood.Curious;  break;
            case "tense":    mood = Mood.Tense;    break;
            case "hostile":  mood = Mood.Hostile;  break;
            default:         mood = Mood.Neutral;  break;
        }
    }

    private void SetTransient(Mood m, float seconds = 25f)
    {
        _transient = m;
        _transientUntil = Time.time + seconds;
    }

    private static readonly Regex RxCompliment = new Regex("귀엽|귀여|예쁘|이쁘|좋아해|사랑|멋지|멋있|고마워|고맙|최고야|잘했");
    private static readonly Regex RxAngry   = new Regex("흥|어림없|짜증|싫어|안 봐|건방|됐거든|됐어요|뭐예요|그만");
    private static readonly Regex RxSad     = new Regex("미안|아쉽|속상|졌|슬프|서운|외로");
    private static readonly Regex RxBoast   = new Regex("역시|당연|저니까|제가 이|1등|이길|실력|어림");
    private static readonly Regex RxJoy     = new Regex("좋아요|재밌|기뻐|기쁘|이겼|신나|ㅎㅎ|!");
    private static readonly Regex RxAgree   = new Regex("그래요|맞아요|알았어요|좋죠|그렇죠|네,");

    /// <summary>대사(와 플레이어가 한 말)에 맞춰 동작·표정·잠깐의 기분을 정한다.</summary>
    public void React(string line, string playerMessage = null)
    {
        if (!_ready) return;
        line = line ?? "";
        string p = playerMessage ?? "";

        if (RxCompliment.IsMatch(p))
        {
            // 칭찬을 들으면 허세를 부려도 손은 머리로 간다
            SetTransient(Mood.Friendly);
            Play(Random.value < 0.7f ? VnGesture.HairTuck : VnGesture.HandsClasped);
            _pose.Express(BlendShapePreset.Joy, 3f, 0.55f);
            return;
        }
        if (RxAngry.IsMatch(line))
        {
            SetTransient(Mood.Tense);
            float r = Random.value;
            Play(r < 0.5f ? VnGesture.ArmsCrossed : r < 0.8f ? VnGesture.LookAway : VnGesture.BehindBack);
        }
        else if (RxSad.IsMatch(line))
        {
            SetTransient(Mood.Sad);
            Play(Random.value < 0.6f ? VnGesture.HandsClasped : VnGesture.Slump);
        }
        else if (RxBoast.IsMatch(line))
        {
            Play(VnGesture.HandsOnHips);
        }
        else if (RxJoy.IsMatch(line))
        {
            SetTransient(Mood.Happy);
            Play(Random.value < 0.5f ? VnGesture.Cheer : VnGesture.Nod);
        }
        else if (line.Contains("?"))
        {
            Play(Random.value < 0.6f ? VnGesture.ThinkChin : VnGesture.HairTuck);
        }
        else if (RxAgree.IsMatch(line) || Random.value < 0.35f)
        {
            Play(VnGesture.Nod);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  기분별 동작 고르기 / 표정
    // ══════════════════════════════════════════════════════════════════

    // 기분별로 자주 하는 동작(같은 동작이 여러 번 들어 있으면 그만큼 자주 나온다)
    private static readonly VnGesture[] PoolNeutral  = { VnGesture.HandsOnHips, VnGesture.HandsOnHips, VnGesture.HandsOnHips, VnGesture.ThinkChin, VnGesture.LookAway, VnGesture.HairTuck };
    private static readonly VnGesture[] PoolFriendly = { VnGesture.HairTuck, VnGesture.HairTuck, VnGesture.HairTuck, VnGesture.HandsClasped, VnGesture.HandsClasped, VnGesture.HandsOnHips, VnGesture.Wave, VnGesture.BehindBack };
    private static readonly VnGesture[] PoolCurious  = { VnGesture.ThinkChin, VnGesture.ThinkChin, VnGesture.ThinkChin, VnGesture.HairTuck, VnGesture.Nod };
    private static readonly VnGesture[] PoolTense    = { VnGesture.ArmsCrossed, VnGesture.ArmsCrossed, VnGesture.ArmsCrossed, VnGesture.LookAway, VnGesture.LookAway, VnGesture.HandsOnHips, VnGesture.BehindBack };
    private static readonly VnGesture[] PoolHostile  = { VnGesture.ArmsCrossed, VnGesture.LookAway, VnGesture.BehindBack };
    private static readonly VnGesture[] PoolHappy    = { VnGesture.HandsOnHips, VnGesture.HandsOnHips, VnGesture.Cheer, VnGesture.HairTuck };
    private static readonly VnGesture[] PoolSad      = { VnGesture.HandsClasped, VnGesture.Slump };

    private VnGesture PickIdle()
    {
        VnGesture[] pool;
        switch (EffectiveMood)
        {
            case Mood.Friendly: pool = PoolFriendly; break;
            case Mood.Curious:  pool = PoolCurious;  break;
            case Mood.Tense:    pool = PoolTense;    break;
            case Mood.Hostile:  pool = PoolHostile;  break;
            case Mood.Happy:    pool = PoolHappy;    break;
            case Mood.Sad:      pool = PoolSad;      break;
            default:            pool = PoolNeutral;  break;
        }
        var g = pool[Random.Range(0, pool.Length)];
        if (g == _last && pool.Length > 1) g = pool[Random.Range(0, pool.Length)];   // 같은 동작 연속은 덜
        return g;
    }

    private static float DefaultHold(VnGesture g)
    {
        switch (g)
        {
            case VnGesture.Wave:         return 1.6f;
            case VnGesture.HandsOnHips:  return 3.5f;
            case VnGesture.HairTuck:     return 2.2f;
            case VnGesture.ThinkChin:    return 3.0f;
            case VnGesture.HandsClasped: return 3.0f;
            case VnGesture.ArmsCrossed:  return 3.5f;
            case VnGesture.Cheer:        return 1.1f;
            case VnGesture.Nod:          return 0.9f;
            case VnGesture.ShakeHead:    return 1.0f;
            case VnGesture.LookAway:     return 2.0f;
            case VnGesture.Slump:        return 2.6f;
            case VnGesture.BehindBack:   return 3.0f;
            default:                     return 2f;
        }
    }

    private void ExpressFor(VnGesture g, float seconds)
    {
        switch (g)
        {
            case VnGesture.Wave:         _pose.Express(BlendShapePreset.Joy, seconds, 0.5f); break;
            case VnGesture.HandsOnHips:  _pose.Express(BlendShapePreset.Fun, seconds, 0.5f); break;
            case VnGesture.HairTuck:     _pose.Express(BlendShapePreset.Joy, seconds, 0.35f); break;
            case VnGesture.HandsClasped:
                if (EffectiveMood == Mood.Sad) _pose.Express(BlendShapePreset.Sorrow, seconds, 0.6f);
                else _pose.Express(BlendShapePreset.Joy, seconds, 0.3f);
                break;
            case VnGesture.ArmsCrossed:  _pose.Express(BlendShapePreset.Angry, seconds, 0.55f); break;
            case VnGesture.Cheer:        _pose.Express(BlendShapePreset.Joy, seconds, 0.9f); _pose.Hop(); break;
            case VnGesture.ShakeHead:    _pose.Express(BlendShapePreset.Angry, seconds, 0.3f); break;
            case VnGesture.LookAway:     _pose.Express(BlendShapePreset.Angry, seconds, 0.35f); break;
            case VnGesture.Slump:        _pose.Express(BlendShapePreset.Sorrow, seconds, 0.7f); break;
            case VnGesture.BehindBack:
                if (EffectiveMood == Mood.Tense || EffectiveMood == Mood.Hostile) _pose.Express(BlendShapePreset.Angry, seconds, 0.3f);
                else _pose.Express(BlendShapePreset.Joy, seconds, 0.25f);
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  매 프레임
    // ══════════════════════════════════════════════════════════════════

    private void LateUpdate()
    {
        if (!_ready || !_pose.isActiveAndEnabled) return;   // 대기 자세가 매 프레임 되돌려 줄 때만(누적 방지)

        if (idleGestures && _layers.Count == 0 && Time.time >= _nextIdle)
            Play(PickIdle());

        for (int i = _layers.Count - 1; i >= 0; i--)
        {
            var l = _layers[i];
            l.T += Time.deltaTime;
            if (l.FadeOut >= 0f) l.FadeOut += Time.deltaTime;
            bool done = l.FadeOut >= 0f ? l.FadeOut >= CrossFade : l.T >= FadeIn + l.Hold + FadeOutTime;
            if (done) _layers.RemoveAt(i);
        }

        _shapeL = _shapeR = _pose.IdleShape;
        _touchedL = _touchedR = false;

        foreach (var l in _layers)
        {
            float w = Weight(l);
            if (w > 0.001f) Apply(l, w);
        }

        // 동작이 쓴 손만 손가락 모양을 다시(대기 자세가 이미 '힘 뺀 손'을 적용해 둠)
        if (_touchedL) _pose.Hand(-1)?.Apply(_shapeL);
        if (_touchedR) _pose.Hand(+1)?.Apply(_shapeR);
    }

    private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    private float Weight(Layer l)
    {
        if (l.FadeOut >= 0f) return l.OutFrom * (1f - Smooth(l.FadeOut / CrossFade));
        float w = Smooth(l.T / FadeIn);
        float outT = l.T - FadeIn - l.Hold;
        if (outT > 0f) w *= 1f - Smooth(outT / FadeOutTime);
        return w;
    }

    private void Apply(Layer l, float w)
    {
        Vector3 F = _pose.Forward;
        Vector3 U = Vector3.up;
        Vector3 R = Vector3.Cross(U, F).normalized;
        float t = l.T;
        bool sad = EffectiveMood == Mood.Sad;

        // ── 몸·고개 (도 단위: pitch + = 숙임, yaw + = 오른쪽, roll + = 기울임) ──
        float chestPitch = 0f, spineYaw = 0f, headYaw = 0f, headPitch = 0f, headRoll = 0f;
        float side = l.Variant == 0 ? 1f : -1f;   // 고개 돌리는 방향 등
        switch (l.G)
        {
            case VnGesture.HandsOnHips:  chestPitch = -3f; headPitch = -4f; headRoll = 4f * side; break;
            case VnGesture.HairTuck:     headRoll = 8f; headPitch = 4f; break;
            case VnGesture.ThinkChin:    chestPitch = 2f; headRoll = 7f; headPitch = -5f; break;
            case VnGesture.HandsClasped: chestPitch = sad ? 5f : 3f; headPitch = sad ? 10f : 3f; headRoll = sad ? 0f : 5f; break;
            case VnGesture.ArmsCrossed:  chestPitch = -2f; headYaw = 16f * side; headPitch = -6f; break;
            case VnGesture.Cheer:        chestPitch = -2f; headPitch = -3f; break;
            case VnGesture.Wave:         headRoll = 5f; break;
            case VnGesture.Nod:          headPitch = 9f * Mathf.Abs(Mathf.Sin(t * 6f)); break;
            case VnGesture.ShakeHead:    headYaw = 13f * Mathf.Sin(t * 10f); break;
            case VnGesture.LookAway:     spineYaw = 5f * side; headYaw = 26f * side; headPitch = -7f; break;
            case VnGesture.Slump:        chestPitch = 7f; headPitch = 13f; break;
            case VnGesture.BehindBack:   chestPitch = -2f; headYaw = 12f * side; headPitch = 4f; headRoll = 5f * side; break;
        }
        Rotate(_spine, U, spineYaw * w);
        Rotate(_chest, R, chestPitch * w);
        Rotate(_head, U, headYaw * w);
        Rotate(_head, R, headPitch * w);
        Rotate(_head, F, headRoll * w);

        // ── 손 (IK 목표) ──
        Vector3 head = _head.position;
        Vector3 chest = _chest.position;
        Vector3 hipL = _lLeg != null ? _lLeg.position : _hips.position - R * 0.09f;
        Vector3 hipR = _rLeg != null ? _rLeg.position : _hips.position + R * 0.09f;
        Vector3 hipC = (hipL + hipR) * 0.5f;

        switch (l.G)
        {
            // 목표·팔꿈치 방향·손바닥 방향·손 모양은 seven.vrm 을 렌더링하며 맞춘 값
            case VnGesture.HandsOnHips:
                Arm(+1, hipR + R * 0.08f + U * 0.14f - F * 0.03f, R - F * 0.4f, -R, Grip, w);
                Arm(-1, hipL - R * 0.08f + U * 0.14f - F * 0.03f, -R - F * 0.4f, R, Grip, w);
                break;
            case VnGesture.HairTuck:   // 귀 옆, 팔꿈치는 옆으로 들고 손바닥은 머리 쪽
                Arm(+1, head + R * 0.12f + U * 0.01f - F * 0.01f, R * 0.8f - U * 0.6f + F * 0.25f, -R, Soft, w);
                break;
            case VnGesture.ThinkChin:  // 오른손은 턱, 왼손은 팔꿈치 받치기
                Arm(+1, head - U * 0.10f + F * 0.10f + R * 0.02f, -U + R * 0.6f, -F + U * 0.3f, LooseFist, w);
                Arm(-1, chest + F * 0.16f - U * 0.14f + R * 0.06f, -R - U, U, Grip, w * 0.9f);
                break;
            case VnGesture.HandsClasped:
                Arm(+1, hipC + F * 0.13f + U * (sad ? 0.0f : 0.03f) + R * 0.025f, R - U * 0.3f - F * 0.3f, -R - F * 0.3f, Clasp, w);
                Arm(-1, hipC + F * 0.13f + U * (sad ? 0.0f : 0.03f) - R * 0.025f, -R - U * 0.3f - F * 0.3f, R - F * 0.3f, Clasp, w);
                break;
            case VnGesture.ArmsCrossed:   // 아래팔을 겹치고 손은 반대쪽 팔꿈치 안으로
                Arm(+1, chest + F * 0.12f - U * 0.02f - R * 0.06f, R - U * 0.2f, -F - U * 0.3f, Tuck, w);
                Arm(-1, chest + F * 0.09f - U * 0.07f + R * 0.06f, -R - U * 0.2f, -F - U * 0.3f, Tuck, w);
                break;
            case VnGesture.Cheer:         // 가슴 앞 두 주먹
                Arm(+1, _rU.position - U * 0.14f + F * 0.22f, -U + R * 0.6f, -R * 0.6f - F, Fist, w);
                Arm(-1, _lU.position - U * 0.14f + F * 0.22f, -U - R * 0.6f, R * 0.6f - F, Fist, w);
                break;
            case VnGesture.Wave:          // 얼굴 옆, 손바닥은 앞, 좌우로 살랑
                Arm(+1, _rU.position + R * (0.20f + 0.04f * Mathf.Sin(t * 9f)) + U * 0.16f + F * 0.12f,
                    R - U * 0.8f, F, Open, w);
                break;
            case VnGesture.Slump:
                Arm(+1, hipR + F * 0.05f + R * 0.02f - U * 0.04f, R * 0.3f - F, -R, Limp, w * 0.6f);
                Arm(-1, hipL + F * 0.05f - R * 0.02f - U * 0.04f, -R * 0.3f - F, R, Limp, w * 0.6f);
                break;
            case VnGesture.BehindBack:    // 뒷짐
                Arm(+1, hipC - F * 0.12f + U * 0.04f + R * 0.03f, R + F * 0.2f - U * 0.3f, -F, Clasp, w);
                Arm(-1, hipC - F * 0.12f + U * 0.04f - R * 0.03f, -R + F * 0.2f - U * 0.3f, -F, Clasp, w);
                break;
        }
    }

    // 손 모양(굽힘, 엄지, 벌림) — 1 = 힘 뺀 손
    private static readonly VnHand.Shape Grip      = new VnHand.Shape(1.6f, 1.0f, 0.0f);
    private static readonly VnHand.Shape Soft      = new VnHand.Shape(0.6f, 0.8f, -0.5f);
    private static readonly VnHand.Shape LooseFist = new VnHand.Shape(2.2f, 1.2f, 0.0f);
    private static readonly VnHand.Shape Clasp     = new VnHand.Shape(1.8f, 1.3f, -0.5f);
    private static readonly VnHand.Shape Tuck      = new VnHand.Shape(2.4f, 1.4f, -0.8f);
    private static readonly VnHand.Shape Fist      = new VnHand.Shape(3.0f, 1.6f, -1.0f);
    private static readonly VnHand.Shape Open      = new VnHand.Shape(0.25f, 0.4f, 1.6f);
    private static readonly VnHand.Shape Limp      = new VnHand.Shape(1.2f, 1.0f, 0.5f);

    /// <summary>side: +1 오른팔, -1 왼팔. 손을 target 으로 보내고, 손바닥을 palm 쪽으로, 손가락은 shape 로.</summary>
    private void Arm(int side, Vector3 target, Vector3 pole, Vector3 palm, VnHand.Shape shape, float w)
    {
        Arm(side, target, pole, w);
        _pose.Hand(side)?.AlignPalm(palm, w);
        if (side > 0) { _shapeR = VnHand.Shape.Lerp(_shapeR, shape, w); _touchedR = true; }
        else          { _shapeL = VnHand.Shape.Lerp(_shapeL, shape, w); _touchedL = true; }
    }

    /// <summary>side: +1 오른팔, -1 왼팔.</summary>
    private void Arm(int side, Vector3 target, Vector3 pole, float w)
    {
        if (side > 0) SolveTwoBone(_rU, _rL, _rH, target, pole, w);
        else          SolveTwoBone(_lU, _lL, _lH, target, pole, w);
    }

    /// <summary>
    /// 위팔·아래팔 2본 IK. 손이 target 에 닿도록 팔꿈치를 pole 쪽으로 굽힌 뒤, 지금 자세와 w 만큼 섞는다.
    /// </summary>
    private static void SolveTwoBone(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole, float w)
    {
        Quaternion idleU = upper.rotation, idleL = lower.rotation;
        Vector3 a = upper.position, b = lower.position, c = hand.position;
        float l1 = (b - a).magnitude, l2 = (c - b).magnitude;
        if (l1 < 1e-4f || l2 < 1e-4f) return;

        Vector3 at = target - a;
        float d = Mathf.Clamp(at.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
        Vector3 dir = at.sqrMagnitude > 1e-8f ? at.normalized : (b - a).normalized;
        float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
        Vector3 perp = Vector3.ProjectOnPlane(pole, dir);
        if (perp.sqrMagnitude < 1e-6f) perp = Vector3.ProjectOnPlane(Vector3.down, dir);
        perp.Normalize();
        Vector3 elbow = a + (dir * cosA + perp * Mathf.Sqrt(1f - cosA * cosA)) * l1;

        upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * upper.rotation;
        Vector3 b2 = lower.position, c2 = hand.position;
        Vector3 reach = a + dir * d;
        lower.rotation = Quaternion.FromToRotation(c2 - b2, reach - b2) * lower.rotation;

        Quaternion ikU = upper.rotation, ikL = lower.rotation;
        upper.rotation = Quaternion.Slerp(idleU, ikU, w);
        lower.rotation = Quaternion.Slerp(idleL, ikL, w);
    }

    private static void Rotate(Transform t, Vector3 axis, float degrees)
    {
        if (t == null || Mathf.Abs(degrees) < 1e-4f) return;
        t.rotation = Quaternion.AngleAxis(degrees, axis) * t.rotation;
    }
}
