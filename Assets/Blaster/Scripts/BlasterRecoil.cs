using UnityEngine;

/// <summary>
/// Gun recoil. Every direct child of "View Model Root" (PlayerObjects: gun, hands, sleeves, flashlight, muzzle point)
/// is kicked back and up as one rigid group and then springs back to where it was.
/// The existing player scripts are not touched: WalkSprintRotationSway rotates PlayerObjects itself,
/// this script only moves the children, so both work together.
/// </summary>
[DefaultExecutionOrder(100)]
[AddComponentMenu("Blaster/Blaster Recoil")]
public class BlasterRecoil : MonoBehaviour
{
    [Header("What moves")]
    [Tooltip("The object whose children are kicked. Empty = the parent of this object (PlayerObjects).")]
    [SerializeField] private Transform viewModelRoot;

    [Header("Kick per shot")]
    [Tooltip("Metres the gun is pushed back towards the player.")]
    [SerializeField] private float kickBack = 0.05f;
    [Tooltip("Metres the gun jumps up.")]
    [SerializeField] private float kickUp = 0.008f;
    [Tooltip("Degrees the muzzle climbs.")]
    [SerializeField] private float pitchKick = 2.5f;
    [Tooltip("Random sideways turn, +- degrees.")]
    [SerializeField] private float yawKick = 0.7f;
    [Tooltip("Random twist, +- degrees.")]
    [SerializeField] private float rollKick = 1f;

    [Header("Limits (when firing fast)")]
    [SerializeField] private float maxBack = 0.14f;
    [SerializeField] private float maxPitch = 7f;

    [Header("Spring (how it returns)")]
    [Tooltip("Higher = snaps back faster.")]
    [SerializeField] private float stiffness = 240f;
    [Tooltip("Higher = less wobble.")]
    [SerializeField] private float damping = 26f;
    [Tooltip("The gun group turns around this point (in PlayerObjects space), roughly the shoulder.")]
    [SerializeField] private Vector3 pivot = new Vector3(0.1f, -0.4f, 0f);

    [Header("Camera")]
    [Tooltip("Small camera bump per shot through the existing FirstPersonShake. 0 = off.")]
    [SerializeField] private float cameraShake = 0.04f;

    private Transform[] targets;
    private Vector3[] basePos;
    private Quaternion[] baseRot;

    private Vector3 pos, posVel;
    private Vector3 rot, rotVel;
    private bool moving;

    private void Start()
    {
        CaptureBasePose();
    }

    [ContextMenu("Recapture base pose")]
    public void CaptureBasePose()
    {
        if (viewModelRoot == null)
            viewModelRoot = transform.parent;
        if (viewModelRoot == null)
            return;

        int count = viewModelRoot.childCount;
        targets = new Transform[count];
        basePos = new Vector3[count];
        baseRot = new Quaternion[count];

        for (int i = 0; i < count; i++)
        {
            Transform c = viewModelRoot.GetChild(i);
            if (c == transform)
                continue;
            targets[i] = c;
            basePos[i] = c.localPosition;
            baseRot[i] = c.localRotation;
        }
    }

    /// <summary>Call once per shot.</summary>
    public void Kick()
    {
        pos += new Vector3(Random.Range(-0.004f, 0.004f), kickUp, -kickBack);
        pos.z = Mathf.Max(pos.z, -maxBack);

        rot += new Vector3(-pitchKick, Random.Range(-yawKick, yawKick), Random.Range(-rollKick, rollKick));
        rot.x = Mathf.Max(rot.x, -maxPitch);

        moving = true;

        if (cameraShake > 0f && FPCam.FirstPersonShake.Instance != null)
            FPCam.FirstPersonShake.Instance.Shake(cameraShake);
    }

    private void LateUpdate()
    {
        if (!moving || targets == null)
            return;

        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        if (dt <= 0f)
            return;

        posVel += (-stiffness * pos - damping * posVel) * dt;
        pos += posVel * dt;
        rotVel += (-stiffness * rot - damping * rotVel) * dt;
        rot += rotVel * dt;

        bool settled = pos.sqrMagnitude < 1e-8f && posVel.sqrMagnitude < 1e-6f &&
                       rot.sqrMagnitude < 1e-5f && rotVel.sqrMagnitude < 1e-3f;
        if (settled)
        {
            pos = Vector3.zero; posVel = Vector3.zero;
            rot = Vector3.zero; rotVel = Vector3.zero;
            moving = false;
        }

        Quaternion r = Quaternion.Euler(rot);
        for (int i = 0; i < targets.Length; i++)
        {
            Transform t = targets[i];
            if (t == null)
                continue;
            t.localPosition = r * (basePos[i] - pivot) + pivot + pos;
            t.localRotation = r * baseRot[i];
        }
    }
}
