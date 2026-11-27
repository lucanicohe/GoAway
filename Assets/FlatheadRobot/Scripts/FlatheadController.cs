using System.Collections.Generic;
using UnityEngine;

namespace FlatheadRobotKit
{
    /// <summary>
    /// Four-legged turret robot controller (on the root of the robot prefab).
    ///   [x] Walking        - spider-style walk. Works in ANY direction (Walk Direction 0-360) because the feet simply
    ///                        step toward the direction of travel; the body never has to turn.
    ///   [x] Aim At Target  - the turret pans and tilts so the gun points at the Target object.
    ///   [x] Jump           - crouch, launch, tuck the legs, land. Keeps jumping while ticked.
    /// All three can be ticked at the same time. Procedural (code on the FBX bones): no Animator needed.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Flathead Robot/Flathead Controller")]
    public class FlatheadController : MonoBehaviour
    {
        [Header("TICK THESE")]
        public bool walking = false;
        public bool aimAtTarget = true;
        public bool jump = false;

        [Tooltip("The object the gun aims at. Move it in the Scene view.")]
        public Transform target;

        [Header("Walk")]
        [Tooltip("Direction of travel relative to the robot: 0 = forward, 90 = right, 180 = back, 270 = left.")]
        [Range(0f, 360f)] public float walkDirection = 0f;
        [Tooltip("Steps (full cycles) per second.")]
        [Range(0.3f, 4f)] public float stepsPerSecond = 1.4f;
        [Range(0.05f, 0.5f)] public float strideLength = 0.30f;
        [Range(0.02f, 0.3f)] public float stepHeight = 0.13f;
        [Range(0f, 0.05f)] public float bodyBob = 0.015f;
        [Range(0f, 10f)] public float bodyLean = 3f;
        [Tooltip("Also move the robot along the walk direction (feet are matched to the speed so they don't slide).")]
        public bool moveWhileWalking = false;

        [Header("Aim")]
        [Tooltip("Max degrees per second the turret turns.")]
        [Range(30f, 1080f)] public float turretSpeed = 420f;
        [Range(-45f, 0f)] public float tiltMin = -15f;
        [Range(0f, 89f)] public float tiltMax = 70f;
        [Tooltip("Demo: makes the Target circle around the robot so you can watch the turret track it.")]
        public bool orbitTarget = false;
        public float orbitRadius = 3.5f;
        public float orbitHeight = 1.3f;
        public float orbitSpeed = 40f;

        [Header("Jump")]
        [Range(0.1f, 1.5f)] public float jumpHeight = 0.45f;
        [Range(0.4f, 2f)] public float jumpDuration = 0.9f;
        [Range(0f, 0.3f)] public float crouchDepth = 0.12f;
        [Tooltip("Pause between jumps while the box stays ticked.")]
        [Range(0f, 2f)] public float jumpPause = 0.35f;

        [Header("Sound (drag the AudioSources from inside the prefab here)")]
        [Tooltip("Noise sound. Plays again and again whatever the robot does (walking, standing, jumping, shooting) and stops when it dies. Untick 'Play On Awake' on the AudioSource.")]
        public AudioSource noiseSource;
        [Tooltip("Seconds of silence AFTER the noise sound has fully played, before it plays again.")]
        [Min(0f)] public float noiseInterval = 3f;
        [Tooltip("Walking sound. Loops only while the robot walks: it is silent while the robot stands still or jumps, and it stops when it dies. Untick 'Play On Awake' on the AudioSource.")]
        public AudioSource walkSource;
        [Tooltip("Fire sound. Plays every time the robot shoots a bolt. Untick 'Play On Awake' on the AudioSource.")]
        public AudioSource fireSource;
        [Tooltip("Destruction sound. Plays once when the robot dies (the noise and walking sounds stop at that moment). Untick 'Play On Awake' on the AudioSource.")]
        public AudioSource destructionSource;

