using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

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
    [Tooltip("교차하는 간선 쌍에 대한 척력. 0이면 꺼짐. 평면 그래프 배치에 필요")]
    [SerializeField] float crossingRepulsionStrength = 18f;

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
    [Tooltip("Directed 그래프: 간선·화살표 색 (무방향 시안과 대비, 시작은 더 투명)")]
    [SerializeField] Color directedEdgeColor = new(1f, 0.48f, 0.18f, 0.97f);
    [SerializeField] float directedArrowHeadLength = 0.28f;
    [SerializeField] float directedArrowHeadBaseRadius = 0.095f;
    [SerializeField] float directedArrowStemLength = 0.18f;
    [SerializeField] float directedArrowStemRadius = 0.026f;
    [Tooltip("화살 끝이 To 노드 쪽에 오도록 (0~1, 클수록 목적지에 가까움)")]
    [SerializeField] float directedArrowTipNearT = 0.9f;
    [Range(0f, 2f)]
    [SerializeField] float directedEdgeEmission = 0.5f;
    [Tooltip("Directed 간선 너비: 출발(가늘게) → 도착(굵게)")]
    [SerializeField] float directedLineWidthStartMul = 0.72f;
    [SerializeField] float directedLineWidthEndMul = 1.28f;
    [Range(0.12f, 1f)]
    [SerializeField] float directedLineAlphaStart = 0.34f;
    [SerializeField] Material directedArrowMaterial;

    [Header("Directed — 흐르는 네비게이션")]
    [Tooltip("방향 간선 위를 따라 반투명 마커가 From → To로 이동")]
    [SerializeField] bool directedFlowNavEnabled = true;
    [Range(1, 4)]
    [SerializeField] int directedFlowPulseCount = 2;
    [SerializeField] float directedFlowOrbScale = 0.13f;
    [Tooltip("간선 전체를 따라 펄스가 한 바퀴 도는 횟수(초당)")]
    [SerializeField] float directedFlowCyclesPerSecond = 0.5f;
    [Tooltip("노드 구 안쪽으로 들어가지 않게 양 끝에서 안쪽으로 띄움(월드 단위)")]
    [SerializeField] float directedFlowEndInset = 0.34f;
    [Tooltip("간선마다 시간 위상을 달리해 동시에 겹쳐 보이지 않게")]
    [SerializeField] float directedFlowEdgePhaseSpread = 0.37f;
    [Tooltip("반투명 구 색 — 알파로 투명도 조절")]
    [SerializeField] Color directedFlowOrbColor = new(1f, 0.82f, 0.45f, 0.38f);

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
    readonly List<Transform> _edgeArrowMarkers = new();
    readonly List<Transform> _directedFlowRoots = new();
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
    Material _directedArrowSharedMaterial;
    Material _directedFlowSharedMaterial;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    static readonly int BlendId = Shader.PropertyToID("_Blend");
    static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
    static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");

    static Mesh _directedArrowHeadMesh;

    static Mesh GetDirectedArrowHeadMesh()
    {
        if (_directedArrowHeadMesh != null)
            return _directedArrowHeadMesh;

        const float tipY = 1f;
        const float baseY = 0f;
        const float r = 0.5f;
        var rt = r * 0.8660254f;
        var verts = new[]
        {
            new Vector3(0f, tipY, 0f),
            new Vector3(0f, baseY, r),
            new Vector3(-rt, baseY, -0.5f * r),
            new Vector3(rt, baseY, -0.5f * r)
        };
        var tris = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 3, 1, 2, 3 };
        var mesh = new Mesh { name = "DirectedArrowHead" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        _directedArrowHeadMesh = mesh;
        return _directedArrowHeadMesh;
    }

    void Awake()
    {
        _cam = Camera.main;
        EnsureRoots();
        EnsureNodeSharedMaterial();
        EnsureStreetAccentMaterials();
        EnsureDirectedArrowMaterial();
        EnsureDirectedFlowMaterial();
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
        if (_directedArrowSharedMaterial != null)
            Destroy(_directedArrowSharedMaterial);
        if (_directedFlowSharedMaterial != null)
            Destroy(_directedFlowSharedMaterial);
    }

    void EnsureDirectedArrowMaterial()
    {
        if (directedArrowMaterial != null) return;
        var shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard")
                     ?? Shader.Find("Sprites/Default");
        if (shader == null) return;
        _directedArrowSharedMaterial = new Material(shader);
        ApplyDirectedArrowMaterialLook(_directedArrowSharedMaterial);
    }

    void ApplyDirectedArrowMaterialLook(Material m)
    {
        if (m == null) return;
        SetMaterialColor(m, directedEdgeColor);
        if (m.HasProperty(EmissionColorId))
        {
            m.EnableKeyword("_EMISSION");
            var e = directedEdgeColor * directedEdgeEmission;
            e.a = 1f;
            m.SetColor(EmissionColorId, e);
        }
    }

    void EnsureDirectedFlowMaterial()
    {
        if (_directedFlowSharedMaterial != null)
        {
            ApplyDirectedFlowMaterialLook(_directedFlowSharedMaterial);
            return;
        }

        var shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Unlit/Transparent")
                     ?? Shader.Find("Sprites/Default");
        if (shader == null) return;

        _directedFlowSharedMaterial = new Material(shader);
        ApplyDirectedFlowMaterialLook(_directedFlowSharedMaterial);
    }

    void ApplyDirectedFlowMaterialLook(Material m)
    {
        if (m == null) return;
        var c = directedFlowOrbColor;
        SetMaterialColor(m, c);

        m.renderQueue = (int)RenderQueue.Transparent;
        if (m.HasProperty(SurfaceId))
        {
            m.SetFloat(SurfaceId, 1f);
            if (m.HasProperty(BlendId))
                m.SetFloat(BlendId, 0f);
            if (m.HasProperty(ZWriteId))
                m.SetFloat(ZWriteId, 0f);
            if (m.HasProperty(SrcBlendId))
                m.SetInt(SrcBlendId, (int)BlendMode.SrcAlpha);
            if (m.HasProperty(DstBlendId))
                m.SetInt(DstBlendId, (int)BlendMode.OneMinusSrcAlpha);
        }

        if (m.HasProperty(EmissionColorId))
        {
            m.EnableKeyword("_EMISSION");
            var e = new Color(c.r, c.g, c.b, 1f) * (0.25f + c.a * 0.5f);
            m.SetColor(EmissionColorId, e);
        }
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
        _edgeArrowMarkers.Clear();
        _directedFlowRoots.Clear();

        foreach (var r in _roads)
            if (r != null) Destroy(r);
        _roads.Clear();

        _activeGraph = null;
        _dragNodeId = null;
    }

    public void BuildFromGraph(GraphData graph, float groundY, bool keepExistingPositions = false)
    {
        Clear();
        _activeGraph = graph;
        _fallbackGroundY = groundY;
        RefreshPlaneBounds(groundY);

        if (keepExistingPositions)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var id in graph.Nodes.Keys)
            {
                var p = graph.Nodes[id].Position;
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z;
                if (p.z > maxZ) maxZ = p.z;
            }

            var dataW = maxX - minX;
            var dataH = maxZ - minZ;
            var dataCx = (minX + maxX) * 0.5f;
            var dataCz = (minZ + maxZ) * 0.5f;

            float targetW, targetH, targetCx, targetCz;
            if (_hasPlaneBounds)
            {
                targetW = (_planeMaxX - _planeMinX) * 0.85f;
                targetH = (_planeMaxZ - _planeMinZ) * 0.85f;
                targetCx = (_planeMinX + _planeMaxX) * 0.5f;
                targetCz = (_planeMinZ + _planeMaxZ) * 0.5f;
            }
            else
            {
                targetW = Mathf.Max(dataW, 14f);
                targetH = Mathf.Max(dataH, 14f);
                targetCx = 0f;
                targetCz = 0f;
            }

            var scaleX = dataW > 0.01f ? targetW / dataW : 1f;
            var scaleZ = dataH > 0.01f ? targetH / dataH : 1f;
            var scale = Mathf.Min(scaleX, scaleZ);

            foreach (var id in graph.Nodes.Keys)
            {
                var p = graph.Nodes[id].Position;
                p.x = (p.x - dataCx) * scale + targetCx;
                p.z = (p.z - dataCz) * scale + targetCz;
                p.y = _groundY + nodeHeightOffset;
                graph.Nodes[id].Position = p;
            }
        }
        else
        {
            foreach (var id in graph.Nodes.Keys)
            {
                var p = RandomNodePositionOnGround();
                graph.Nodes[id].Position = p;
            }
        }

        foreach (var kv in graph.Nodes)
            SpawnNode(kv.Key, kv.Value.Position);

        foreach (var edge in graph.Edges)
            SpawnEdgeLine(edge.From, edge.To, graph.Directed);

        if (!keepExistingPositions)
        {
            for (var i = 0; i < layoutIterations; i++)
                StepForceLayout(graph, layoutStep);
        }

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
        SetDirectedArrowMarkersVisible(false);
        SetDirectedFlowNavRenderersVisible(graph.Directed && directedFlowNavEnabled);
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
                DestroyColliderIfPresent(b);
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
                DestroyColliderIfPresent(go);
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
                DestroyColliderIfPresent(go);
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

    void SetDirectedArrowMarkersVisible(bool visible)
    {
        foreach (var marker in _edgeArrowMarkers)
        {
            if (marker == null) continue;
            marker.gameObject.SetActive(visible);
        }
    }

    void SetDirectedFlowNavRenderersVisible(bool visible)
    {
        foreach (var root in _directedFlowRoots)
        {
            if (root == null) continue;
            for (var c = 0; c < root.childCount; c++)
            {
                var rend = root.GetChild(c).GetComponent<Renderer>();
                if (rend != null) rend.enabled = visible;
            }
        }
    }

    public float GetRoadSurfaceWorldY(float groundY) =>
        groundY + roadSurfaceYOffset + roadPavementThickness * 0.5f;

    public float GetStreetWalkHalfWidth() => streetClearWidth * 0.5f * 0.82f;

    public void BuildWalkNetwork(GraphData graph, float groundY, List<WalkSegment> segments, Dictionary<int, List<int>> outgoingByNode)
    {
        segments.Clear();
        outgoingByNode.Clear();
        if (graph == null || graph.Edges.Count == 0) return;

        void AddOutgoing(int fromNode, int segmentIndex)
        {
            if (!outgoingByNode.TryGetValue(fromNode, out var list))
            {
                list = new List<int>();
                outgoingByNode[fromNode] = list;
            }

            list.Add(segmentIndex);
        }

        var y = GetRoadSurfaceWorldY(groundY);

        if (graph.Directed)
        {
            foreach (var e in graph.Edges)
            {
                if (!TryGetEdgeXZSegment(graph, e, out var sa, out var sb, out var len))
                    continue;
                var idx = segments.Count;
                segments.Add(new WalkSegment
                {
                    A = new Vector3(sa.x, y, sa.y),
                    B = new Vector3(sb.x, y, sb.y),
                    FromNode = e.From,
                    ToNode = e.To,
                    Length = len
                });
                AddOutgoing(e.From, idx);
            }
        }
        else
        {
            foreach (var e in graph.Edges)
            {
                if (!TryGetEdgeXZSegment(graph, e, out var sa, out var sb, out var len))
                    continue;
                var fwd = new WalkSegment
                {
                    A = new Vector3(sa.x, y, sa.y),
                    B = new Vector3(sb.x, y, sb.y),
                    FromNode = e.From,
                    ToNode = e.To,
                    Length = len
                };
                AddOutgoing(e.From, segments.Count);
                segments.Add(fwd);
                var rev = new WalkSegment
                {
                    A = fwd.B,
                    B = fwd.A,
                    FromNode = e.To,
                    ToNode = e.From,
                    Length = len
                };
                AddOutgoing(e.To, segments.Count);
                segments.Add(rev);
            }
        }
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

        if (crossingRepulsionStrength > 0f && data.Edges.Count > 1)
        {
            var edges = data.Edges;
            for (var ei = 0; ei < edges.Count; ei++)
            {
                var ea = edges[ei];
                var a = data.Nodes[ea.From].Position; a.y = 0f;
                var b = data.Nodes[ea.To].Position; b.y = 0f;
                var ma = new Vector2(a.x, a.z);
                var mb = new Vector2(b.x, b.z);

                for (var ej = ei + 1; ej < edges.Count; ej++)
                {
                    var eb = edges[ej];
                    if (ea.From == eb.From || ea.From == eb.To || ea.To == eb.From || ea.To == eb.To)
                        continue;

                    var c = data.Nodes[eb.From].Position; c.y = 0f;
                    var d = data.Nodes[eb.To].Position; d.y = 0f;
                    var mc = new Vector2(c.x, c.z);
                    var md = new Vector2(d.x, d.z);

                    if (!SegmentsIntersect(ma, mb, mc, md)) continue;

                    var mid1 = (ma + mb) * 0.5f;
                    var mid2 = (mc + md) * 0.5f;
                    var diff = mid1 - mid2;
                    var dist = Mathf.Max(diff.magnitude, 0.01f);
                    var dir = diff / dist;
                    var fm = dir * (crossingRepulsionStrength / (dist * dist));

                    var f3 = new Vector3(fm.x, 0f, fm.y);
                    var f3n = -f3;
                    if (indexOf.TryGetValue(ea.From, out var ai)) force[ai] += f3;
                    if (indexOf.TryGetValue(ea.To, out var bi)) force[bi] += f3;
                    if (indexOf.TryGetValue(eb.From, out var ci)) force[ci] += f3n;
                    if (indexOf.TryGetValue(eb.To, out var di)) force[di] += f3n;
                }
            }
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

    static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        return Cross2D(a, b, c) * Cross2D(a, b, d) < 0f &&
               Cross2D(c, d, a) * Cross2D(c, d, b) < 0f;
    }

    static float Cross2D(Vector2 o, Vector2 a, Vector2 b)
    {
        return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
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

    void SpawnEdgeLine(int from, int to, bool directed)
    {
        var go = new GameObject($"Edge_{from}_{to}_{_edgeLines.Count}");
        go.transform.SetParent(edgesRoot, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        if (directed)
        {
            lr.startWidth = edgeLineWidth * directedLineWidthStartMul;
            lr.endWidth = edgeLineWidth * directedLineWidthEndMul;
            var de = directedEdgeColor;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(de, 0f), new GradientColorKey(Color.Lerp(de, Color.white, 0.15f), 1f) },
                new[] { new GradientAlphaKey(Mathf.Clamp01(directedLineAlphaStart) * de.a, 0f), new GradientAlphaKey(Mathf.Min(1f, de.a), 1f) });
            lr.colorGradient = grad;
            lr.numCapVertices = 5;
        }
        else
        {
            lr.startWidth = edgeLineWidth;
            lr.endWidth = edgeLineWidth;
            lr.startColor = edgeColor;
            lr.endColor = edgeColor;
            lr.numCapVertices = 3;
        }

        _edgeLines.Add(lr);
        _edgePairs.Add((from, to));

        if (directed)
        {
            _edgeArrowMarkers.Add(SpawnDirectedArrowUnder(go.transform));
            _directedFlowRoots.Add(directedFlowNavEnabled ? SpawnDirectedFlowNavUnder(go.transform) : null);
        }
        else
        {
            _edgeArrowMarkers.Add(null);
            _directedFlowRoots.Add(null);
        }
    }

    Transform SpawnDirectedFlowNavUnder(Transform parent)
    {
        EnsureDirectedFlowMaterial();
        if (_directedFlowSharedMaterial == null)
            return null;

        var root = new GameObject("FlowNav").transform;
        root.SetParent(parent, false);

        var n = Mathf.Clamp(directedFlowPulseCount, 1, 4);
        var sc = Mathf.Max(0.02f, directedFlowOrbScale);
        for (var p = 0; p < n; p++)
        {
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = $"Pulse{p}";
            orb.transform.SetParent(root, false);
            orb.transform.localScale = Vector3.one * sc;
            DestroyColliderIfPresent(orb);
            var rend = orb.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.sharedMaterial = _directedFlowSharedMaterial;
                rend.shadowCastingMode = ShadowCastingMode.Off;
                rend.receiveShadows = false;
            }
        }

        return root;
    }

    Transform SpawnDirectedArrowUnder(Transform parent)
    {
        var root = new GameObject("DirectionMarker").transform;
        root.SetParent(parent, false);

        Material mat;
        if (directedArrowMaterial != null)
            mat = directedArrowMaterial;
        else
        {
            if (_directedArrowSharedMaterial != null)
                ApplyDirectedArrowMaterialLook(_directedArrowSharedMaterial);
            mat = _directedArrowSharedMaterial;
        }

        var headGo = new GameObject("ArrowHead");
        headGo.transform.SetParent(root, false);
        var mf = headGo.AddComponent<MeshFilter>();
        mf.sharedMesh = GetDirectedArrowHeadMesh();
        var mr = headGo.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;

        var br = Mathf.Max(0.01f, directedArrowHeadBaseRadius);
        var hl = Mathf.Max(0.02f, directedArrowHeadLength);
        headGo.transform.localScale = new Vector3(br * 2f, hl, br * 2f);

        var stemLen = directedArrowStemLength;
        var stemR = directedArrowStemRadius;
        if (stemLen > 0.002f && stemR > 0.002f)
        {
            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = "ArrowStem";
            stem.transform.SetParent(root, false);
            DestroyColliderIfPresent(stem);
            stem.transform.localScale = new Vector3(stemR * 2f, stemLen * 0.5f, stemR * 2f);
            stem.transform.localPosition = new Vector3(0f, -stemLen * 0.5f, 0f);
            var srend = stem.GetComponent<Renderer>();
            if (srend != null)
            {
                srend.sharedMaterial = mat;
                srend.shadowCastingMode = ShadowCastingMode.Off;
                srend.receiveShadows = false;
            }
        }

        return root;
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

            var full = nb.Position - na.Position;
            var len = full.magnitude;
            if (len < 1e-8f) continue;
            var dir3 = full / len;

            if (i < _edgeArrowMarkers.Count)
            {
                var tip = _edgeArrowMarkers[i];
                if (tip != null)
                {
                    var tipWorld = na.Position + full * Mathf.Clamp01(directedArrowTipNearT);
                    var hl = Mathf.Max(0.02f, directedArrowHeadLength);
                    tip.position = tipWorld - dir3 * hl;
                    tip.rotation = Quaternion.FromToRotation(Vector3.up, dir3);
                }
            }

            if (i >= _directedFlowRoots.Count) continue;
            var flowRoot = _directedFlowRoots[i];
            if (flowRoot == null) continue;
            if (!directedFlowNavEnabled)
            {
                flowRoot.gameObject.SetActive(false);
                continue;
            }

            flowRoot.gameObject.SetActive(true);

            var inset = Mathf.Min(directedFlowEndInset, len * 0.48f);
            var segStart = na.Position + dir3 * inset;
            var segEnd = nb.Position - dir3 * inset;
            var seg = segEnd - segStart;
            if (seg.sqrMagnitude < 1e-8f)
            {
                for (var c = 0; c < flowRoot.childCount; c++)
                    flowRoot.GetChild(c).position = na.Position;
                continue;
            }

            var invN = 1f / Mathf.Max(1, flowRoot.childCount);
            var edgePhase = i * directedFlowEdgePhaseSpread;
            var spd = directedFlowCyclesPerSecond;

            for (var c = 0; c < flowRoot.childCount; c++)
            {
                var u = Mathf.Repeat(Time.time * spd + edgePhase + c * invN, 1f);
                flowRoot.GetChild(c).position = segStart + seg * u;
            }
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
