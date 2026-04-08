using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 좌클릭: 보행자(PedestrianUnit) 선택. 우클릭: 바닥 목표 지점 — 선택된 유닛만 A* 경로로 이동.
/// </summary>
public class PedestrianNavInput : MonoBehaviour
{
    [SerializeField] PedestrianCrowdSim crowdSim;
    [SerializeField] GraphManager graphManager;
    [SerializeField] Camera targetCamera;
    [SerializeField] LayerMask pedestrianLayers = ~0;
    [SerializeField] LayerMask groundLayers = ~0;
    [SerializeField] float rayMaxDistance = 250f;

    void Reset()
    {
        crowdSim = FindAnyObjectByType<PedestrianCrowdSim>();
        graphManager = FindAnyObjectByType<GraphManager>();
        targetCamera = Camera.main;
    }

    void Update()
    {
        if (crowdSim == null) return;
        var cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        var ray = cam.ScreenPointToRay(mouse.position.ReadValue());

        if (mouse.rightButton.wasPressedThisFrame)
        {
            if (Physics.Raycast(ray, out var gh, rayMaxDistance, groundLayers, QueryTriggerInteraction.Ignore))
                crowdSim.TryNavigateSelectedTo(gh.point);
            return;
        }

        if (!mouse.leftButton.wasPressedThisFrame) return;

        var hits = Physics.RaycastAll(ray, rayMaxDistance, pedestrianLayers, QueryTriggerInteraction.Ignore);
        PedestrianUnit best = null;
        var bestD = float.MaxValue;
        foreach (var h in hits)
        {
            var u = h.collider.GetComponentInParent<PedestrianUnit>();
            if (u == null) continue;
            if (h.distance >= bestD) continue;
            bestD = h.distance;
            best = u;
        }

        crowdSim.SetSelectedUnit(best);
    }
}
