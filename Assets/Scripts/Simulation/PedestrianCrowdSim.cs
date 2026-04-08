using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 도로 구간을 따라 이동하는 원통형 보행자. 국소 밀도(이웃 수)가 임계를 넘으면 제거됩니다.
/// </summary>
public class PedestrianCrowdSim : MonoBehaviour
{
    [SerializeField] GraphVisualizer visualizer;
    [SerializeField] Transform pedestriansRoot;

    [Header("보행")]
    [SerializeField] float walkSpeed = 1.15f;
    [SerializeField] float lateralWander = 0.65f;
    [SerializeField] float cylinderRadiusXZ = 0.11f;
    [SerializeField] float cylinderHeight = 0.52f;
    [SerializeField] Color pedestrianColor = new(0.92f, 0.88f, 0.82f, 1f);

    [Header("압력(밀집 사망)")]
    [SerializeField] float pressureRadius = 0.42f;
    [Tooltip("이 반경 안의 다른 사람 수가 이 값 이상이면 사망 처리")]
    [SerializeField] int pressureNeighborDeathThreshold = 5;

    readonly List<WalkSegment> _segments = new();
    readonly Dictionary<int, List<int>> _outgoing = new();
    readonly List<PedestrianAgent> _agents = new();

    Material _pedestrianMaterial;
    float _groundY;
    float _walkHalfWidth;

    struct PedestrianAgent
    {
        public Transform Transform;
        public int SegmentIndex;
        public float T;
        public float Lateral;
    }

    void Awake()
    {
        EnsurePedestrianMaterial();
        EnsureRoot();
    }

    void OnDestroy()
    {
        if (_pedestrianMaterial != null)
            Destroy(_pedestrianMaterial);
    }

    void EnsureRoot()
    {
        if (pedestriansRoot != null) return;
        var go = new GameObject("Pedestrians");
        go.transform.SetParent(transform, false);
        pedestriansRoot = go.transform;
    }

    void EnsurePedestrianMaterial()
    {
        if (_pedestrianMaterial != null) return;
        var shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard")
                     ?? Shader.Find("Unlit/Color");
        if (shader == null) return;
        _pedestrianMaterial = new Material(shader);
        if (_pedestrianMaterial.HasProperty(Shader.PropertyToID("_BaseColor")))
            _pedestrianMaterial.SetColor(Shader.PropertyToID("_BaseColor"), pedestrianColor);
        else if (_pedestrianMaterial.HasProperty(Shader.PropertyToID("_Color")))
            _pedestrianMaterial.SetColor(Shader.PropertyToID("_Color"), pedestrianColor);
        else
            _pedestrianMaterial.color = pedestrianColor;
    }

    public int LivingCount => _agents.Count;

    public void ClearAllAgents()
    {
        for (var i = 0; i < _agents.Count; i++)
        {
            var tr = _agents[i].Transform;
            if (tr != null) Destroy(tr.gameObject);
        }

        _agents.Clear();
        _segments.Clear();
        _outgoing.Clear();
    }

    public void OnRoadsBuilt(GraphData graph, float groundY)
    {
        if (visualizer == null || graph == null)
        {
            ClearAllAgents();
            return;
        }

        _groundY = groundY;
        _walkHalfWidth = Mathf.Max(0.05f, visualizer.GetStreetWalkHalfWidth() - cylinderRadiusXZ);
        visualizer.BuildWalkNetwork(graph, groundY, _segments, _outgoing);

        for (var i = _agents.Count - 1; i >= 0; i--)
            DestroyAgentAt(i);
    }

    public void AddOne()
    {
        if (_segments.Count == 0)
        {
            Debug.LogWarning("[PedestrianCrowdSim] 도로가 없습니다. 먼저 그래프를 확정·빌드하세요.");
            return;
        }

        EnsurePedestrianMaterial();
        EnsureRoot();

        var si = Random.Range(0, _segments.Count);
        var seg = _segments[si];
        var t = Random.Range(0.05f, 0.95f);
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = $"Pedestrian_{_agents.Count}";
        go.transform.SetParent(pedestriansRoot, false);
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = new Vector3(cylinderRadiusXZ * 2f, cylinderHeight * 0.5f, cylinderRadiusXZ * 2f);

        var rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.sharedMaterial = _pedestrianMaterial;
            rend.shadowCastingMode = ShadowCastingMode.Off;
        }

