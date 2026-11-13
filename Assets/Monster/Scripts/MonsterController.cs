using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bear-like monster controller. Lives on the monster's root object (the prefab already has it).
///
///   [x] Run On All Fours   -> gallop on four legs (head low, spine flexing, hind legs driving).
///   [x] Attack On Two Legs -> rears up onto its hind legs and goes straight into alternating claw swipes.
///   (neither)              -> idles on all fours (breathing, head sway).
///
/// If both are ticked, the attack wins (the monster stands up). Unticking attack drops it back onto all fours.
/// Everything is done in code on the FBX bones - no Animator or clips needed.
/// Based on real bear behaviour: plantigrade quadruped gallop, rearing up on the hind legs, forepaw swats.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Monster/Monster Controller")]
public class MonsterController : MonoBehaviour
{
    [Header("TICK THESE")]
    [Tooltip("Tick to gallop on all four legs.")]
    public bool runOnAllFours = false;

    [Tooltip("Tick to rear up on the hind legs and attack with the claws (like a bear). Overrides running.")]
    public bool attackOnTwoLegs = false;

    [Header("Run settings")]
    [Range(0.5f, 3f)] public float stridesPerSecond = 1.5f;
    [Tooltip("Also travel forward while running (off = runs on the spot).")]
    public bool moveWhileRunning = false;
    [Tooltip("0 = straight line. Otherwise the monster runs in a circle of this radius (metres).")]
    public float circleRadius = 0f;

    [Header("Attack settings")]
    [Range(0.4f, 3f)] public float riseSeconds = 1.1f;
    [Range(0.3f, 3f)] public float dropSeconds = 0.8f;
    [Range(0.3f, 2f)] public float swipeSeconds = 1.0f;

    [Header("Misc")]
    [Tooltip("Keeps feet, paws and body from sinking through the floor (the floor is the monster's own Y position).")]
    public bool keepAboveGround = true;

    class RigBone
    {
        public Transform t;
        public Vector3 restPos;
        public Quaternion restRot;
        public Quaternion fours;
        public Quaternion off = Quaternion.identity;
    }

    RigBone[] bones;
    readonly Dictionary<string, int> index = new Dictionary<string, int>();
    Vector3 lU, lF, lL;                 // creature basis in the monster root's local space
    Vector3 pitchAxisL, yawAxisL;       // + pitch lifts the front, + yaw turns toward the creature's left
    Vector3[][] contactPts;             // contact sample points, in each bone's local space
    int[] contactBone;
    int iRoot, iChest, iNeck, iHead;
    int[] thigh = new int[2], shin = new int[2], foot = new int[2];       // [0]=R, [1]=L
    int[] shoulder = new int[2], forearm = new int[2], hand = new int[2];
    // gait (feet are placed on the floor with two-bone IK so they really plant)
    const float Duty = 0.42f;                 // fraction of the stride a paw is on the ground
    const float RunDrop = 0.12f;              // body lowers a little when galloping
    const float SwingStartRise = 0.7f;        // swipes begin once the rise is this far along (0..1), so there is no pause at the top
    int[] limbUpper = new int[4], limbMid = new int[4], limbEnd = new int[4], limbContact = new int[4];
    float[] limbHx = new float[4], limbHz = new float[4], limbClear = new float[4];
    Quaternion[] limbRestRot = new Quaternion[4];
    static readonly float[] LimbPhase = { 0f, 0.13f, 0.5f, 0.63f };   // R hind, L hind, L fore, R fore
    static readonly bool[] LimbHind = { true, true, false, false };
    static readonly string[] LimbEndName = { "Foot_R", "Foot_L", "Hand_L", "Hand_R" };
    bool rigReady;
    float riseT, attackTime, phase, runW, idleTime;
    Vector3 pitchW, yawW;

    /// <summary>Ground speed (m/s) at which the paws do not slide while galloping.</summary>
    public float RunSpeed { get { return 2.67f * stridesPerSecond; } }

    /// <summary>Direction the monster faces (world space, flattened).</summary>
    public Vector3 Forward { get { return transform.TransformDirection(lF); } }

