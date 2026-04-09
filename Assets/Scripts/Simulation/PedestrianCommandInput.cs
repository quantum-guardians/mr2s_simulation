using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 좌클릭: 보행자 선택. 우클릭: 바닥 목표 지점 → 선택된 유닛에 A* 경로 부여.
/// </summary>
public class PedestrianCommandInput : MonoBehaviour
{
    [SerializeField] PedestrianCrowdSim crowdSim;
    [SerializeField] Camera targetCamera;
    [SerializeField] float rayMaxDistance = 250f;
    [Tooltip("보행자 레이캐스트에 사용 (비우면 모든 레이어)")]
    [SerializeField] LayerMask pedestrianLayers = ~0;
    [Tooltip("지면(Plane) 레이캐스트 — 비우면 수학적 평면(y=RoadSurfaceY) 사용")]
    [SerializeField] LayerMask groundLayers;

    int? _selectedAgentId;

    void Awake()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    void Update()
    {
        if (crowdSim == null || targetCamera == null)
            return;

        if (_selectedAgentId.HasValue && !crowdSim.HasAgentId(_selectedAgentId.Value))
        {
            _selectedAgentId = null;
            crowdSim.SetSelectedAgentId(null);
        }

        var mouse = Mouse.current;
        if (mouse == null)
            return;

        if (mouse.leftButton.wasPressedThisFrame)
            TrySelectPedestrian();

        if (mouse.rightButton.wasPressedThisFrame)
            TryCommandMoveToGround();
    }

    void TrySelectPedestrian()
    {
        var ray = targetCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        var hits = Physics.RaycastAll(ray, rayMaxDistance, pedestrianLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        PedestrianUnit bestUnit = null;
        foreach (var h in hits)
        {
            var u = h.collider.GetComponentInParent<PedestrianUnit>();
            if (u == null) continue;
            if (h.distance >= best) continue;
            best = h.distance;
            bestUnit = u;
        }

        if (bestUnit != null)
        {
            _selectedAgentId = bestUnit.AgentId;
            crowdSim.SetSelectedAgentId(_selectedAgentId);
        }
        else
        {
            _selectedAgentId = null;
            crowdSim.SetSelectedAgentId(null);
        }
    }

    void TryCommandMoveToGround()
    {
        if (!_selectedAgentId.HasValue || crowdSim == null)
            return;

        var ray = targetCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!TryGetGroundPointIgnoringPedestrians(ray, out var hitPoint))
            return;

        crowdSim.TryOrderMoveToWorldPoint(_selectedAgentId.Value, hitPoint);
    }

    bool TryGetGroundPointIgnoringPedestrians(Ray ray, out Vector3 hitPoint)
    {
        var hits = Physics.RaycastAll(ray, rayMaxDistance, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        if (hits != null && hits.Length > 1)
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        if (hits != null)
        {
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                if (h.collider.GetComponentInParent<PedestrianUnit>() != null)
                    continue;
                if (groundLayers.value != 0 && ((1 << h.collider.gameObject.layer) & groundLayers.value) == 0)
                    continue;
                hitPoint = h.point;
                return true;
            }
        }

        return TryIntersectHorizontalPlane(ray, crowdSim.RoadSurfaceWorldY, out hitPoint);
    }

    static bool TryIntersectHorizontalPlane(Ray ray, float planeY, out Vector3 point)
    {
        var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        point = default;
        if (!plane.Raycast(ray, out var enter))
            return false;
        point = ray.GetPoint(enter);
        return true;
    }
}
