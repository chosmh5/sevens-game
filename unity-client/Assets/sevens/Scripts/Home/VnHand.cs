using UnityEngine;

/// <summary>
/// 한쪽 손(손목 + 손가락 15개)의 자세.
///
/// VRM 기준 자세(T포즈)에서 손가락마다 '손바닥 쪽으로 굽히는 축'과 '가운데로 모으는 축'을 뼈 위치로 구해 두고,
/// 매 프레임 기준 자세로 되돌린 뒤 모양(Shape: 굽힘·엄지·벌림)만큼 돌린다 → 누적되지 않는다.
/// 손가락 뼈가 일부 없는 모델이면 있는 뼈만 쓴다.
/// </summary>
public class VnHand
{
    /// <summary>손 모양. curl 0 = 쫙 편 손, 1 = 힘 뺀 손, 3 = 주먹. spread 음수면 손가락을 붙인다.</summary>
    public struct Shape
    {
        public float curl, thumb, spread;
        public Shape(float curl, float thumb, float spread) { this.curl = curl; this.thumb = thumb; this.spread = spread; }

        public static Shape Relaxed => new Shape(1f, 1f, 1f);
        public static Shape Lerp(Shape a, Shape b, float t) =>
            new Shape(Mathf.Lerp(a.curl, b.curl, t), Mathf.Lerp(a.thumb, b.thumb, t), Mathf.Lerp(a.spread, b.spread, t));
    }

    // 손가락별 마디 굽힘(도) — 새끼손가락 쪽으로 갈수록 더 굽는 '힘 뺀 손' (seven.vrm 렌더링으로 맞춤)
    private static readonly float[][] Relax =
    {
        new[] { 14f, 20f, 12f },   // 검지
        new[] { 20f, 26f, 14f },   // 중지
        new[] { 26f, 30f, 16f },   // 약지
        new[] { 32f, 34f, 18f },   // 새끼
    };
    private static readonly float[] Spread = { -4f, 0f, 4f, 8f };   // 가운데(중지)로 모으기
    private static readonly float[] ThumbCurl = { 14f, 24f, 20f };
    private const float ThumbIn = 30f;                                // 엄지를 검지 쪽으로

    private static readonly HumanBodyBones[,] LeftFingers =
    {
        { HumanBodyBones.LeftIndexProximal,  HumanBodyBones.LeftIndexIntermediate,  HumanBodyBones.LeftIndexDistal },
        { HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal },
        { HumanBodyBones.LeftRingProximal,   HumanBodyBones.LeftRingIntermediate,   HumanBodyBones.LeftRingDistal },
        { HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal },
    };
    private static readonly HumanBodyBones[,] RightFingers =
    {
        { HumanBodyBones.RightIndexProximal,  HumanBodyBones.RightIndexIntermediate,  HumanBodyBones.RightIndexDistal },
        { HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal },
        { HumanBodyBones.RightRingProximal,   HumanBodyBones.RightRingIntermediate,   HumanBodyBones.RightRingDistal },
        { HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal },
    };

    public readonly int Side;            // +1 오른손, -1 왼손
    public Transform Wrist { get; private set; }
    public Transform LowerArm { get; private set; }

    private readonly Transform[,] _f = new Transform[4, 3];
    private readonly Quaternion[,] _fRest = new Quaternion[4, 3];
    private readonly Vector3[,] _fCurl = new Vector3[4, 3];      // 뼈 로컬 축
    private readonly Vector3[] _fSpread = new Vector3[4];
    private readonly Transform[] _t = new Transform[3];
    private readonly Quaternion[] _tRest = new Quaternion[3];
    private readonly Vector3[] _tCurl = new Vector3[3];
    private Vector3 _tIn;
    private Vector3 _wristFlex;                                    // 손목 로컬 축
    private Transform _middle, _index, _little;
    private float _palmSign = 1f;

    private VnHand(int side) { Side = side; }