        [Header("Malfunction (starts the moment the virus upload is complete)")]
        [Tooltip("Drag the NORMAL (non-stealth) material here. When the virus upload is complete, every mesh of the robot (all its material slots) switches from the stealth material to this one. Empty = no material change.")]
        public Material normalMaterial;
        [Tooltip("The robot shoots slowly and randomly while it malfunctions. Seconds between two shots = a random time between these two values.")]
        [Min(0.5f)] public float malfunctionShotMin = 1.5f;
        [Min(0.5f)] public float malfunctionShotMax = 4f;
        [Tooltip("0 = total chaos: the robot shoots wherever its gun happens to point, the player included (its bolts hurt like normal bolts). " +
                 "Above 0: a shot is held back while the gun points within this many degrees of the player, which makes the stray fire safer.")]
        [Range(0f, 90f)] public float skipPlayerAngle = 0f;

        [Header("Sounds through walls")]
        [Tooltip("When a wall is between the robot and the player (the camera), all its sounds get quieter and muffled.")]
        public bool muffleThroughWalls = true;
        [Tooltip("Which layers count as walls. Default = everything solid. The robot itself, the player and other droids are always ignored. Trigger colliders are ignored too.")]
        public LayerMask wallMask = ~0;
        [Tooltip("Volume while a wall blocks the sound (1 = unchanged, 0.4 = 40%).")]
        [Range(0f, 1f)] public float muffledVolume = 0.4f;
        [Tooltip("Low pass cutoff in Hz while a wall blocks the sound. Lower = more muffled.")]
        [Range(200f, 8000f)] public float muffledCutoff = 800f;
        [Tooltip("Seconds to fade between clear and muffled.")]
        [Range(0.01f, 1f)] public float muffleFadeTime = 0.15f;

        class RigBone { public Transform t; public Vector3 restPos; public Quaternion restRot; }

        static readonly string[] LegNames = { "Leg_R_Front", "Leg_L_Rear", "Leg_L_Front", "Leg_R_Rear" };   // first two = one trot pair
        RigBone[] all;
        RigBone body, pan, tilt, muzzle, root;
        RigBone[] upper = new RigBone[4], lower = new RigBone[4], foot = new RigBone[4], tip = new RigBone[4];
        Vector3[] ankleRest = new Vector3[4];       // root-local
        Vector3[] tipOffset = new Vector3[4];       // root-local (ankle -> toe, constant)
        Quaternion[] footRestRoot = new Quaternion[4];
        Vector3 lF, lU, lR;                          // robot basis in root-local space
        float yawSign = 1f, tiltSign = 1f;
        bool ready;
        float phase, walkW, aimW, yaw, pitch, yawVel, pitchVel, jumpU = -1f, nextJump, orbitAngle;
        const float Duty = 0.55f;
        float noiseWait;          // seconds left until the noise sound plays again (counts down only while it is silent)
        bool soundsStopped;       // true once the robot has died

        // sounds through walls: one entry per Audio Source (volume + low pass filter get changed while a wall blocks the sound)
        class SoundMuffle
        {
            public AudioSource src;
            public AudioLowPassFilter lowPass;
            public float baseVolume;
        }
        readonly List<SoundMuffle> muffles = new List<SoundMuffle>();
        float muffleBlend;        // 0 = clear, 1 = muffled

        /// <summary>Direction the robot faces (world, flattened).</summary>
        public Vector3 Forward { get { return Vector3.ProjectOnPlane(transform.TransformDirection(lF), transform.up).normalized; } }
        /// <summary>Walking speed (m/s) at which the feet don't slide.</summary>
        public float WalkSpeed { get { return strideLength * stepsPerSecond / Duty; } }
        public bool IsJumping { get { return jumpU >= 0f; } }

