using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GraphInputUI : MonoBehaviour
{
    [SerializeField] GraphManager graphManager;
    [SerializeField] InputField edgeInput;
    [SerializeField] Button drawGraphButton;
    [Tooltip("Small-world API로 방향 그래프 전송·응답 시각화")]
    [SerializeField] Button drawNodeButton;
    [SerializeField] Button buildButton;
    [Tooltip("Delaunay 삼각분할 평면 그래프 자동 생성")]
    [SerializeField] Button autoGenerateButton;

    void Reset()
    {
        graphManager = FindAnyObjectByType<GraphManager>();
    }

    void Awake()
    {
        if (edgeInput != null)
            edgeInput.lineType = InputField.LineType.MultiLineNewline;

        EnsureAutoGenerateButton();
    }

    void OnEnable()
    {
        if (drawGraphButton != null) drawGraphButton.onClick.AddListener(OnBuildClicked);
        if (drawNodeButton != null) drawNodeButton.onClick.AddListener(OnDrawNodeClicked);
        if (buildButton != null) buildButton.onClick.AddListener(OnConfirmClicked);
        if (autoGenerateButton != null) autoGenerateButton.onClick.AddListener(OnAutoGenerateClicked);
    }

    void OnDisable()
    {
        if (drawGraphButton != null) drawGraphButton.onClick.RemoveListener(OnBuildClicked);
        if (drawNodeButton != null) drawNodeButton.onClick.RemoveListener(OnDrawNodeClicked);
        if (buildButton != null) buildButton.onClick.RemoveListener(OnConfirmClicked);
        if (autoGenerateButton != null) autoGenerateButton.onClick.RemoveListener(OnAutoGenerateClicked);
    }

    void OnBuildClicked()
    {
        if (graphManager == null)
        {
            Debug.LogError("[GraphInputUI] GraphManager 참조가 없습니다.");
            return;
        }

        var text = edgeInput != null ? edgeInput.text : string.Empty;
        graphManager.BuildGraphFromInput(text);
    }

    void OnDrawNodeClicked()
    {
        if (graphManager == null)
        {
            Debug.LogError("[GraphInputUI] GraphManager 참조가 없습니다.");
            return;
        }

        var text = edgeInput != null ? edgeInput.text : string.Empty;
        graphManager.RequestSmallWorldFromInput(text);
    }

    void OnConfirmClicked()
    {
        if (graphManager == null)
        {
            Debug.LogError("[GraphInputUI] GraphManager 참조가 없습니다.");
            return;
        }

        graphManager.ConfirmLayoutAndBuildRoads();
    }

    void OnAutoGenerateClicked()
    {
        if (graphManager == null)
        {
            Debug.LogError("[GraphInputUI] GraphManager 참조가 없습니다.");
            return;
        }

        graphManager.GeneratePlanarGraph();
    }

    void EnsureAutoGenerateButton()
    {
        if (autoGenerateButton != null)
            return;

        var template = drawGraphButton != null ? drawGraphButton : drawNodeButton != null ? drawNodeButton : buildButton;
        if (template == null)
            return;

        var clone = Instantiate(template.gameObject, template.transform.parent);
        clone.name = "ButtonAutoGenerateGraph";
        clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);

        autoGenerateButton = clone.GetComponent<Button>();
        if (autoGenerateButton != null)
            autoGenerateButton.onClick.RemoveAllListeners();

        var rect = clone.GetComponent<RectTransform>();
        if (rect != null)
            rect.anchoredPosition += new Vector2(rect.sizeDelta.x + 10f, 0f);

        var text = clone.GetComponentInChildren<Text>();
        if (text != null)
            text.text = "Random graph";

        var tmp = clone.GetComponentInChildren<TMP_Text>();
        if (tmp != null)
            tmp.text = "Random graph";
    }
}
