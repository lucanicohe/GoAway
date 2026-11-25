using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

public class intro : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button endButton;

    [Header("Scene")]
    [SerializeField] private string sceneName = "map";

    [Header("Hover Effect")]
    [SerializeField] private float hoverScale = 1.1f;

    void Start()
    {
        startButton.onClick.AddListener(StartGame);
        endButton.onClick.AddListener(QuitGame);

        startButton.gameObject.AddComponent<ButtonHover>().SetScale(hoverScale);
        endButton.gameObject.AddComponent<ButtonHover>().SetScale(hoverScale);
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