        // ---- read-only values used by the FlatheadEnemy (the enemy brain that drives this controller)
        /// <summary>0..1 while a jump is playing (0-0.22 crouch, 0.22-0.78 in the air, 0.78-1 landing), -1 otherwise.</summary>
        public float JumpProgress { get { return jumpU; } }
        /// <summary>True once the rig (bones) has been found.</summary>
        public bool IsReady { get { return ready; } }
        /// <summary>Tip of the gun barrel (world).</summary>
        public Vector3 MuzzlePosition { get { return muzzle != null ? muzzle.t.position : transform.position; } }
        /// <summary>Pivot of the gun (world): where the turret "looks" from.</summary>
        public Vector3 TurretPosition { get { return tilt != null ? tilt.t.position : transform.position; } }
        /// <summary>Direction the gun barrel points (world).</summary>
        public Vector3 GunDirection
        {
            get
            {
                if (muzzle == null || tilt == null) return transform.forward;
                Vector3 d = muzzle.t.position - tilt.t.position;
                return d.sqrMagnitude > 1e-8f ? d.normalized : transform.forward;
            }
        }
        /// <summary>The turret bone that pans (Gun_Pan).</summary>
        public Transform TurretBone { get { return pan != null ? pan.t : null; } }

        void Awake() { DetectRig(); InitSounds(); }
        void Start() { InitSounds(); }       // again, in case an AudioSource with 'Play On Awake' started after Awake
        void Update() { UpdateSounds(); }

        List<RigBone> allList;

        /// <summary>Finds the bones and measures the rest pose. Safe to call more than once.</summary>
        public bool DetectRig()
        {
            if (ready) return true;
            Dictionary<string, Transform> map = new Dictionary<string, Transform>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true)) if (!map.ContainsKey(t.name)) map[t.name] = t;
            allList = new List<RigBone>();
            all = null;
            // small helper so Grab can append
            RigBone G(string n) { Transform t; if (!map.TryGetValue(n, out t)) { Debug.LogError("FlatheadController: bone '" + n + "' not found. Use the Flathead prefab / Flathead.fbx.", this); return null; } RigBone b = new RigBone(); b.t = t; b.restPos = t.localPosition; b.restRot = t.localRotation; allList.Add(b); return b; }
            root = G("Root"); body = G("Body"); pan = G("Gun_Pan"); tilt = G("Gun_Tilt"); muzzle = G("Gun_Muzzle");
            for (int i = 0; i < 4; i++)
            {
                upper[i] = G(LegNames[i] + "_Upper"); lower[i] = G(LegNames[i] + "_Lower"); foot[i] = G(LegNames[i] + "_Foot"); tip[i] = G(LegNames[i] + "_Tip");
            }
            if (root == null || body == null || pan == null || tilt == null || muzzle == null) return false;
            for (int i = 0; i < 4; i++) if (upper[i] == null || lower[i] == null || foot[i] == null || tip[i] == null) return false;

            // basis from the gun barrel (forward) - independent of how Unity converted the FBX axes
            Vector3 up = transform.up;
            Vector3 f = Vector3.ProjectOnPlane(muzzle.t.position - tilt.t.position, up);
            if (f.sqrMagnitude < 1e-8f) f = transform.forward;
            f.Normalize();
            lF = transform.InverseTransformDirection(f).normalized;
            lU = transform.InverseTransformDirection(up).normalized;
            lR = Vector3.Cross(lU, lF).normalized;          // physical right
            yawSign = Vector3.Dot(Quaternion.AngleAxis(10f, lU) * lF, lR) > 0f ? 1f : -1f;
            tiltSign = Vector3.Dot(Quaternion.AngleAxis(10f, lR) * lF, lU) > 0f ? 1f : -1f;

