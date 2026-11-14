
using System.Collections;
using UnityEngine;

public class MuzzleFlash : MonoBehaviour
{
    [Header("Flash Objects")]
    [SerializeField] private GameObject flashParent;
    [SerializeField] private SpriteRenderer[] flashRenderers;

    [Header("Sprite Variations")]
    [SerializeField] private Sprite[] flashSprites;

    [Header("Timing")]
    [SerializeField] private float duration = 0.06f;

    [Header("Random X Rotation")]
    [SerializeField] private float minXRotation = -20f;
    [SerializeField] private float maxXRotation = 20f;

    private Quaternion[] originalRotations;
    private Coroutine flashRoutine;

    private void Awake()
    {
        originalRotations = new Quaternion[flashRenderers.Length];

        // Remember each sprite's original rotation
        for (int i = 0; i < flashRenderers.Length; i++)
        {
            if (flashRenderers[i] == null)
                continue;

            originalRotations[i] =
                flashRenderers[i].transform.localRotation;
        }

        if (flashParent != null)
            flashParent.SetActive(false);
    }

    public void Fire()
    {
        if (flashParent == null ||
            flashSprites == null ||
            flashSprites.Length == 0)
            return;

        // Randomize all three independently
        for (int i = 0; i < flashRenderers.Length; i++)
        {
            SpriteRenderer sr = flashRenderers[i];

            if (sr == null)
                continue;

            // Random sprite selection
            sr.sprite = flashSprites[
                Random.Range(0, flashSprites.Length)
            ];

            // Random X-axis rotation
            float xAngle = Random.Range(
                minXRotation,
                maxXRotation
            );

            sr.transform.localRotation =
                originalRotations[i] *
                Quaternion.Euler(xAngle, 0f, 0f);
        }

        // Restart flash if shooting rapidly
        if (flashRoutine != null)
            StopCoroutine(flashRoutine);

        flashRoutine = StartCoroutine(ShowFlash());
    }

    private IEnumerator ShowFlash()
    {
        flashParent.SetActive(true);

        yield return new WaitForSeconds(duration);

        flashParent.SetActive(false);
        flashRoutine = null;
    }
}
