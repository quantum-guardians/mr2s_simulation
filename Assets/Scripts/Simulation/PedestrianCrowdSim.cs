using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 도로 구간을 따라 이동하는 원통형 보행자. 명령 시 A*로 노드 경로를 따라갑니다.
/// </summary>
public class PedestrianCrowdSim : MonoBehaviour
{
    [SerializeField] GraphVisualizer visualizer;
    [SerializeField] Transform pedestriansRoot;

    [Header("보행")]
    [SerializeField] float walkSpeed = 1.15f;
    [SerializeField] float lateralWander = 0.65f;
    [Tooltip("명령 경로 추종 시 횡방향 흔들림 배율 (0에 가까울수록 직선)")]
    [SerializeField] float commandLateralWanderMul = 0.15f;
    [SerializeField] float cylinderRadiusXZ = 0.11f;
    [SerializeField] float cylinderHeight = 0.52f;
    [SerializeField] Color pedestrianColor = new(0.92f, 0.88f, 0.82f, 1f);
    [SerializeField] Color selectedTint = new(0.45f, 0.95f, 1f, 1f);

    [Header("압력(밀집 사망)")]
    [SerializeField] float pressureRadius = 0.42f;
    [Tooltip("이 반경 안의 다른 사람 수가 이 값 이상이면 사망 처리")]
    [SerializeField] int pressureNeighborDeathThreshold = 5;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    readonly List<WalkSegment> _segments = new();
    readonly Dictionary<int, List<int>> _outgoing = new();
    readonly Dictionary<int, List<(int to, float cost)>> _astarEdges = new();
    readonly Dictionary<int, Vector3> _nodeWorld = new();
    readonly List<PedestrianAgent> _agents = new();

    Material _pedestrianMaterial;
    MaterialPropertyBlock _mpb;
    float _walkHalfWidth;
    int _nextAgentId = 1;
    int? _selectedAgentId;

    public int LivingCount => _agents.Count;
    public float RoadSurfaceWorldY { get; private set; }

    struct PedestrianAgent
    {
        public int Id;
        public Transform Transform;
        public Renderer Renderer;
        public int SegmentIndex;
        public float T;
        public float Lateral;
        public List<int> CommandPath;
        public int CommandTargetIdx;
    }

    void Awake()
    {
        EnsurePedestrianMaterial();
        EnsureRoot();
        _mpb = new MaterialPropertyBlock();
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
        ApplyBaseColor(_pedestrianMaterial, pedestrianColor);
    }

    static void ApplyBaseColor(Material m, Color c)
    {
        if (m.HasProperty(BaseColorId))
            m.SetColor(BaseColorId, c);
        else if (m.HasProperty(ColorId))
            m.SetColor(ColorId, c);
        else
            m.color = c;
    }

    public bool HasAgentId(int agentId)
    {
        for (var i = 0; i < _agents.Count; i++)
        {
            if (_agents[i].Id == agentId)
                return true;
        }

        return false;
    }

    public void SetSelectedAgentId(int? agentId)
    {
        _selectedAgentId = agentId;
        RefreshAllHighlights();
    }

    public void SetSelectedUnit(PedestrianUnit unit) =>
        SetSelectedAgentId(unit != null ? unit.AgentId : null);

    public bool TryNavigateSelectedTo(Vector3 worldPoint)
    {
        if (!_selectedAgentId.HasValue)
            return false;
        return TryOrderMoveToWorldPoint(_selectedAgentId.Value, worldPoint);
    }

    void RefreshAllHighlights()
    {
        for (var i = 0; i < _agents.Count; i++)
            ApplyHighlight(_agents[i], _agents[i].Id == _selectedAgentId);
    }

