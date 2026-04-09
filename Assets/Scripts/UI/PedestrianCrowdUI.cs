using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보행자 수 표시 및 +/- (또는 ◀ ▶) 버튼으로 한 명씩 추가·제거.
/// </summary>
public class PedestrianCrowdUI : MonoBehaviour
{
    [SerializeField] PedestrianCrowdSim crowdSim;
    [SerializeField] Text countLabel;
    [Tooltip("비우면 countLabel만 사용")]
    [SerializeField] TMPro.TextMeshProUGUI countLabelTmp;
    [SerializeField] Button addPedestrianButton;
    [SerializeField] Button removePedestrianButton;

    void Reset()
    {
        crowdSim = FindAnyObjectByType<PedestrianCrowdSim>();
    }

    void OnEnable()
    {
        if (addPedestrianButton != null) addPedestrianButton.onClick.AddListener(OnAdd);
        if (removePedestrianButton != null) removePedestrianButton.onClick.AddListener(OnRemove);
    }

    void OnDisable()
    {
        if (addPedestrianButton != null) addPedestrianButton.onClick.RemoveListener(OnAdd);
        if (removePedestrianButton != null) removePedestrianButton.onClick.RemoveListener(OnRemove);
    }

    void Update()
    {
        if (crowdSim == null) return;
        var s = crowdSim.LivingCount.ToString();
        if (countLabel != null) countLabel.text = s;
        if (countLabelTmp != null) countLabelTmp.text = s;
    }

    void OnAdd() => crowdSim?.AddOne();

    void OnRemove() => crowdSim?.RemoveOne();
}