        var lat = Random.Range(-_walkHalfWidth * 0.35f, _walkHalfWidth * 0.35f);
        PlaceOnSegment(go.transform, seg, t, lat);

        _agents.Add(new PedestrianAgent
        {
            Transform = go.transform,
            SegmentIndex = si,
            T = t,
            Lateral = lat
        });
    }

    public void RemoveOne()
    {
        if (_agents.Count == 0) return;
        var i = Random.Range(0, _agents.Count);
        DestroyAgentAt(i);
    }

    void DestroyAgentAt(int index)
    {
        var tr = _agents[index].Transform;
        if (tr != null) Destroy(tr.gameObject);
        _agents.RemoveAt(index);
    }

    void FixedUpdate()
    {
        if (_segments.Count == 0 || _agents.Count == 0) return;

        var dt = Time.fixedDeltaTime;
        for (var i = 0; i < _agents.Count; i++)
        {
            var a = _agents[i];
            StepAgent(ref a, dt);
            _agents[i] = a;
        }

        ApplyPressureDeaths();
    }

    void StepAgent(ref PedestrianAgent agent, float dt)
    {
        if (agent.SegmentIndex < 0 || agent.SegmentIndex >= _segments.Count) return;

        var seg = _segments[agent.SegmentIndex];
        var fwd = seg.B - seg.A;
        if (fwd.sqrMagnitude < 1e-8f) return;
        fwd.Normalize();
        var right = Vector3.Cross(Vector3.up, fwd).normalized;

        agent.Lateral += (Random.value - 0.5f) * 2f * lateralWander * dt;
        agent.Lateral = Mathf.Clamp(agent.Lateral, -_walkHalfWidth, _walkHalfWidth);

        var speedAlong = walkSpeed / Mathf.Max(0.01f, seg.Length);
        agent.T += speedAlong * dt;

        if (agent.T >= 1f)
        {
            agent.T = 0f;
            agent.SegmentIndex = PickNextSegmentIndex(seg.ToNode);
            seg = _segments[agent.SegmentIndex];
        }

        PlaceOnSegment(agent.Transform, seg, agent.T, agent.Lateral);
    }

    int PickNextSegmentIndex(int atNode)
    {
        if (_outgoing.TryGetValue(atNode, out var outs) && outs.Count > 0)
            return outs[Random.Range(0, outs.Count)];

        if (_segments.Count == 0) return 0;
        return Random.Range(0, _segments.Count);
    }

    static void PlaceOnSegment(Transform tr, WalkSegment seg, float t, float lateral)
    {
        var fwd = seg.B - seg.A;
        fwd.Normalize();
        var right = Vector3.Cross(Vector3.up, fwd).normalized;
        var p = Vector3.Lerp(seg.A, seg.B, Mathf.Clamp01(t)) + right * lateral;
        tr.position = p;
        if (fwd.sqrMagnitude > 1e-8f)
            tr.rotation = Quaternion.LookRotation(fwd, Vector3.up);
    }

    void ApplyPressureDeaths()
    {
        var n = _agents.Count;
        if (n == 0) return;

        var r = pressureRadius;
        var r2 = r * r;
        var need = Mathf.Max(1, pressureNeighborDeathThreshold);
        var kill = new bool[n];

        for (var i = 0; i < n; i++)
        {
            var pi = _agents[i].Transform.position;
            var cnt = 0;
            for (var j = 0; j < n; j++)
            {
                if (i == j) continue;
                if ((_agents[j].Transform.position - pi).sqrMagnitude <= r2)
                    cnt++;
                if (cnt >= need) break;
            }

            if (cnt >= need)
                kill[i] = true;
        }

        for (var i = n - 1; i >= 0; i--)
        {
            if (kill[i])
                DestroyAgentAt(i);
        }
    }
}