    void ApplyHighlight(PedestrianAgent agent, bool selected)
    {
        if (agent.Renderer == null) return;
        var c = selected ? Color.Lerp(pedestrianColor, selectedTint, 0.55f) : pedestrianColor;
        _mpb.Clear();
        if (agent.Renderer.sharedMaterial != null && agent.Renderer.sharedMaterial.HasProperty(BaseColorId))
            _mpb.SetColor(BaseColorId, c);
        else if (agent.Renderer.sharedMaterial != null && agent.Renderer.sharedMaterial.HasProperty(ColorId))
            _mpb.SetColor(ColorId, c);
        agent.Renderer.SetPropertyBlock(_mpb);
    }

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
        _astarEdges.Clear();
        _nodeWorld.Clear();
        _selectedAgentId = null;
    }

    public void OnRoadsBuilt(GraphData graph, float groundY)
    {
        if (visualizer == null || graph == null)
        {
            ClearAllAgents();
            return;
        }

        RoadSurfaceWorldY = visualizer.GetRoadSurfaceWorldY(groundY);
        _walkHalfWidth = Mathf.Max(0.05f, visualizer.GetStreetWalkHalfWidth() - cylinderRadiusXZ);
        visualizer.BuildWalkNetwork(graph, groundY, _segments, _outgoing);

        _nodeWorld.Clear();
        foreach (var kv in graph.Nodes)
            _nodeWorld[kv.Key] = kv.Value.Position;

        RebuildAstarEdges();

        for (var i = _agents.Count - 1; i >= 0; i--)
            DestroyAgentAt(i);
    }

    void RebuildAstarEdges()
    {
        _astarEdges.Clear();
        var best = new Dictionary<(int from, int to), float>();
        foreach (var seg in _segments)
        {
            var k = (seg.FromNode, seg.ToNode);
            if (!best.TryGetValue(k, out var len) || seg.Length < len)
                best[k] = seg.Length;
        }

        foreach (var kv in best)
        {
            if (!_astarEdges.TryGetValue(kv.Key.from, out var list))
            {
                list = new List<(int to, float cost)>();
                _astarEdges[kv.Key.from] = list;
            }

            list.Add((kv.Key.to, kv.Value));
        }
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
        var id = _nextAgentId++;

        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = $"Pedestrian_{id}";
        go.transform.SetParent(pedestriansRoot, false);
        go.transform.localScale = new Vector3(cylinderRadiusXZ * 2f, cylinderHeight * 0.5f, cylinderRadiusXZ * 2f);

        var rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.sharedMaterial = _pedestrianMaterial;
            rend.shadowCastingMode = ShadowCastingMode.Off;
        }

        var unit = go.AddComponent<PedestrianUnit>();
        unit.Initialize(this, id);

        var lat = Random.Range(-_walkHalfWidth * 0.35f, _walkHalfWidth * 0.35f);
        PlaceOnSegment(go.transform, seg, t, lat);

        _agents.Add(new PedestrianAgent
        {
            Id = id,
            Transform = go.transform,
            Renderer = rend,
            SegmentIndex = si,
            T = t,
            Lateral = lat,
            CommandPath = null,
            CommandTargetIdx = 0
        });

        ApplyHighlight(_agents[_agents.Count - 1], id == _selectedAgentId);
    }

    public void RemoveOne()
    {
        if (_agents.Count == 0) return;
        var i = Random.Range(0, _agents.Count);
        if (_selectedAgentId == _agents[i].Id)
            _selectedAgentId = null;
        DestroyAgentAt(i);
        RefreshAllHighlights();
    }

    public bool TryOrderMoveToWorldPoint(int agentId, Vector3 worldPoint)
    {
        if (_astarEdges.Count == 0 || _nodeWorld.Count == 0)
            return false;

        var idx = -1;
        for (var i = 0; i < _agents.Count; i++)
        {
            if (_agents[i].Id != agentId) continue;
            idx = i;
            break;
        }

        if (idx < 0) return false;

        var start = NearestNodeId(_agents[idx].Transform.position);
        var goal = NearestNodeId(worldPoint);
        if (start < 0 || goal < 0) return false;

        if (!RoadPathfinding.TryFindPath(start, goal, _astarEdges, _nodeWorld, out var path) || path.Count < 2)
        {
            Debug.LogWarning("[PedestrianCrowdSim] 목표까지 도로 그래프 경로를 찾지 못했습니다.");
            return false;
        }

        var segIdx = FindSegmentIndex(path[0], path[1]);
        if (segIdx < 0)
        {
            Debug.LogWarning("[PedestrianCrowdSim] 경로의 첫 간선을 찾지 못했습니다.");
            return false;
        }

        var a = _agents[idx];
        a.CommandPath = path;
        a.CommandTargetIdx = 1;
        a.SegmentIndex = segIdx;
        a.T = 0.02f;
        a.Lateral = 0f;
        _agents[idx] = a;

        PlaceOnSegment(a.Transform, _segments[segIdx], a.T, a.Lateral);
        return true;
    }

    int NearestNodeId(Vector3 p)
    {
        var best = -1;
        var bd = float.MaxValue;
        foreach (var kv in _nodeWorld)
        {
            var q = kv.Value;
            var d = (new Vector2(q.x - p.x, q.z - p.z)).sqrMagnitude;
            if (d < bd)
            {
                bd = d;
                best = kv.Key;
            }
        }

        return best;
    }

    int FindSegmentIndex(int fromNode, int toNode)
    {
        for (var i = 0; i < _segments.Count; i++)
        {
            var s = _segments[i];
            if (s.FromNode == fromNode && s.ToNode == toNode)
                return i;
        }

        return -1;
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

        var latMul = agent.CommandPath != null ? commandLateralWanderMul : 1f;
        agent.Lateral += (Random.value - 0.5f) * 2f * lateralWander * latMul * dt;
        agent.Lateral = Mathf.Clamp(agent.Lateral, -_walkHalfWidth, _walkHalfWidth);

        var speedAlong = walkSpeed / Mathf.Max(0.01f, seg.Length);
        agent.T += speedAlong * dt;

        if (agent.T >= 1f)
        {
            var toNode = seg.ToNode;
            agent.T = 0f;

            if (agent.CommandPath != null &&
                agent.CommandTargetIdx < agent.CommandPath.Count &&
                toNode == agent.CommandPath[agent.CommandTargetIdx])
            {
                agent.CommandTargetIdx++;
                if (agent.CommandTargetIdx >= agent.CommandPath.Count)
                {
                    agent.CommandPath = null;
                    agent.CommandTargetIdx = 0;
                }
            }

            agent.SegmentIndex = PickNextSegmentIndex(toNode, ref agent);
            seg = _segments[agent.SegmentIndex];
        }

        PlaceOnSegment(agent.Transform, seg, agent.T, agent.Lateral);
    }

    int PickNextSegmentIndex(int atNode, ref PedestrianAgent agent)
    {
        if (agent.CommandPath != null &&
            agent.CommandTargetIdx < agent.CommandPath.Count &&
            _outgoing.TryGetValue(atNode, out var outs))
        {
            var target = agent.CommandPath[agent.CommandTargetIdx];
            var candidates = new List<int>(4);
            foreach (var si in outs)
            {
                if (_segments[si].ToNode == target)
                    candidates.Add(si);
            }

            if (candidates.Count > 0)
                return candidates[Random.Range(0, candidates.Count)];
        }

        if (_outgoing.TryGetValue(atNode, out var outs2) && outs2.Count > 0)
            return outs2[Random.Range(0, outs2.Count)];

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
            if (!kill[i]) continue;
            if (_selectedAgentId == _agents[i].Id)
                _selectedAgentId = null;
            DestroyAgentAt(i);
        }

        if (_selectedAgentId.HasValue)
            RefreshAllHighlights();
    }
}
