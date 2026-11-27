using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using System.Collections;

public class intro : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button endButton;

    [Header("Scene")]
    [SerializeField] private string sceneName = "map";

    [Header("Hover Effect")]
    [SerializeField] private float hoverScale = 1.1f;

    [Header("Intro Images")]
    [SerializeField] private Image[] introImages;   // drag your 3 images here in order
    [SerializeField] private float fadeDuration = 1f;

    void Start()
    {
        // make sure they start invisible
        foreach (var img in introImages)
        {
            if (img != null)
            {
                Color c = img.color;
                c.a = 0f;
                img.color = c;
            }
        }

        startButton.onClick.AddListener(StartGame);
        endButton.onClick.AddListener(QuitGame);

        startButton.gameObject.AddComponent<ButtonHover>().SetScale(hoverScale);
        endButton.gameObject.AddComponent<ButtonHover>().SetScale(hoverScale);

        StartCoroutine(PlayIntroSequence());
    }

    IEnumerator PlayIntroSequence()
    {
        foreach (var img in introImages)
        {
            if (img == null) continue;

            float t = 0f;
            Color c = img.color;

            while (t < fadeDuration)
            {
                t += Time.deltaTime;
                c.a = Mathf.Clamp01(t / fadeDuration);
                img.color = c;
                yield return null;
            }

            // force final alpha just in case
            c.a = 1f;
            img.color = c;
        }
    }

    void StartGame()
    {
        SceneManager.LoadScene(sceneName);
    }

    void QuitGame()
    {
        Application.Quit();
        Debug.Log("Game Quit");
    }
}

public class ButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Vector3 originalScale;
    private float hoverScale = 1.1f;

    public void SetScale(float scale)
    {
        hoverScale = scale;
    }

    void Awake()
    {
        originalScale = transform.localScale;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        transform.localScale = originalScale * hoverScale;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        transform.localScale = originalScale;
    }
}