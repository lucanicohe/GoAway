using System.Collections;
using UnityEngine;

public class Doors : MonoBehaviour, IBlasterDamageable
{
    public Transform leftDoor;
    public Transform rightDoor;
    public float duration = 1f;

    [Header("Local Y positions (defaults = your old values)")]
    public float leftClosedY = 0.229f;
    public float leftOpenY = 0.24009f;
    public float rightClosedY = 0.21098f;
    public float rightOpenY = 0.19974f;

    bool opened;

    void Awake()
    {
        opened = false;

        if (leftDoor == null || rightDoor == null)
            Debug.LogWarning("[Doors] Left Door or Right Door is empty on \"" + name + "\". Assign both in the Inspector.", this);
        else
            Debug.Log("[Doors] ready on \"" + name + "\" -> left: \"" + leftDoor.name + "\", right: \"" + rightDoor.name + "\"", this);

        if (leftDoor != null)
        {
            Vector3 p = leftDoor.localPosition;
            p.y = leftClosedY;
            leftDoor.localPosition = p;
        }

        if (rightDoor != null)
        {
            Vector3 p = rightDoor.localPosition;
            p.y = rightClosedY;
            rightDoor.localPosition = p;
        }
    }

    public void TakeBlasterDamage(BlasterHitInfo hit)
    {
        if (opened) return;
        Debug.Log("[Doors] \"" + name + "\" was shot.", this);
        Open();
    }

    [ContextMenu("Test: Open now")]
    void TestOpen()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[Doors] Press Play first, then use the test.", this);
            return;
        }
        Open();
    }

    void Open()
    {
        if (opened) return;

        if (leftDoor == null || rightDoor == null)
        {
            Debug.LogWarning("[Doors] Left Door or Right Door is empty on \"" + name + "\".", this);
            return;
        }

        opened = true;
        Report(leftDoor);
        Report(rightDoor);
        StartCoroutine(Move());
    }

    void Report(Transform leaf)
    {
        Animator anim = leaf.GetComponentInParent<Animator>();
        Renderer r = leaf.GetComponentInChildren<Renderer>();
        Debug.Log("[Doors] " + leaf.name
            + ": renderer=" + (r != null ? r.name : "NONE")
            + ", partOfStaticBatch=" + (r != null && r.isPartOfStaticBatch)
            + ", animator=" + (anim != null ? anim.gameObject.name : "none")
            + ", localPos=" + leaf.localPosition.ToString("F3"), leaf);
    }

    IEnumerator Move()
    {
        Vector3 leftStart = leftDoor.localPosition;
        Vector3 rightStart = rightDoor.localPosition;

        Vector3 leftEnd = leftStart;
        leftEnd.y = leftOpenY;

        Vector3 rightEnd = rightStart;
        rightEnd.y = rightOpenY;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            leftDoor.localPosition = Vector3.Lerp(leftStart, leftEnd, t);
            rightDoor.localPosition = Vector3.Lerp(rightStart, rightEnd, t);
            yield return null;
        }

        // one frame later: did something move the leaves back?
        yield return null;
        Debug.Log("[Doors] after opening: left local y = " + leftDoor.localPosition.y.ToString("F4")
            + " (wanted " + leftOpenY + "), right local y = " + rightDoor.localPosition.y.ToString("F4")
            + " (wanted " + rightOpenY + ")", this);
    }
}