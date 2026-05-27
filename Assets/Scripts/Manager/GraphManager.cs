using System.Collections.Generic;
using System.Globalization;
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
        if (!TryParseGraph(inputText, out var data, out var error))
        {
            Debug.LogWarning("[GraphManager] " + error);
            return;
        }

        data.Directed = false;
        _graphData = data;
        pedestrianCrowdSim?.ClearAllAgents();
        visualizer.Clear();
        visualizer.BuildFromGraph(_graphData, defaultGroundY);
    }

    public void RequestSmallWorldFromInput(string inputText)
    {
        EnsureApiClient();

        if (!TryParseDirectedGraph(inputText, out var requestData, out var err))
        {
            Debug.LogWarning("[GraphManager] " + err);
            return;
        }

        if (apiClient == null)
        {
            Debug.LogWarning("[GraphManager] SmallWorldApiClient를 찾을 수 없습니다.");
            return;
        }

        apiClient.SendRequest(requestData);
    }

    public void GeneratePlanarGraph()
    {
        var gen = new PlanarGraphGenerator(autoVertexCount, PlanarGraphGenerator.DefaultRemoveRatio);
        var data = gen.Generate();

        _graphData = data;
        pedestrianCrowdSim?.ClearAllAgents();
        visualizer.Clear();
        visualizer.BuildFromGraph(_graphData, defaultGroundY, keepExistingPositions: true);
    }

    void OnSmallWorldResponse(GraphData result)
    {
        _graphData = result;
        pedestrianCrowdSim?.ClearAllAgents();
        visualizer.Clear();
        visualizer.BuildFromGraph(_graphData, defaultGroundY);
    }

    void OnSmallWorldFailed(string error)
    {
        Debug.LogWarning("[GraphManager] Small-world API 실패: " + error);
    }

    void EnsureApiClient()
    {
        if (apiClient != null)
            return;

        apiClient = GetComponent<SmallWorldApiClient>();
        if (apiClient == null)
            apiClient = gameObject.AddComponent<SmallWorldApiClient>();
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

    static bool TryParseGraph(string input, out GraphData data, out string error)
    {
        data = new GraphData();
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "입력이 비어 있습니다.";
            return false;
        }

        var edgeByPair = new Dictionary<(int min, int max), GraphEdgeData>();

        var lines = input.Split(new[] { '\r', '\n' }, System.StringSplitOptions.None);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var parts = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
            {
                error = $"한 줄에 숫자 3개가 필요합니다: \"{line}\"";
                return false;
            }

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var from) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var to))
            {
                error = $"노드 ID는 정수여야 합니다: \"{line}\"";
                return false;
            }

            if (!float.TryParse(parts[2], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var weight))
            {
                error = $"가중치를 읽을 수 없습니다: \"{line}\"";
                return false;
            }

            if (from == to)
            {
                Debug.LogWarning($"[GraphManager] 자기 자신과의 간선 무시: \"{line}\"");
                continue;
            }

            (int min, int max) key = from < to ? (from, to) : (to, from);
            if (edgeByPair.ContainsKey(key))
                Debug.LogWarning($"[GraphManager] 중복 간선 ({key.min}-{key.max}), 마지막 가중치로 덮어씁니다.");

            data.EnsureNode(from);
            data.EnsureNode(to);
            edgeByPair[key] = new GraphEdgeData(from, to, weight);
        }

        foreach (var e in edgeByPair.Values)
            data.Edges.Add(e);

        if (data.Nodes.Count == 0)
        {
            error = "유효한 간선이 없습니다.";
            return false;
        }

        return true;
    }

    static bool TryParseDirectedGraph(string input, out GraphData data, out string error)
    {
        data = new GraphData { Directed = true };
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "입력이 비어 있습니다.";
            return false;
        }

        var lines = input.Split(new[] { '\r', '\n' }, System.StringSplitOptions.None);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var parts = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
            {
                error = $"한 줄에 숫자 3개가 필요합니다: \"{line}\"";
                return false;
            }

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var from) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var to))
            {
                error = $"노드 ID는 정수여야 합니다: \"{line}\"";
                return false;
            }

            if (!float.TryParse(parts[2], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var weight))
            {
                error = $"가중치를 읽을 수 없습니다: \"{line}\"";
                return false;
            }

            if (from == to)
            {
                Debug.LogWarning($"[GraphManager] 자기 자신으로의 간선 무시: \"{line}\"");
                continue;
            }

            data.EnsureNode(from);
            data.EnsureNode(to);
            data.Edges.Add(new GraphEdgeData(from, to, weight));
        }

        if (data.Nodes.Count == 0)
        {
            error = "유효한 간선이 없습니다.";
            return false;
        }

        return true;
    }
}