    /// <summary>T포즈(기준 자세)일 때 불러야 한다.</summary>
    public static VnHand Create(Animator anim, int side)
    {
        var h = new VnHand(side);
        bool left = side < 0;
        h.Wrist    = anim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        h.LowerArm = anim.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var bones = left ? LeftFingers : RightFingers;
        for (int f = 0; f < 4; f++)
        for (int k = 0; k < 3; k++)
            h._f[f, k] = anim.GetBoneTransform(bones[f, k]);
        h._t[0] = anim.GetBoneTransform(left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
        h._t[1] = anim.GetBoneTransform(left ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate);
        h._t[2] = anim.GetBoneTransform(left ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal);
        h._index = h._f[0, 0]; h._middle = h._f[1, 0]; h._little = h._f[3, 0];
        if (h.Wrist == null || h._middle == null || h._index == null || h._little == null) return h.Wrist != null ? h : null;

        // 손 방향 d(손목→중지), 손바닥 법선 n(T포즈에선 아래), 가로 across(새끼→검지)
        Vector3 d = (h._middle.position - h.Wrist.position).normalized;
        Vector3 across = (h._index.position - h._little.position).normalized;
        Vector3 n = Vector3.Cross(d, across).normalized;
        h._palmSign = n.y > 0f ? -1f : 1f;
        n *= h._palmSign;

        Vector3 curl = Vector3.Cross(d, n).normalized;   // d 를 n(손바닥) 쪽으로
        for (int f = 0; f < 4; f++)
        for (int k = 0; k < 3; k++)
        {
            var b = h._f[f, k];
            if (b == null) continue;
            h._fRest[f, k] = b.localRotation;
            h._fCurl[f, k] = Quaternion.Inverse(b.rotation) * curl;
            if (k == 0) h._fSpread[f] = Quaternion.Inverse(b.rotation) * n;
        }

        if (h._t[0] != null && h._t[1] != null)
        {
            Vector3 dt = (h._t[1].position - h._t[0].position).normalized;
            Vector3 tc = Vector3.Cross(dt, n).normalized;
            Vector3 ti = Vector3.Cross(dt, d).normalized;
            for (int k = 0; k < 3; k++)
            {
                if (h._t[k] == null) continue;
                h._tRest[k] = h._t[k].localRotation;
                h._tCurl[k] = Quaternion.Inverse(h._t[k].rotation) * tc;
            }
            h._tIn = Quaternion.Inverse(h._t[0].rotation) * ti;
        }
        h._wristFlex = Quaternion.Inverse(h.Wrist.rotation) * curl;
        return h;
    }

    /// <summary>손가락을 기준 자세로 되돌린 뒤 shape 대로 굽힌다. wristFlex 가 있으면 손목도(손목은 바깥에서 되돌린다는 전제).</summary>
    public void Apply(Shape s, float wristFlexDeg = 0f)
    {
        float spreadSign = Side > 0 ? 1f : -1f;
        for (int f = 0; f < 4; f++)
        for (int k = 0; k < 3; k++)
        {
            var b = _f[f, k];
            if (b == null) continue;
            var r = _fRest[f, k] * Quaternion.AngleAxis(Relax[f][k] * s.curl, _fCurl[f, k]);
            if (k == 0) r *= Quaternion.AngleAxis(Spread[f] * s.spread * spreadSign, _fSpread[f]);
            b.localRotation = r;
        }
        for (int k = 0; k < 3; k++)
        {
            var b = _t[k];
            if (b == null) continue;
            var r = _tRest[k] * Quaternion.AngleAxis(ThumbCurl[k] * s.thumb, _tCurl[k]);
            if (k == 0) r *= Quaternion.AngleAxis(ThumbIn * Mathf.Min(s.thumb, 1.6f), _tIn);
            b.localRotation = r;
        }
        if (Mathf.Abs(wristFlexDeg) > 1e-3f && Wrist != null)
            Wrist.localRotation = Wrist.localRotation * Quaternion.AngleAxis(wristFlexDeg, _wristFlex);
    }

    /// <summary>지금 손바닥이 보는 방향(월드).</summary>
    public Vector3 PalmNormal
    {
        get
        {
            if (Wrist == null || _middle == null || _index == null || _little == null) return Vector3.zero;
            Vector3 d = (_middle.position - Wrist.position).normalized;
            Vector3 across = (_index.position - _little.position).normalized;
            return Vector3.Cross(d, across).normalized * _palmSign;
        }
    }

    /// <summary>
    /// 팔뚝 축으로 돌려 손바닥이 want 쪽을 보게 한다(아래팔 60% + 손목 40% — 한 뼈만 비틀면 손목이 꼬여 보인다).
    /// </summary>
    public void AlignPalm(Vector3 want, float weight, float maxDeg = 100f)
    {
        if (LowerArm == null || Wrist == null || weight <= 0f) return;
        Vector3 axis = (Wrist.position - LowerArm.position).normalized;
        Vector3 n = PalmNormal;
        Vector3 a = Vector3.ProjectOnPlane(n, axis), b = Vector3.ProjectOnPlane(want, axis);
        if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return;
        float ang = Vector3.SignedAngle(a, b, axis);
        ang = Mathf.Clamp(ang, -maxDeg, maxDeg) * weight;
        LowerArm.rotation = Quaternion.AngleAxis(ang * 0.6f, axis) * LowerArm.rotation;
        Wrist.rotation = Quaternion.AngleAxis(ang * 0.4f, axis) * Wrist.rotation;
    }
}
