using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GraphInputUI : MonoBehaviour
{
    [SerializeField] GraphManager graphManager;
    [SerializeField] InputField edgeInput;
    [SerializeField] Button drawGraphButton;
    [SerializeField] Button buildButton;

    void Reset()
    {
        graphManager = FindAnyObjectByType<GraphManager>();
    }

    void Awake()
    {
        if (edgeInput == null) return;
        edgeInput.lineType = InputField.LineType.MultiLineNewline;
    }

    void OnEnable()
    {
        if (drawGraphButton != null) drawGraphButton.onClick.AddListener(OnBuildClicked);
        if (buildButton != null) buildButton.onClick.AddListener(OnConfirmClicked);
    }

    void OnDisable()
    {
        if (drawGraphButton != null) drawGraphButton.onClick.RemoveListener(OnBuildClicked);
        if (buildButton != null) buildButton.onClick.RemoveListener(OnConfirmClicked);
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

    void OnConfirmClicked()
    {
        if (graphManager == null)
        {
            Debug.LogError("[GraphInputUI] GraphManager 참조가 없습니다.");
            return;
        }

        graphManager.ConfirmLayoutAndBuildRoads();
    }
}