            for (int i = 0; i < 4; i++)
            {
                ankleRest[i] = transform.InverseTransformPoint(foot[i].t.position);
                tipOffset[i] = transform.InverseTransformPoint(tip[i].t.position) - ankleRest[i];
                footRestRoot[i] = Quaternion.Inverse(transform.rotation) * foot[i].t.rotation;
            }
            all = allList.ToArray();
            ready = true;
            return true;
        }

        static float SStep(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        static void TwoBoneIK(Transform upperT, Transform midT, Transform endT, Vector3 target, Vector3 pole)
        {
            Vector3 a = upperT.position, b = midT.position, c = endT.position;
            float l1 = (b - a).magnitude, l2 = (c - b).magnitude;
            Vector3 toT = target - a; float dist = toT.magnitude;
            if (dist < 1e-4f || l1 < 1e-4f) return;
            Vector3 dir = toT / dist;
            float d = Mathf.Clamp(dist, Mathf.Abs(l1 - l2) + 0.001f, l1 + l2 - 0.001f);
            float cosA = (l1 * l1 + d * d - l2 * l2) / (2f * l1 * d);
            float sinA = Mathf.Sqrt(Mathf.Max(0f, 1f - cosA * cosA));
            Vector3 pl = Vector3.ProjectOnPlane(pole, dir);
            if (pl.sqrMagnitude < 1e-6f) return;
            pl.Normalize();
            Vector3 newMid = a + dir * (l1 * cosA) + pl * (l1 * sinA);
            upperT.rotation = Quaternion.FromToRotation(b - a, newMid - a) * upperT.rotation;
            Vector3 m2 = midT.position, c2 = endT.position;
            midT.rotation = Quaternion.FromToRotation(c2 - m2, (a + dir * d) - m2) * midT.rotation;
        }

        [Header("Runtime Culling (set by CullingGroup)")]
        [Tooltip("When true, procedural leg IK and bone rotations are skipped while offscreen.")]
        public bool cullVisuals = false;

        void LateUpdate()
        {
            if (cullVisuals) return;
            if (!ready && !DetectRig()) return;
            float dt = Time.deltaTime;

            if (orbitTarget && target != null)
            {
                orbitAngle += orbitSpeed * dt;
                float a = orbitAngle * Mathf.Deg2Rad;
                Vector3 c = transform.position;
                target.position = c + new Vector3(Mathf.Cos(a) * orbitRadius, orbitHeight, Mathf.Sin(a) * orbitRadius);
            }

            for (int i = 0; i < all.Length; i++) { all[i].t.localPosition = all[i].restPos; all[i].t.localRotation = all[i].restRot; }

            // ---------- state
            walkW = Mathf.MoveTowards(walkW, walking ? 1f : 0f, dt / 0.25f);
            if (walkW > 0f) phase = Mathf.Repeat(phase + stepsPerSecond * dt, 1f);

            if (jumpU < 0f)
            {
                if (jump && Time.time >= nextJump) jumpU = 0f;
            }
            else
            {
                jumpU += dt / Mathf.Max(0.2f, jumpDuration);
                if (jumpU >= 1f) { jumpU = -1f; nextJump = Time.time + jumpPause; }
            }

            float jw = jumpU >= 0f ? 1f : 0f;
            float dirRad = walkDirection * Mathf.Deg2Rad;
            Vector3 d3 = lF * Mathf.Cos(dirRad) + lR * Mathf.Sin(dirRad);       // travel direction, root-local

            // ---------- body: bob, lean, jump height
            float bodyY = 0f, air = 0f;
            if (walkW > 0f && jw == 0f) bodyY += bodyBob * Mathf.Cos(4f * Mathf.PI * phase) * walkW;
            if (jw > 0f)
            {
                float u = jumpU;
                float b;
                if (u < 0.22f) b = -crouchDepth * SStep(u / 0.22f);
                else if (u < 0.78f) { float k = (u - 0.22f) / 0.56f; b = Mathf.Max(-crouchDepth, -crouchDepth * (1f - SStep(Mathf.Min(1f, k * 3f))) + jumpHeight * 4f * k * (1f - k)); }
                else b = -crouchDepth * SStep((u - 0.78f) / 0.22f) * 0.9f;
                bodyY += b; air = b;
            }
            body.t.localPosition = body.restPos + lU * bodyY;
            if (walkW > 0f && bodyLean > 0f)
            {
                Quaternion lean = Quaternion.FromToRotation(lU, (lU + d3 * Mathf.Tan(bodyLean * Mathf.Deg2Rad * walkW)).normalized);
                body.t.localRotation = lean * body.restRot;
            }

            // ---------- turret aim (computed in the body's frame, so body lean doesn't matter)
            aimW = Mathf.MoveTowards(aimW, (aimAtTarget && target != null) ? 1f : 0f, dt / 0.25f);
            float yawGoal = 0f, pitchGoal = 0f;
            if (aimAtTarget && target != null)
            {
                Vector3 tl = body.t.InverseTransformPoint(target.position) - body.t.InverseTransformPoint(tilt.t.position);
                float ax = Vector3.Dot(tl, lR), ay = Vector3.Dot(tl, lU), az = Vector3.Dot(tl, lF);
                yawGoal = Mathf.Atan2(ax, az) * Mathf.Rad2Deg;
                pitchGoal = Mathf.Clamp(Mathf.Atan2(ay, Mathf.Sqrt(ax * ax + az * az)) * Mathf.Rad2Deg, tiltMin, tiltMax);
            }
            float sm = 0.08f;
            yaw = Mathf.SmoothDampAngle(yaw, yawGoal, ref yawVel, sm, turretSpeed);
            pitch = Mathf.SmoothDamp(pitch, pitchGoal, ref pitchVel, sm, turretSpeed);
            pan.t.localRotation = Quaternion.AngleAxis(yaw * yawSign, lU) * pan.restRot;
            tilt.t.localRotation = Quaternion.AngleAxis(pitch * tiltSign, lR) * tilt.restRot;

            // ---------- legs (two-bone IK to feet that follow the gait / jump)
            Vector3 pole = transform.TransformDirection(lU);
            for (int i = 0; i < 4; i++)
            {
                Vector3 off = Vector3.zero;
                if (jw > 0f)
                {
                    float lift = Mathf.Max(0f, air - 0.17f);
                    float tk = Mathf.Min(1f, lift / 0.12f) * 0.35f;
                    Vector3 hip = transform.InverseTransformPoint(upper[i].t.position);       // current hip (body moved)
                    Vector3 toHip = Vector3.ProjectOnPlane(hip - ankleRest[i], lU);
                    off = lU * lift + toHip * (tk * 0.5f);
                }
                else if (walkW > 0f)
                {
                    float ph = Mathf.Repeat(phase + (i >= 2 ? 0.5f : 0f), 1f);
                    float s, y = 0f;
                    if (ph < Duty) s = 0.5f - ph / Duty;
                    else { float q = (ph - Duty) / (1f - Duty); s = -0.5f + SStep(q); y = stepHeight * Mathf.Sin(Mathf.PI * q); }
                    off = (d3 * (strideLength * s) + lU * y) * walkW;
                }
                Vector3 targetWorld = transform.TransformPoint(ankleRest[i] + off);
                TwoBoneIK(upper[i].t, lower[i].t, foot[i].t, targetWorld, pole);
                foot[i].t.rotation = transform.rotation * footRestRoot[i];
            }

            if (moveWhileWalking && walkW > 0f && jw == 0f)
                transform.position += transform.TransformDirection(d3) * (WalkSpeed * walkW * dt);
        }

        /// <summary>Puts the Normal Material on every mesh of the robot (all material slots). Called once by the FlatheadEnemy when the robots malfunction.</summary>
        public void ApplyNormalMaterial()
        {
            if (normalMaterial == null)
            {
                Debug.LogWarning("FlatheadController: no Normal Material is assigned on the Flathead prefab, so this robot keeps its stealth material after the virus upload.", this);
                return;
            }
            Renderer[] rs = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;   // not particles, trails, lines
                int slots = r.sharedMaterials.Length;
                Material[] mats = new Material[slots];
                for (int k = 0; k < slots; k++) mats[k] = normalMaterial;
                r.sharedMaterials = mats;
            }
        }

        // ------------------------------------------------------------------ sound

        // Every sound starts silent. Noise, fire and destruction play once per Play, the walking sound loops while the legs walk.
        void InitSounds()
        {
            soundsStopped = false;
            if (noiseSource != null) { noiseSource.playOnAwake = false; noiseSource.loop = false; noiseSource.Stop(); }
            if (walkSource != null) { walkSource.playOnAwake = false; walkSource.loop = true; walkSource.Stop(); }
            if (fireSource != null) { fireSource.playOnAwake = false; fireSource.loop = false; fireSource.Stop(); }
            if (destructionSource != null) { destructionSource.playOnAwake = false; destructionSource.loop = false; destructionSource.Stop(); }
            noiseWait = noiseInterval;
            SetupMuffle(noiseSource);
            SetupMuffle(walkSource);
            SetupMuffle(fireSource);
            SetupMuffle(destructionSource);
        }

        // each Audio Source gets a low pass filter (off while the sound is clear) and remembers its normal volume
        void SetupMuffle(AudioSource s)
        {
            if (s == null) return;
            for (int i = 0; i < muffles.Count; i++)
                if (muffles[i].src == s) return;                     // same source dragged into two slots, or InitSounds ran twice

            SoundMuffle m = new SoundMuffle();
            m.src = s;
            m.baseVolume = s.volume;
            m.lowPass = s.GetComponent<AudioLowPassFilter>();
            if (m.lowPass == null) m.lowPass = s.gameObject.AddComponent<AudioLowPassFilter>();
            m.lowPass.cutoffFrequency = 22000f;
            m.lowPass.enabled = false;
            muffles.Add(m);
        }

        /// <summary>
        /// Called by the FlatheadEnemy. wallBlocked = a wall is between the robot and the player: all sounds fade to quieter + muffled
        /// (or back to clear). snap = no fade, jump straight to the final state (used at the moment of death).
        /// </summary>
        public void SetSoundsBlocked(bool wallBlocked, float dt, bool snap = false)
        {
            if (muffles.Count == 0) return;

            if (snap) muffleBlend = wallBlocked ? 1f : 0f;
            else muffleBlend = Mathf.MoveTowards(muffleBlend, wallBlocked ? 1f : 0f, dt / Mathf.Max(0.01f, muffleFadeTime));
            float cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(22000f), Mathf.Log(muffledCutoff), muffleBlend));   // fades evenly to the ear
            float vol = Mathf.Lerp(1f, muffledVolume, muffleBlend);
            for (int i = 0; i < muffles.Count; i++)
            {
                SoundMuffle m = muffles[i];
                if (m.src == null || m.lowPass == null) continue;
                m.src.volume = m.baseVolume * vol;
                m.lowPass.enabled = muffleBlend > 0.001f;
                m.lowPass.cutoffFrequency = cutoff;
            }
        }

        void UpdateSounds()
        {
            if (soundsStopped) return;

            // NOISE: always, whatever the robot does. The wait only counts down after the sound has fully played.
            if (noiseSource != null)
            {
                if (noiseSource.isPlaying) noiseWait = noiseInterval;
                else
                {
                    noiseWait -= Time.deltaTime;
                    if (noiseWait <= 0f)
                    {
                        noiseSource.Play();
                        noiseWait = noiseInterval;
                    }
                }
            }

            // WALKING: only while the legs walk. Not while standing still, not during a jump (crouch, air, landing).
            if (walkSource != null)
            {
                bool walkingNow = walking && !jump && !IsJumping;
                if (walkingNow && !walkSource.isPlaying) walkSource.Play();
                else if (!walkingNow && walkSource.isPlaying) walkSource.Stop();
            }
        }

        /// <summary>Called by the FlatheadEnemy every time the robot shoots a bolt.</summary>
        public void PlayFireSound()
        {
            if (fireSource != null && fireSource.clip != null) fireSource.PlayOneShot(fireSource.clip);   // one shot per bolt, fast fire doesn't cut the last one off
        }

        /// <summary>Called by the FlatheadEnemy when the robot dies: noise and walking stop for good, the destruction sound plays once.</summary>
        public void PlayDeathSounds()
        {
            soundsStopped = true;
            if (noiseSource != null) noiseSource.Stop();
            if (walkSource != null) walkSource.Stop();
            if (destructionSource != null) destructionSource.Play();
        }

        // for your own scripts / UI buttons
        public void SetWalking(bool v) { walking = v; }
        public void SetAiming(bool v) { aimAtTarget = v; }
        public void SetJump(bool v) { jump = v; }
    }
}