    void Awake() { DetectRig(); }

    static float SStep(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    static float Keys(float t, float[] kt, float[] kv)
    {
        for (int i = 0; i < kt.Length - 1; i++)
        {
            if (t <= kt[i + 1])
                return kv[i] + (kv[i + 1] - kv[i]) * SStep((t - kt[i]) / (kt[i + 1] - kt[i]));
        }
        return kv[kv.Length - 1];
    }

    Vector3 Rmul(float[] m, Vector3 v)
    {
        float bx = Vector3.Dot(v, lL), by = Vector3.Dot(v, lU), bz = Vector3.Dot(v, lF);
        float x = m[0] * bx + m[1] * by + m[2] * bz;
        float y = m[3] * bx + m[4] * by + m[5] * bz;
        float z = m[6] * bx + m[7] * by + m[8] * bz;
        return x * lL + y * lU + z * lF;
    }

    /// <summary>Finds the bones and records the bind pose. Safe to call more than once.</summary>
    public bool DetectRig()
    {
        if (rigReady) return true;
        Dictionary<string, Transform> map = new Dictionary<string, Transform>();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (!map.ContainsKey(t.name)) map[t.name] = t;

        string[] names = MonsterRigData.Bones;
        List<string> missing = new List<string>();
        foreach (string n in names) if (!map.ContainsKey(n)) missing.Add(n);
        if (missing.Count > 0)
        {
            Debug.LogError("MonsterController: missing bones: " + string.Join(", ", missing.ToArray()) + ". Use the Monster prefab / Monster.fbx from this package.", this);
            return false;
        }

        bones = new RigBone[names.Length];
        index.Clear();
        for (int i = 0; i < names.Length; i++)
        {
            RigBone b = new RigBone();
            b.t = map[names[i]];
            b.restPos = b.t.localPosition;
            b.restRot = b.t.localRotation;
            bones[i] = b;
            index[names[i]] = i;
        }

        // --- creature basis, found from the bones (so it works no matter how Unity converted the FBX axes)
        Vector3 up = transform.up;
        Vector3 fw = Vector3.ProjectOnPlane((map["Foot_L_end"].position - map["Foot_L"].position) + (map["Foot_R_end"].position - map["Foot_R"].position), up);
        if (fw.sqrMagnitude < 1e-8f) fw = transform.forward;
        fw.Normalize();
        Vector3 lf = Vector3.ProjectOnPlane(map["Shoulder_L"].position - map["Shoulder_R"].position, up);
        lf -= Vector3.Dot(lf, fw) * fw;
        lf.Normalize();
        lU = transform.InverseTransformDirection(up).normalized;
        lF = transform.InverseTransformDirection(fw).normalized;
        lL = transform.InverseTransformDirection(lf).normalized;

        Vector3 cand = Vector3.Cross(lU, lF).normalized;
        Vector3 test = Quaternion.AngleAxis(10f, cand) * lF;
        pitchAxisL = Vector3.Dot(test, lU) > 0f ? cand : -cand;
        Vector3 test2 = Quaternion.AngleAxis(10f, lU) * lF;
        yawAxisL = Vector3.Dot(test2, lL) > 0f ? lU : -lU;

        // --- all-fours pose (stored as rotation matrices in the creature basis)
        for (int i = 0; i < bones.Length; i++)
        {
            float[] m = MonsterRigData.Fours[i];
            Vector3 f = Rmul(m, Vector3.forward);
            Vector3 u = Rmul(m, Vector3.up);
            bones[i].fours = bones[i].restRot * Quaternion.LookRotation(f, u);
        }

        // --- ground contact points
        int nc = MonsterRigData.ContactBones.Length;
        contactBone = new int[nc];
        contactPts = new Vector3[nc][];
        for (int k = 0; k < nc; k++)
        {
            int bi = index[MonsterRigData.ContactBones[k]];
            contactBone[k] = bi;
            float[] a = MonsterRigData.ContactPoints[k];
            Quaternion boneLocal = Quaternion.Inverse(transform.rotation) * bones[bi].t.rotation;
            Quaternion inv = Quaternion.Inverse(boneLocal);
            Vector3[] pts = new Vector3[a.Length / 3];
            for (int j = 0; j < pts.Length; j++)
            {
                Vector3 v = a[3 * j] * lL + a[3 * j + 1] * lU + a[3 * j + 2] * lF;
                pts[j] = inv * v;
            }
            contactPts[k] = pts;
        }

        iRoot = index["Root"]; iChest = index["Chest"]; iNeck = index["Neck"]; iHead = index["Head"];
        string[] sd = { "R", "L" };
        for (int s = 0; s < 2; s++)
        {
            thigh[s] = index["Thigh_" + sd[s]]; shin[s] = index["Shin_" + sd[s]]; foot[s] = index["Foot_" + sd[s]];
            shoulder[s] = index["Shoulder_" + sd[s]]; forearm[s] = index["Forearm_" + sd[s]]; hand[s] = index["Hand_" + sd[s]];
        }

        // --- gait data: where each paw rests in the all-fours pose (floor-relative), and how high the ankle/wrist sits above the floor
        limbUpper[0] = thigh[0];    limbMid[0] = shin[0];    limbEnd[0] = foot[0];
        limbUpper[1] = thigh[1];    limbMid[1] = shin[1];    limbEnd[1] = foot[1];
        limbUpper[2] = shoulder[1]; limbMid[2] = forearm[1]; limbEnd[2] = hand[1];
        limbUpper[3] = shoulder[0]; limbMid[3] = forearm[0]; limbEnd[3] = hand[0];
        SetBasePose(0f);
        for (int k = 0; k < 4; k++)
        {
            limbContact[k] = System.Array.IndexOf(MonsterRigData.ContactBones, LimbEndName[k]);
            Transform et = bones[limbEnd[k]].t;
            Vector3 p = transform.InverseTransformPoint(et.position);
            limbHx[k] = Vector3.Dot(p, lL);
            limbHz[k] = Vector3.Dot(p, lF);
            limbClear[k] = Vector3.Dot(p, lU) - ContactMinHeight(limbContact[k]);
            limbRestRot[k] = Quaternion.Inverse(transform.rotation) * et.rotation;
        }
        for (int i = 0; i < bones.Length; i++)
        {
            bones[i].t.localPosition = bones[i].restPos;
            bones[i].t.localRotation = bones[i].restRot;
        }
        rigReady = true;
        return true;
    }

    float ContactMinHeight(int k)
    {
        Transform bt = bones[contactBone[k]].t;
        Vector3[] pts = contactPts[k];
        float minY = float.MaxValue;
        for (int j = 0; j < pts.Length; j++)
        {
            float y = Vector3.Dot(transform.InverseTransformPoint(bt.TransformPoint(pts[j])), lU);
            if (y < minY) minY = y;
        }
        return minY;
    }

    void SetBasePose(float r)
    {
        for (int i = 0; i < bones.Length; i++)
        {
            RigBone b = bones[i];
            b.t.localPosition = b.restPos;
            b.t.localRotation = Quaternion.Slerp(b.fours, b.restRot, r);
            b.off = Quaternion.identity;
        }
        bones[iRoot].t.localPosition = bones[iRoot].restPos + lU * ((1f - r) * -MonsterRigData.GroundFours);
    }

    void Add(int b, float pitchDeg, float yawDeg)
    {
        Quaternion q = Quaternion.AngleAxis(yawDeg, yawW) * Quaternion.AngleAxis(pitchDeg, pitchW);
        bones[b].off = q * bones[b].off;
    }
    void Add(int b, float pitchDeg) { Add(b, pitchDeg, 0f); }

    void LateUpdate()
    {
        if (!rigReady && !DetectRig()) return;
        float dt = Time.deltaTime;

        // ---------- state
        bool attacking = attackOnTwoLegs;
        bool running = runOnAllFours && !attackOnTwoLegs;
        float target = attacking ? 1f : 0f;
        float rate = target > riseT ? 1f / riseSeconds : 1f / dropSeconds;
        riseT = Mathf.MoveTowards(riseT, target, dt * rate);
        float r = SStep(riseT);
        runW = Mathf.MoveTowards(runW, running ? 1f : 0f, dt / 0.35f);
        if (runW > 0f) phase = Mathf.Repeat(phase + stridesPerSecond * 2f * Mathf.PI * dt, 2f * Mathf.PI);
        idleTime += dt;
        if (attacking && riseT > SwingStartRise) attackTime += dt;
        else if (!attacking && riseT < 0.5f) attackTime = 0f;

        // ---------- base pose: blend between all-fours (r=0) and upright bind pose (r=1)
        pitchW = transform.TransformDirection(pitchAxisL);
        yawW = transform.TransformDirection(yawAxisL);
        SetBasePose(r);
        if (runW > 0f)
            bones[iRoot].t.localPosition += lU * (runW * (-RunDrop + 0.05f * Mathf.Cos(2f * phase + 0.5f)));

        // ---------- animation layers (world-space rotation offsets about the body's side/up axes)
        float wIdle = (1f - runW) * (1f - r);
        if (wIdle > 0f) Idle(wIdle);
        if (runW > 0f) Run(phase, runW);
        if (r > 0f) Attack(r);

        // ---------- apply offsets parent-first
        for (int i = 0; i < bones.Length; i++)
            bones[i].t.rotation = bones[i].off * bones[i].t.rotation;

        // ---------- gallop: plant the paws on the floor
        if (runW > 0f) RunIK(phase, runW);

        // ---------- keep above the floor
        if (keepAboveGround)
        {
            float minY = float.MaxValue;
            for (int k = 0; k < contactPts.Length; k++)
                minY = Mathf.Min(minY, ContactMinHeight(k));
            if (minY < 0f) bones[iRoot].t.localPosition += lU * (-minY);
        }

        // ---------- optional travel
        if (moveWhileRunning && runW > 0f)
        {
            Vector3 f = Vector3.ProjectOnPlane(Forward, Vector3.up).normalized;
            float speed = RunSpeed;
            transform.position += f * (speed * runW * dt);
            if (circleRadius > 0.1f)
                transform.Rotate(0f, (speed / circleRadius) * Mathf.Rad2Deg * runW * dt, 0f, Space.World);
        }
    }

    // ===================== animation layers =====================

    void Idle(float w)
    {
        float t = idleTime, tp = 2f * Mathf.PI;
        Add(iChest, w * 1.2f * Mathf.Sin(tp * t / 4f));
        Add(iNeck, w * (2f * Mathf.Sin(tp * t / 4f + 0.6f) - 6f));
        Add(iHead, w * (-4f + 3f * Mathf.Sin(tp * t / 5.3f)), w * 14f * Mathf.Sin(tp * t / 7f));
    }

    // Bear-style gallop. Body part: hump flexes, shoulders roll, head stays low.
    void Run(float ph, float w)
    {
        Add(iRoot, w * 3f * Mathf.Sin(ph + 0.3f));
        Add(iChest, w * 7f * Mathf.Sin(ph + 1.0f), w * 4f * Mathf.Sin(ph));
        Add(iNeck, w * (-8f + 4f * Mathf.Sin(ph + 2.2f)));
        Add(iHead, w * -6f);
    }

    // Legs: each paw follows a floor path (planted and sliding back, then lifted and swung forward).
    // Hind pair drives (right then left), then the forelegs reach (left then right).
    void RunIK(float ph, float w)
    {
        Vector3 pole = transform.TransformDirection((-lF + 0.2f * lU).normalized);   // knees / elbows point backwards
        for (int k = 0; k < 4; k++)
        {
            float u = Mathf.Repeat(ph / (2f * Mathf.PI) + LimbPhase[k], 1f);
            bool hind = LimbHind[k];
            float back = hind ? 0.75f : 0.55f, front = hind ? 0.30f : 0.65f, lift = hind ? 0.55f : 0.50f;
            float z, y, q = 0f;
            if (u < Duty) { z = front - (front + back) * (u / Duty); y = 0f; }
            else { q = (u - Duty) / (1f - Duty); z = -back + (front + back) * SStep(q); y = lift * Mathf.Sin(Mathf.PI * q); }

            Transform et = bones[limbEnd[k]].t;
            Quaternion preEnd = et.rotation;
            Vector3 local = lL * limbHx[k] + lU * (limbClear[k] + y) + lF * (limbHz[k] + z);
            Vector3 target = Vector3.Lerp(et.position, transform.TransformPoint(local), w);
            TwoBoneIK(bones[limbUpper[k]].t, bones[limbMid[k]].t, et, target, pole);

            float tilt = u >= Duty ? 25f * Mathf.Sin(Mathf.PI * q) : 0f;     // toes lift a little in the swing
            Quaternion fin = transform.rotation * (Quaternion.AngleAxis(tilt, pitchAxisL) * limbRestRot[k]);
            et.rotation = Quaternion.Slerp(preEnd, fin, w);
        }
    }

    static void TwoBoneIK(Transform upper, Transform mid, Transform end, Vector3 target, Vector3 pole)
    {
        Vector3 a = upper.position, b = mid.position, c = end.position;
        float l1 = (b - a).magnitude, l2 = (c - b).magnitude;
        Vector3 toT = target - a;
        float dist = toT.magnitude;
        if (dist < 1e-4f || l1 < 1e-4f) return;
        Vector3 dir = toT / dist;
        float d = Mathf.Clamp(dist, Mathf.Abs(l1 - l2) + 0.001f, l1 + l2 - 0.001f);
        float cosA = (l1 * l1 + d * d - l2 * l2) / (2f * l1 * d);
        float sinA = Mathf.Sqrt(Mathf.Max(0f, 1f - cosA * cosA));
        Vector3 pl = Vector3.ProjectOnPlane(pole, dir);
        if (pl.sqrMagnitude < 1e-6f) return;
        pl.Normalize();
        Vector3 newMid = a + dir * (l1 * cosA) + pl * (l1 * sinA);
        upper.rotation = Quaternion.FromToRotation(b - a, newMid - a) * upper.rotation;
        Vector3 m2 = mid.position, c2 = end.position;
        mid.rotation = Quaternion.FromToRotation(c2 - m2, (a + dir * d) - m2) * mid.rotation;
    }

    static readonly float[] KT = { 0f, 0.4f, 0.62f, 1f };
    static readonly float[] kShP = { 0f, 28f, -50f, 0f };
    static readonly float[] kShY = { 0f, 20f, -30f, 0f };
    static readonly float[] kEl = { 0f, 14f, -12f, 0f };
    static readonly float[] kChP = { 0f, -3f, 4f, 0f };
    static readonly float[] kChY = { 0f, -12f, 14f, 0f };

    void Attack(float r)
    {
        float ready = SStep((r - 0.55f) / 0.45f);
        Add(iChest, -4f * SStep(r) * (1f - ready * 0.5f));
        for (int s = 0; s < 2; s++)
        {
            float sg = s == 1 ? 1f : -1f;       // + = left side
            Add(shoulder[s], 55f * ready, 8f * sg * ready);
            Add(forearm[s], 12f * ready);
        }
        if (ready <= 0f) return;

        // endless alternating swipes: left, right, left, right...
        float x = (attackTime % (2f * swipeSeconds)) / swipeSeconds;
        int k = Mathf.FloorToInt(x);
        Swipe(k == 0 ? 1 : 0, x - k, ready);
    }

    void Swipe(int s, float u, float ready)
    {
        float sg = s == 1 ? 1f : -1f;
        Add(shoulder[s], ready * Keys(u, KT, kShP), ready * sg * Keys(u, KT, kShY));
        Add(forearm[s], ready * Keys(u, KT, kEl));
        Add(iChest, ready * Keys(u, KT, kChP), ready * sg * Keys(u, KT, kChY));
    }

    // Handy for your own scripts / UI buttons:
    public void SetRunning(bool value) { runOnAllFours = value; }
    public void SetAttacking(bool value) { attackOnTwoLegs = value; }
}
