using UnityEngine;

public class GraphManager : MonoBehaviour
{
    [SerializeField] GraphVisualizer visualizer;
    [SerializeField] float defaultGroundY;
    [Tooltip("도로 빌드 후 보행 시뮬 네트워크 갱신 (비우면 생략)")]
    [SerializeField] PedestrianCrowdSim pedestrianCrowdSim;

    [Header("Small-world API")]
    [SerializeField] SmallWorldApiClient apiClient;

    [Header("Auto-Generate")]
    [SerializeField] int autoVertexCount = 10;

    GraphData _graphData;
    readonly System.Collections.Generic.List<string> _parseWarnings = new();
    System.Collections.Generic.Dictionary<int, Vector3> _pendingNodePositions;
    GraphData _pendingFallbackData;

    public GraphData CurrentGraph => _graphData;
    public float DefaultGroundY => defaultGroundY;

    void Awake()
    {
        EnsureApiClient();
    }

    void OnEnable()
    {
        EnsureApiClient();

        if (apiClient != null)
        {
            apiClient.OnResponseReceived += OnSmallWorldResponse;
            apiClient.OnRequestFailed += OnSmallWorldFailed;
        }
    }

    void OnDisable()
    {
        if (apiClient != null)
        {
            apiClient.OnResponseReceived -= OnSmallWorldResponse;
            apiClient.OnRequestFailed -= OnSmallWorldFailed;
        }
    }

    public void BuildGraphFromInput(string inputText)
    {
        _parseWarnings.Clear();
        if (!GraphInputParser.TryParseUndirected(inputText, out var data, out var error, _parseWarnings))
        {
            Debug.LogWarning("[GraphManager] " + error);
            return;
        }

        LogParseWarnings();
        data.Directed = false;
        _graphData = data;
        pedestrianCrowdSim?.ClearAllAgents();
        visualizer.Clear();
        visualizer.BuildFromGraph(_graphData, defaultGroundY);
    }

    public void RequestSmallWorldFromInput(string inputText)
    {
        EnsureApiClient();

        _parseWarnings.Clear();
        if (!GraphInputParser.TryParseDirected(inputText, out var requestData, out var err, _parseWarnings))
        {
            Debug.LogWarning("[GraphManager] " + err);
            return;
        }
        LogParseWarnings();

        if (apiClient == null)
        {
            Debug.LogWarning("[GraphManager] SmallWorldApiClient를 찾을 수 없습니다.");
            return;
        }

        apiClient.SendRequest(requestData);
    }

    public void GeneratePlanarGraph()
    {
        EnsureApiClient();

        var gen = new PlanarGraphGenerator(autoVertexCount, PlanarGraphGenerator.DefaultRemoveRatio);
        var data = gen.Generate();

        // Store positions before sending to API (API response doesn't include positions).
        _pendingNodePositions = new System.Collections.Generic.Dictionary<int, Vector3>();
        foreach (var kv in data.Nodes)
            _pendingNodePositions[kv.Key] = kv.Value.Position;
        _pendingFallbackData = data;

        data.Directed = true;
        pedestrianCrowdSim?.ClearAllAgents();
        visualizer.Clear();

        if (apiClient != null)
        {
            apiClient.SendRequest(data);
        }
    }

    void OnSmallWorldResponse(GraphData result)
    {
        // Restore node positions from planar generator if available.
        if (_pendingNodePositions != null)
        {
            foreach (var kv in _pendingNodePositions)
            {
                if (result.Nodes.TryGetValue(kv.Key, out var node))
                    node.Position = kv.Value;
                else
                    result.Nodes[kv.Key] = new GraphNodeData(kv.Key, kv.Value);
            }
            _pendingNodePositions = null;
        }

        _graphData = result;
        pedestrianCrowdSim?.ClearAllAgents();
        visualizer.Clear();
        visualizer.BuildFromGraph(_graphData, defaultGroundY);
    }

    void OnSmallWorldFailed(string error)
    {
        Debug.LogWarning("[GraphManager] Small-world API 실패: " + error);

        if (_pendingFallbackData != null)
        {
            _graphData = _pendingFallbackData;
            _pendingFallbackData = null;
            _pendingNodePositions = null;
            pedestrianCrowdSim?.ClearAllAgents();
            visualizer.Clear();
            visualizer.BuildFromGraph(_graphData, defaultGroundY, keepExistingPositions: true);
        }
    }

    void EnsureApiClient()
    {
        if (apiClient != null)
            return;

        apiClient = GetComponent<SmallWorldApiClient>();
        if (apiClient == null)
            apiClient = gameObject.AddComponent<SmallWorldApiClient>();
    }

    void LogParseWarnings()
    {
        foreach (var warning in _parseWarnings)
            Debug.LogWarning("[GraphManager] " + warning);
    }

    public void ConfirmLayoutAndBuildRoads()
    {
        if (_graphData == null || _graphData.Nodes.Count == 0)
        {
            Debug.LogWarning("[GraphManager] 확정할 그래프가 없습니다.");
            return;
        }

        visualizer.SyncPositionsFromSceneToData(_graphData);
        visualizer.BuildRoads(_graphData, defaultGroundY);
        pedestrianCrowdSim?.OnRoadsBuilt(_graphData, defaultGroundY);
    }

    public void ClearAll()
    {
        _graphData = null;
        pedestrianCrowdSim?.ClearAllAgents();
        visualizer.Clear();
    }

}
