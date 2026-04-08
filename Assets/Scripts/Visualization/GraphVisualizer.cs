using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class GraphVisualizer : MonoBehaviour
{
    [Header("Hierarchy")]
    [SerializeField] Transform nodesRoot;
    [SerializeField] Transform edgesRoot;
    [SerializeField] Transform roadsRoot;

    [Header("Layout")]
    [SerializeField] int layoutIterations = 120;
    [SerializeField] float repulsionStrength = 12f;
    [SerializeField] float springStrength = 0.08f;
    [SerializeField] float idealLengthPerWeight = 0.35f;
    [SerializeField] float minIdealLength = 0.4f;
    [SerializeField] float layoutStep = 0.35f;
    [SerializeField] float initialSpread = 4f;
    [SerializeField] float nodeSphereScale = 0.35f;
    [SerializeField] float nodeHeightOffset = 0.2f;

    [Header("Nodes")]
    [SerializeField] Color nodeColor = new(0.15f, 0.72f, 0.28f, 1f);
    [Tooltip("비우면 위 색으로 런타임 재질 생성")]
    [SerializeField] Material nodeMaterial;

    [Header("Ground (Plane)")]
    [Tooltip("바닥 Plane의 Collider. 비우면 Ground Plane Transform에서 Collider를 찾습니다.")]
    [SerializeField] Collider groundPlaneCollider;
    [Tooltip("Collider가 없을 때 Plane Transform만 넣어도 됩니다.")]
    [SerializeField] Transform groundPlaneTransform;
    [SerializeField] float planeInnerMargin = 0.15f;

    [Header("Node labels")]
    [SerializeField] float nodeLabelFontSize = 5f;
    [Tooltip("구 로컬 반지름(0.5) 기준, 위로 추가 오프셋")]
    [SerializeField] float nodeLabelYOffset = 0.12f;
    [SerializeField] Color nodeLabelColor = Color.white;

    [Header("Edges")]
    [SerializeField] float edgeLineWidth = 0.06f;
    [SerializeField] Color edgeColor = new(0.2f, 0.75f, 1f, 0.9f);

    [Header("Street — 보행·시뮬 통로")]
    [Tooltip("건물 사이 실제 ‘거리’ 폭. 나중에 에이전트는 이 폭 안을 다니게 맞추면 됨")]
    [SerializeField] float streetClearWidth = 1.15f;
    [SerializeField] float roadPavementThickness = 0.04f;
    [SerializeField] float roadSurfaceYOffset = 0.004f;
    [SerializeField] Material roadMaterial;

    [Header("Street — 디테일")]
    [SerializeField] bool streetAddCurbs = true;
    [SerializeField] float curbWidth = 0.07f;
    [SerializeField] float curbHeight = 0.055f;
    [SerializeField] Color curbColor = new(0.45f, 0.45f, 0.48f, 1f);
    [SerializeField] bool streetAddCenterLine = true;
    [SerializeField] float centerLineDashLength = 0.42f;
    [SerializeField] float centerLineGap = 0.32f;
    [SerializeField] float centerLineWidth = 0.07f;
    [SerializeField] Color centerLineColor = new(0.95f, 0.92f, 0.35f, 1f);

    [Tooltip("교차(노드) 근처 포장 단축 — 도로 중심선 샘플과 동일")]
    [SerializeField] float streetCornerInset = 0.4f;

    [Header("Buildings — 격자 + 거리장 (도로 위에 안 올림)")]
    [Tooltip("간선 옆에 건물을 붙이지 않고, 격자에서 도로·노드와의 거리만 보고 배치")]
    [SerializeField] bool buildInteriorBuildings = true;
    [SerializeField] float buildingGridCellSize = 1.05f;
    [Tooltip("도로 반폭(streetClearWidth/2)에 더해 빈 공간")]
    [SerializeField] float buildingExtraRoadClearance = 0.2f;
    [Tooltip("노드(교차) 주변 건물 금지 반경")]
    [SerializeField] float buildingNodePlazaRadius = 0.55f;
    [Tooltip("외곽 띠 안쪽을 채울 때 노드 바운딩에 더하는 여유 (외곽 띠와 동일 권장)")]
    [SerializeField] float buildingDistrictPadding = 1f;
    [SerializeField] float buildingHeight = 3.2f;
    [Range(0f, 0.4f)]
    [SerializeField] float buildingHeightVariation = 0.2f;
    [SerializeField] Material buildingMaterial;
    [SerializeField] int buildingMaxInstances = 1800;

    [Header("District — 바깥 건물 띠")]
    [Tooltip("전체 도로망을 한 블록처럼 감싸는 외곽 건물")]
    [SerializeField] bool buildOuterBuildingRing = true;
    [SerializeField] float outerRingPadding = 1f;
    [SerializeField] float outerRingDepth = 2.6f;
    [SerializeField] float outerRingHeight = 4.2f;
    [SerializeField] Material outerRingMaterial;
    [Tooltip("외곽 벽을 여러 패널로 나눔")]
    [SerializeField] float outerRingPanelTargetLength = 3.5f;
    [Range(0f, 0.35f)]
    [SerializeField] float outerRingHeightVariation = 0.18f;

    [Header("Input")]
    [SerializeField] float rayMaxDistance = 200f;

    readonly Dictionary<int, Transform> _nodeTransforms = new();
    readonly List<LineRenderer> _edgeLines = new();
    readonly List<(int from, int to)> _edgePairs = new();
    readonly List<GameObject> _roads = new();

    GraphData _activeGraph;
    float _fallbackGroundY;
    float _groundY;
    bool _hasPlaneBounds;
    float _planeMinX, _planeMaxX, _planeMinZ, _planeMaxZ;
    int? _dragNodeId;
    Camera _cam;
    Material _runtimeNodeMaterial;
    Material _runtimeCurbMaterial;
    Material _runtimeLineMaterial;
    Material _runtimeFacadeMaterial;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        _cam = Camera.main;
        EnsureRoots();
        EnsureNodeSharedMaterial();
        EnsureStreetAccentMaterials();
    }

    void OnDestroy()
    {
        if (_runtimeNodeMaterial != null)
            Destroy(_runtimeNodeMaterial);
        if (_runtimeCurbMaterial != null)
            Destroy(_runtimeCurbMaterial);
        if (_runtimeLineMaterial != null)
            Destroy(_runtimeLineMaterial);
        if (_runtimeFacadeMaterial != null)
            Destroy(_runtimeFacadeMaterial);
    }

    void EnsureStreetAccentMaterials()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard")
                     ?? Shader.Find("Unlit/Color");
        if (shader == null) return;

        _runtimeCurbMaterial = new Material(shader);
        SetMaterialColor(_runtimeCurbMaterial, curbColor);

        _runtimeLineMaterial = new Material(shader);
        SetMaterialColor(_runtimeLineMaterial, centerLineColor);

        _runtimeFacadeMaterial = new Material(shader);
        SetMaterialColor(_runtimeFacadeMaterial, new Color(0.38f, 0.4f, 0.46f, 1f));
    }

    static void SetMaterialColor(Material m, Color c)
    {
        if (m.HasProperty(BaseColorId))
            m.SetColor(BaseColorId, c);
        else if (m.HasProperty(ColorId))
            m.SetColor(ColorId, c);
        else
            m.color = c;
    }

    void EnsureNodeSharedMaterial()
    {
        if (nodeMaterial != null) return;

        var shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard")
                     ?? Shader.Find("Unlit/Color");
        if (shader == null) return;

        _runtimeNodeMaterial = new Material(shader);
        if (_runtimeNodeMaterial.HasProperty("_BaseColor"))
            _runtimeNodeMaterial.SetColor("_BaseColor", nodeColor);
        else if (_runtimeNodeMaterial.HasProperty("_Color"))
            _runtimeNodeMaterial.SetColor("_Color", nodeColor);
        else
            _runtimeNodeMaterial.color = nodeColor;
    }

    void EnsureRoots()
    {
        if (nodesRoot == null)
        {
            var go = new GameObject("Nodes");
            nodesRoot = go.transform;
            nodesRoot.SetParent(transform, false);
        }

        if (edgesRoot == null)
        {
            var go = new GameObject("Edges");
            edgesRoot = go.transform;
            edgesRoot.SetParent(transform, false);
        }

        if (roadsRoot == null)
        {
            var go = new GameObject("Roads");
            roadsRoot = go.transform;
            roadsRoot.SetParent(transform, false);
        }
    }

    public void Clear()
    {
        foreach (var t in _nodeTransforms.Values)
            if (t != null) Destroy(t.gameObject);
        _nodeTransforms.Clear();

        foreach (var lr in _edgeLines)
            if (lr != null) Destroy(lr.gameObject);
        _edgeLines.Clear();
        _edgePairs.Clear();

        foreach (var r in _roads)
            if (r != null) Destroy(r);
        _roads.Clear();

        _activeGraph = null;
        _dragNodeId = null;
    }

    public void BuildFromGraph(GraphData graph, float groundY)
    {
        Clear();
        _activeGraph = graph;
        _fallbackGroundY = groundY;
        RefreshPlaneBounds(groundY);

        foreach (var id in graph.Nodes.Keys)
        {
            var p = RandomNodePositionOnGround();
            graph.Nodes[id].Position = p;
        }

        foreach (var kv in graph.Nodes)
            SpawnNode(kv.Key, kv.Value.Position);

        foreach (var edge in graph.Edges)
            SpawnEdgeLine(edge.From, edge.To);

        for (var i = 0; i < layoutIterations; i++)
            StepForceLayout(graph, layoutStep);

        ApplyDataPositionsToTransforms(graph);
        SyncNodeDataFromTransforms();
        UpdateEdgeLines();
    }

    public void SyncPositionsFromSceneToData(GraphData graph)
    {
        RefreshPlaneBounds(_fallbackGroundY);
        foreach (var kv in _nodeTransforms)
        {
            if (!graph.Nodes.TryGetValue(kv.Key, out var node)) continue;
            var p = kv.Value.position;
            p.y = _groundY + nodeHeightOffset;
            p = ClampPositionToPlaneXZ(p);
            kv.Value.position = p;
            node.Position = p;
        }
    }

    public void BuildRoads(GraphData graph, float groundY)
    {
        foreach (var r in _roads)
            if (r != null) Destroy(r);
        _roads.Clear();

        var surfaceY = groundY + roadSurfaceYOffset + roadPavementThickness * 0.5f;

        var roadSegments = new List<(Vector2 a, Vector2 b)>(graph.Edges.Count);
        foreach (var edge in graph.Edges)
        {
            if (TryGetEdgeXZSegment(graph, edge, out var sa, out var sb, out _))
                roadSegments.Add((sa, sb));
        }

        foreach (var edge in graph.Edges)
            BuildStreetPavementOnly(graph, edge, groundY, surfaceY);

        var rng = Random.state;
        if (buildInteriorBuildings && roadSegments.Count > 0)
        {
            Random.InitState(unchecked(graph.Nodes.Count * 83492791 ^ graph.Edges.Count * 17));
            BuildInteriorBuildingsOnGrid(graph, groundY, roadSegments);
            Random.state = rng;
        }

        if (buildOuterBuildingRing && graph.Nodes.Count > 0)
        {
            Random.InitState(unchecked(graph.Nodes.Count * 83492791 ^ graph.Edges.Count));
            BuildOuterBuildingRing(graph, groundY);
            Random.state = rng;
        }

        SetEdgeLinesVisible(false);
    }

    bool TryGetEdgeXZSegment(GraphData graph, GraphEdgeData edge, out Vector2 start, out Vector2 end, out float lengthEff)
    {
        start = default;
        end = default;
        lengthEff = 0f;

        if (!graph.Nodes.TryGetValue(edge.From, out var na) || !graph.Nodes.TryGetValue(edge.To, out var nb))
            return false;

        var ax = new Vector2(na.Position.x, na.Position.z);
        var bx = new Vector2(nb.Position.x, nb.Position.z);
        var delta = bx - ax;
        var lengthFull = delta.magnitude;
        if (lengthFull < 0.001f) return false;

        var dir = delta / lengthFull;
        var inset = Mathf.Min(streetCornerInset, lengthFull * 0.38f);
        if (inset * 2f >= lengthFull - 0.08f)
            inset = 0f;

        start = ax + dir * inset;
        end = bx - dir * inset;
        lengthEff = Vector2.Distance(start, end);
        return lengthEff >= 0.08f;
    }

    void BuildStreetPavementOnly(GraphData graph, GraphEdgeData edge, float groundY, float surfaceY)
    {
        if (!TryGetEdgeXZSegment(graph, edge, out var start, out var end, out var lengthEff))
            return;

        var midXZ = (start + end) * 0.5f;
        var dir2 = (end - start).normalized;
        var mid = new Vector3(midXZ.x, surfaceY, midXZ.y);
        var rot = Quaternion.LookRotation(new Vector3(dir2.x, 0f, dir2.y), Vector3.up);

        var pavement = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pavement.name = $"Street_{edge.From}_{edge.To}";
        pavement.transform.SetParent(roadsRoot, false);
        pavement.transform.SetPositionAndRotation(mid, rot);
        pavement.transform.localScale = new Vector3(streetClearWidth, roadPavementThickness, lengthEff * 0.998f);
        ApplyMaterialIfAny(pavement, roadMaterial);
        ApplyPavementTintIfPossible(pavement);
        DestroyColliderIfPresent(pavement);
        _roads.Add(pavement);

        var halfStreet = streetClearWidth * 0.5f;
        var curbY = groundY + roadSurfaceYOffset + roadPavementThickness + curbHeight * 0.5f;

        if (streetAddCurbs && _runtimeCurbMaterial != null)
        {
            for (var s = -1; s <= 1; s += 2)
            {
                var xOff = s * (halfStreet - curbWidth * 0.5f);
                var cPos = new Vector3(midXZ.x, curbY, midXZ.y) + rot * new Vector3(xOff, 0f, 0f);
                var curb = GameObject.CreatePrimitive(PrimitiveType.Cube);
                curb.name = $"Curb_{edge.From}_{edge.To}_{(s > 0 ? "R" : "L")}";
                curb.transform.SetParent(roadsRoot, false);
                curb.transform.SetPositionAndRotation(cPos, rot);
                curb.transform.localScale = new Vector3(curbWidth, curbHeight, lengthEff * 0.995f);
                curb.GetComponent<Renderer>().sharedMaterial = _runtimeCurbMaterial;
                DestroyColliderIfPresent(curb);
                _roads.Add(curb);
            }
        }

        if (streetAddCenterLine && _runtimeLineMaterial != null && centerLineDashLength > 0.05f)
        {
            var lineY = surfaceY + roadPavementThickness * 0.5f + 0.006f;
            var z = -lengthEff * 0.5f + centerLineDashLength * 0.5f;
            var dashIdx = 0;
            while (z < lengthEff * 0.5f - centerLineDashLength * 0.25f)
            {
                var linePos = new Vector3(midXZ.x, lineY, midXZ.y) + rot * new Vector3(0f, 0f, z);
                var dash = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dash.name = $"RoadLine_{edge.From}_{edge.To}_{dashIdx++}";
                dash.transform.SetParent(roadsRoot, false);
                dash.transform.SetPositionAndRotation(linePos, rot);
                dash.transform.localScale = new Vector3(centerLineWidth, 0.018f, centerLineDashLength * 0.92f);
                dash.GetComponent<Renderer>().sharedMaterial = _runtimeLineMaterial;
                DestroyColliderIfPresent(dash);
                _roads.Add(dash);
                z += centerLineDashLength + centerLineGap;
            }
        }
    }

    void ComputePaddedGraphBoundsXZ(GraphData graph, float pad, out float minX, out float maxX, out float minZ, out float maxZ)
    {
        minX = float.PositiveInfinity;
        maxX = float.NegativeInfinity;
        minZ = float.PositiveInfinity;
        maxZ = float.NegativeInfinity;

        foreach (var n in graph.Nodes.Values)
        {
            var p = n.Position;
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        minX -= pad;
        maxX += pad;
        minZ -= pad;
        maxZ += pad;

        var spanX = maxX - minX;
        var spanZ = maxZ - minZ;
        const float minSpan = 2f;
        if (spanX < minSpan)
        {
            var cx = (minX + maxX) * 0.5f;
            minX = cx - minSpan * 0.5f;
            maxX = cx + minSpan * 0.5f;
        }

        if (spanZ < minSpan)
        {
            var cz = (minZ + maxZ) * 0.5f;
            minZ = cz - minSpan * 0.5f;
            maxZ = cz + minSpan * 0.5f;
        }
    }

    void ClampRectToPlaneBounds(ref float minX, ref float maxX, ref float minZ, ref float maxZ)
    {
        if (!_hasPlaneBounds) return;
        minX = Mathf.Max(minX, _planeMinX);
        maxX = Mathf.Min(maxX, _planeMaxX);
        minZ = Mathf.Max(minZ, _planeMinZ);
        maxZ = Mathf.Min(maxZ, _planeMaxZ);
    }

    static float MinDistanceSqPointToSegment2D(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var len2 = ab.sqrMagnitude;
        if (len2 < 1e-10f)
            return (p - a).sqrMagnitude;
        var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        var proj = a + t * ab;
        return (p - proj).sqrMagnitude;
    }

    static float MinDistanceSqToRoadSegments(Vector2 p, List<(Vector2 a, Vector2 b)> segments)
    {
        var best = float.MaxValue;
        foreach (var s in segments)
            best = Mathf.Min(best, MinDistanceSqPointToSegment2D(p, s.a, s.b));
        return best;
    }

    void BuildInteriorBuildingsOnGrid(GraphData graph, float groundY, List<(Vector2 a, Vector2 b)> roadSegments)
    {
        var facadeMat = buildingMaterial != null ? buildingMaterial : _runtimeFacadeMaterial;
        if (facadeMat == null) return;

        var padForDistrict = buildOuterBuildingRing ? outerRingPadding : buildingDistrictPadding;
        ComputePaddedGraphBoundsXZ(graph, padForDistrict, out var minX, out var maxX, out var minZ, out var maxZ);
        ClampRectToPlaneBounds(ref minX, ref maxX, ref minZ, ref maxZ);
        if (maxX - minX < 0.15f || maxZ - minZ < 0.15f) return;

        var cell = Mathf.Max(0.25f, buildingGridCellSize);
        var roadDenySq = Mathf.Pow(streetClearWidth * 0.5f + buildingExtraRoadClearance, 2f);
        var nodeDenySq = Mathf.Pow(Mathf.Max(0.05f, buildingNodePlazaRadius), 2f);

        var placed = 0;
        for (var wx = minX + cell * 0.5f; wx <= maxX - cell * 0.5f + 1e-5f; wx += cell)
        {
            for (var wz = minZ + cell * 0.5f; wz <= maxZ - cell * 0.5f + 1e-5f; wz += cell)
            {
                if (placed >= buildingMaxInstances) return;

                var p = new Vector2(wx, wz);
                if (MinDistanceSqToRoadSegments(p, roadSegments) < roadDenySq)
                    continue;

                var skipNode = false;
                foreach (var n in graph.Nodes.Values)
                {
                    var q = new Vector2(n.Position.x, n.Position.z);
                    if ((p - q).sqrMagnitude < nodeDenySq)
                    {
                        skipNode = true;
                        break;
                    }
                }

                if (skipNode) continue;

                var hMul = 1f - buildingHeightVariation + Random.value * (2f * buildingHeightVariation);
                var h = Mathf.Max(0.45f, buildingHeight * Mathf.Max(0.35f, hMul));
                var foot = cell * 0.9f;

                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = $"CityBlock_{placed}";
                b.transform.SetParent(roadsRoot, false);
                b.transform.position = new Vector3(wx, groundY + h * 0.5f, wz);
                b.transform.rotation = Quaternion.identity;
                b.transform.localScale = new Vector3(foot, h, foot);
                ApplyMaterialIfAny(b, facadeMat);
                ApplyBuildingFacadeTint(b);
                _roads.Add(b);
                placed++;
            }
        }
    }

    void BuildOuterBuildingRing(GraphData graph, float groundY)
    {
        ComputePaddedGraphBoundsXZ(graph, outerRingPadding, out var minX, out var maxX, out var minZ, out var maxZ);

        var spanX = maxX - minX;
        var spanZ = maxZ - minZ;

        var d = outerRingDepth;
        var hBase = outerRingHeight;
        var mat = outerRingMaterial != null ? outerRingMaterial : buildingMaterial ?? _runtimeFacadeMaterial;

        void PanelRowX(string prefix, float zCenter, float totalX, float xMin, float depthZ)
        {
            var panels = Mathf.Clamp(Mathf.RoundToInt(totalX / Mathf.Max(1f, outerRingPanelTargetLength)), 2, 16);
            var w = totalX / panels;
            for (var i = 0; i < panels; i++)
            {
                var hx = hBase * (1f - outerRingHeightVariation + Random.value * (2f * outerRingHeightVariation));
                hx = Mathf.Max(0.6f, hx);
                var xC = xMin + (i + 0.5f) * w;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"{prefix}_{i}";
                go.transform.SetParent(roadsRoot, false);
                go.transform.position = new Vector3(xC, groundY + hx * 0.5f, zCenter);
                go.transform.rotation = Quaternion.identity;
                go.transform.localScale = new Vector3(w * 0.97f, hx, depthZ);
                ApplyMaterialIfAny(go, mat);
                ApplyBuildingFacadeTint(go);
                _roads.Add(go);
            }
        }

        void PanelRowZ(string prefix, float xCenter, float totalZ, float zMin, float depthX)
        {
            var panels = Mathf.Clamp(Mathf.RoundToInt(totalZ / Mathf.Max(1f, outerRingPanelTargetLength)), 2, 16);
            var w = totalZ / panels;
            for (var i = 0; i < panels; i++)
            {
                var hx = hBase * (1f - outerRingHeightVariation + Random.value * (2f * outerRingHeightVariation));
                hx = Mathf.Max(0.6f, hx);
                var zC = zMin + (i + 0.5f) * w;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"{prefix}_{i}";
                go.transform.SetParent(roadsRoot, false);
                go.transform.position = new Vector3(xCenter, groundY + hx * 0.5f, zC);
                go.transform.rotation = Quaternion.identity;
                go.transform.localScale = new Vector3(depthX, hx, w * 0.97f);
                ApplyMaterialIfAny(go, mat);
                ApplyBuildingFacadeTint(go);
                _roads.Add(go);
            }
        }

        var totalX = spanX + d * 2f;
        var x0 = minX - d;
        PanelRowX("Outer_S", minZ - d * 0.5f, totalX, x0, d);
        PanelRowX("Outer_N", maxZ + d * 0.5f, totalX, x0, d);

        var totalZ = spanZ + d * 2f;
        var z0 = minZ - d;
        PanelRowZ("Outer_W", minX - d * 0.5f, totalZ, z0, d);
        PanelRowZ("Outer_E", maxX + d * 0.5f, totalZ, z0, d);
    }

    void ApplyPavementTintIfPossible(GameObject pavement)
    {
        var r = pavement.GetComponent<Renderer>();
        if (r == null || roadMaterial == null) return;
        var block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(BaseColorId))
        {
            var c = r.sharedMaterial.GetColor(BaseColorId);
            block.SetColor(BaseColorId, c * new Color(0.88f, 0.88f, 0.9f, 1f));
            r.SetPropertyBlock(block);
        }
    }

    void ApplyBuildingFacadeTint(GameObject building)
    {
        var r = building.GetComponent<Renderer>();
        if (r == null || r.sharedMaterial == null) return;
        if (!r.sharedMaterial.HasProperty(BaseColorId) && !r.sharedMaterial.HasProperty(ColorId)) return;

        var block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        var jitter = 0.92f + Random.value * 0.12f;
        var bias = new Color(jitter, jitter * Random.Range(0.97f, 1.03f), jitter * Random.Range(0.95f, 1.05f), 1f);
        if (r.sharedMaterial.HasProperty(BaseColorId))
        {
            var c = r.sharedMaterial.GetColor(BaseColorId);
            block.SetColor(BaseColorId, c * bias);
        }
        else
        {
            var c = r.sharedMaterial.GetColor(ColorId);
            block.SetColor(ColorId, c * bias);
        }

        r.SetPropertyBlock(block);
    }

    static void ApplyMaterialIfAny(GameObject go, Material mat)
    {
        if (mat == null) return;
        var r = go.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = mat;
    }

    static void DestroyColliderIfPresent(GameObject go)
    {
        var c = go.GetComponent<Collider>();
        if (c != null) Destroy(c);
    }

    public void SetEdgeLinesVisible(bool visible)
    {
        foreach (var lr in _edgeLines)
            if (lr != null) lr.enabled = visible;
    }

    void LateUpdate()
    {
        if (_activeGraph != null)
            RefreshPlaneBounds(_fallbackGroundY);

        HandleDrag();
        SyncNodeDataFromTransforms();
        UpdateEdgeLines();
    }

    void HandleDrag()
    {
        if (_activeGraph == null || _cam == null) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        var ray = _cam.ScreenPointToRay(mouse.position.ReadValue());

        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (TryPickNode(ray, out var id))
                _dragNodeId = id;
        }

        if (mouse.leftButton.wasReleasedThisFrame)
            _dragNodeId = null;

        if (!_dragNodeId.HasValue) return;
        if (!TryIntersectGroundPlane(ray, _groundY, out var hitPoint)) return;

        hitPoint.y = _groundY + nodeHeightOffset;
        hitPoint = ClampPositionToPlaneXZ(hitPoint);
        var idv = _dragNodeId.Value;
        if (_nodeTransforms.TryGetValue(idv, out var tr))
            tr.position = hitPoint;
        if (_activeGraph.Nodes.TryGetValue(idv, out var node))
            node.Position = hitPoint;
    }

    bool TryPickNode(Ray ray, out int nodeId)
    {
        nodeId = default;
        var hits = Physics.RaycastAll(ray, rayMaxDistance);
        float best = float.MaxValue;
        foreach (var h in hits)
        {
            if (!_nodeTransforms.TryGetKeyByValue(h.collider.transform, out var id)) continue;
            if (h.distance >= best) continue;
            best = h.distance;
            nodeId = id;
        }

        return best < float.MaxValue;
    }

    static bool TryIntersectGroundPlane(Ray ray, float groundY, out Vector3 point)
    {
        var plane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
        point = default;
        if (!plane.Raycast(ray, out var enter)) return false;
        point = ray.GetPoint(enter);
        return true;
    }

    void StepForceLayout(GraphData data, float dt)
    {
        var ids = new List<int>(data.Nodes.Keys);
        var n = ids.Count;
        var force = new Vector3[n];

        for (var i = 0; i < n; i++)
        {
            var pi = data.Nodes[ids[i]].Position;
            pi.y = _groundY;
            for (var j = i + 1; j < n; j++)
            {
                var pj = data.Nodes[ids[j]].Position;
                pj.y = _groundY;
                var d = pi - pj;
                d.y = 0f;
                var dist = Mathf.Max(d.magnitude, 0.05f);
                var dir = d / dist;
                var f = dir * (repulsionStrength / (dist * dist));
                force[i] += f;
                force[j] -= f;
            }
        }

        var indexOf = new Dictionary<int, int>(n);
        for (var k = 0; k < n; k++)
            indexOf[ids[k]] = k;

        foreach (var e in data.Edges)
        {
            if (!data.Nodes.TryGetValue(e.From, out var na) || !data.Nodes.TryGetValue(e.To, out var nb)) continue;
            var pi = na.Position;
            var pj = nb.Position;
            pi.y = _groundY;
            pj.y = _groundY;
            var d = pj - pi;
            d.y = 0f;
            var dist = Mathf.Max(d.magnitude, 0.05f);
            var dir = d / dist;
            var ideal = Mathf.Max(minIdealLength, e.Weight * idealLengthPerWeight);
            var delta = dist - ideal;
            var fs = dir * (delta * springStrength);
            if (indexOf.TryGetValue(e.From, out var fi)) force[fi] += fs;
            if (indexOf.TryGetValue(e.To, out var fj)) force[fj] -= fs;
        }

        for (var i = 0; i < n; i++)
        {
            var f = force[i];
            f.y = 0f;
            var node = data.Nodes[ids[i]];
            node.Position += f * dt;
            node.Position.y = _groundY + nodeHeightOffset;
            node.Position = ClampPositionToPlaneXZ(node.Position);
        }
    }

    void RefreshPlaneBounds(float fallbackGroundY)
    {
        _hasPlaneBounds = false;
        _groundY = fallbackGroundY;

        var col = groundPlaneCollider;
        if (col == null && groundPlaneTransform != null)
            col = groundPlaneTransform.GetComponent<Collider>();

        if (col == null) return;

        var b = col.bounds;
        _groundY = b.max.y;
        var sphereRadiusWorld = nodeSphereScale * 0.5f;
        var m = planeInnerMargin + sphereRadiusWorld;
        _planeMinX = b.min.x + m;
        _planeMaxX = b.max.x - m;
        _planeMinZ = b.min.z + m;
        _planeMaxZ = b.max.z - m;
        _hasPlaneBounds = _planeMaxX > _planeMinX && _planeMaxZ > _planeMinZ;
    }

    Vector3 RandomNodePositionOnGround()
    {
        var y = _groundY + nodeHeightOffset;
        if (_hasPlaneBounds)
        {
            return ClampPositionToPlaneXZ(new Vector3(
                Random.Range(_planeMinX, _planeMaxX),
                y,
                Random.Range(_planeMinZ, _planeMaxZ)));
        }

        var angle = Random.Range(0f, Mathf.PI * 2f);
        var r = Random.Range(0.2f, initialSpread);
        return new Vector3(Mathf.Cos(angle) * r, y, Mathf.Sin(angle) * r);
    }

    Vector3 ClampPositionToPlaneXZ(Vector3 p)
    {
        if (!_hasPlaneBounds) return p;
        p.x = Mathf.Clamp(p.x, _planeMinX, _planeMaxX);
        p.z = Mathf.Clamp(p.z, _planeMinZ, _planeMaxZ);
        return p;
    }

    void SyncNodeDataFromTransforms()
    {
        if (_activeGraph == null) return;
        foreach (var kv in _nodeTransforms)
        {
            if (!_activeGraph.Nodes.TryGetValue(kv.Key, out var node)) continue;
            var p = kv.Value.position;
            p.y = _groundY + nodeHeightOffset;
            p = ClampPositionToPlaneXZ(p);
            kv.Value.position = p;
            node.Position = p;
        }
    }

    void ApplyDataPositionsToTransforms(GraphData data)
    {
        foreach (var kv in data.Nodes)
        {
            if (!_nodeTransforms.TryGetValue(kv.Key, out var tr)) continue;
            var p = kv.Value.Position;
            p.y = _groundY + nodeHeightOffset;
            tr.position = ClampPositionToPlaneXZ(p);
        }
    }

    void SpawnNode(int id, Vector3 position)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = $"Node_{id}";
        go.transform.SetParent(nodesRoot, false);
        go.transform.position = position;
        go.transform.localScale = Vector3.one * nodeSphereScale;
        var rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            if (nodeMaterial != null)
                rend.sharedMaterial = nodeMaterial;
            else if (_runtimeNodeMaterial != null)
                rend.sharedMaterial = _runtimeNodeMaterial;
        }

        _nodeTransforms[id] = go.transform;
        AttachNodeIdLabel(go.transform, id);
    }

    void AttachNodeIdLabel(Transform nodeRoot, int id)
    {
        var labelGo = new GameObject("NodeId");
        labelGo.transform.SetParent(nodeRoot, false);
        labelGo.transform.localPosition = Vector3.up * (0.5f + nodeLabelYOffset);
        labelGo.transform.localRotation = Quaternion.identity;
        labelGo.transform.localScale = Vector3.one;

        var tmp = labelGo.AddComponent<TextMeshPro>();
        tmp.text = id.ToString();
        tmp.fontSize = nodeLabelFontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = nodeLabelColor;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;

        tmp.rectTransform.sizeDelta = new Vector2(2f, 1f);

        labelGo.AddComponent<NodeLabelBillboard>();
    }

    void SpawnEdgeLine(int from, int to)
    {
        var go = new GameObject($"Edge_{from}_{to}");
        go.transform.SetParent(edgesRoot, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.startWidth = edgeLineWidth;
        lr.endWidth = edgeLineWidth;
        lr.useWorldSpace = true;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = edgeColor;
        lr.endColor = edgeColor;
        lr.numCapVertices = 3;
        _edgeLines.Add(lr);
        _edgePairs.Add((from, to));
    }

    void UpdateEdgeLines()
    {
        if (_activeGraph == null) return;

        for (var i = 0; i < _edgeLines.Count; i++)
        {
            var lr = _edgeLines[i];
            if (lr == null) continue;
            var pair = _edgePairs[i];
            if (!_activeGraph.Nodes.TryGetValue(pair.from, out var na) ||
                !_activeGraph.Nodes.TryGetValue(pair.to, out var nb))
                continue;
            lr.SetPosition(0, na.Position);
            lr.SetPosition(1, nb.Position);
        }
    }
}

static class TransformDictExt
{
    public static bool TryGetKeyByValue(this Dictionary<int, Transform> map, Transform value, out int key)
    {
        foreach (var kv in map)
        {
            if (kv.Value == value)
            {
                key = kv.Key;
                return true;
            }

            if (value != null && value.IsChildOf(kv.Value))
            {
                key = kv.Key;
                return true;
            }
        }

        key = default;
        return false;
    }
}

sealed class NodeLabelBillboard : MonoBehaviour
{
    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var toCam = transform.position - cam.transform.position;
        if (toCam.sqrMagnitude < 1e-8f) return;
        transform.rotation = Quaternion.LookRotation(toCam);
    }
}
