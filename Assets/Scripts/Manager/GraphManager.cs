using System.Collections;
using System.Globalization;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public class GraphManager : MonoBehaviour
{
    [SerializeField] GraphVisualizer visualizer;
    [SerializeField] float defaultGroundY;
    [Tooltip("도로 빌드 후 보행 시뮬 네트워크 갱신 (비우면 생략)")]
    [SerializeField] PedestrianCrowdSim pedestrianCrowdSim;

    [Header("Small-world API")]
    [SerializeField] string smallWorldApiUrl = "https://quantum.yunseong.dev/api/v1/optimize/small-world";

    GraphData _graphData;

    public GraphData CurrentGraph => _graphData;
    public float DefaultGroundY => defaultGroundY;

    public bool IsRequestInProgress { get; private set; }

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
        if (IsRequestInProgress)
        {
            Debug.LogWarning("[GraphManager] 이미 요청 중입니다.");
            return;
        }

        if (!TryParseDirectedGraph(inputText, out var requestData, out var err))
        {
            Debug.LogWarning("[GraphManager] " + err);
            return;
        }

        if (requestData.Edges.Count == 0)
        {
            Debug.LogWarning("[GraphManager] 전송할 간선이 없습니다.");
            return;
        }

        StartCoroutine(SmallWorldPostRoutine(requestData));
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

            EnsureNode(data, from);
            EnsureNode(data, to);
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

            EnsureNode(data, from);
            EnsureNode(data, to);
            data.Edges.Add(new GraphEdgeData(from, to, weight));
        }

        if (data.Nodes.Count == 0)
        {
            error = "유효한 간선이 없습니다.";
            return false;
        }

        return true;
    }

    static bool TryParseSmallWorldResponseBody(string body, out GraphData data, out string error)
    {
        data = new GraphData { Directed = true };
        error = null;

        if (string.IsNullOrWhiteSpace(body))
        {
            error = "응답 본문이 비어 있습니다.";
            return false;
        }

        body = body.Trim().TrimStart('\uFEFF');

        if (body.StartsWith("{", System.StringComparison.Ordinal))
        {
            var dto = JsonUtility.FromJson<SmallWorldResponseBody>(body);
            if (dto?.edges != null && dto.edges.Length > 0)
            {
                foreach (var e in dto.edges)
                {
                    if (e == null) continue;
                    var from = e._from;
                    var to = e.to;
                    if (from == to) continue;
                    var w = e.weight;
                    if (w == 0f)
                        w = 1f;
                    EnsureNode(data, from);
                    EnsureNode(data, to);
                    data.Edges.Add(new GraphEdgeData(from, to, w));
                }

                if (data.Edges.Count > 0)
                {
                    error = null;
                    return true;
                }
            }

            TryParseSmallWorldEdgesRegex(body, data);
            if (data.Edges.Count > 0)
            {
                error = null;
                return true;
            }

            error = "JSON 응답에서 간선을 읽지 못했습니다. (edges / _from, to 형식 확인)";
            return false;
        }

        if (TryParseSmallWorldResponsePlainLines(body, data, out error))
            return true;

        if (data.Edges.Count == 0)
            error ??= "응답에서 유효한 간선을 찾지 못했습니다.";
        return data.Edges.Count > 0;
    }

    static void TryParseSmallWorldEdgesRegex(string json, GraphData data)
    {
        data.Edges.Clear();
        data.Nodes.Clear();

        var rx = new Regex("\"_from\"\\s*:\\s*(\\d+)\\s*,\\s*\"to\"\\s*:\\s*(\\d+)(?:\\s*,\\s*\"weight\"\\s*:\\s*([0-9.eE+-]+))?",
            RegexOptions.CultureInvariant);
        foreach (Match m in rx.Matches(json))
        {
            if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var from) ||
                !int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var to))
                continue;
            if (from == to) continue;
            var w = 1f;
            if (m.Groups[3].Success &&
                float.TryParse(m.Groups[3].Value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed))
                w = parsed;
            EnsureNode(data, from);
            EnsureNode(data, to);
            data.Edges.Add(new GraphEdgeData(from, to, w));
        }
    }

    static bool TryParseSmallWorldResponsePlainLines(string body, GraphData data, out string error)
    {
        error = null;
        data.Edges.Clear();
        data.Nodes.Clear();

        var lines = body.Split(new[] { '\r', '\n' }, System.StringSplitOptions.None);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line == "{" || line == "}") continue;
            if (line.StartsWith("{", System.StringComparison.Ordinal)) continue;

            var parts = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
            {
                error = $"응답 줄 형식 오류 (from to weight 필요): \"{line}\"";
                return false;
            }

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var from) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var to))
            {
                error = $"응답 노드 ID 오류: \"{line}\"";
                return false;
            }

            if (!float.TryParse(parts[2], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var weight))
            {
                error = $"응답 가중치 오류: \"{line}\"";
                return false;
            }

            if (from == to) continue;

            EnsureNode(data, from);
            EnsureNode(data, to);
            data.Edges.Add(new GraphEdgeData(from, to, weight));
        }

        return data.Edges.Count > 0;
    }

    IEnumerator SmallWorldPostRoutine(GraphData directedInput)
    {
        IsRequestInProgress = true;
        var json = BuildSmallWorldRequestJson(directedInput);
        var url = string.IsNullOrWhiteSpace(smallWorldApiUrl)
            ? "https://quantum.yunseong.dev/api/v1/optimize/small-world"
            : smallWorldApiUrl.Trim();

        using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        var bodyRaw = Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
        var ok = req.result == UnityWebRequest.Result.Success;
#else
        var ok = !req.isNetworkError && !req.isHttpError;
#endif
        if (!ok)
        {
            Debug.LogWarning($"[GraphManager] Small-world 요청 실패: {req.error} / HTTP {req.responseCode}\n{req.downloadHandler.text}");
            IsRequestInProgress = false;
            yield break;
        }

        var text = req.downloadHandler.text;
        if (!TryParseSmallWorldResponseBody(text, out var result, out var parseErr))
        {
            Debug.LogWarning("[GraphManager] 응답 파싱 실패: " + parseErr + "\n본문:\n" + text);
            IsRequestInProgress = false;
            yield break;
        }

        _graphData = result;
        pedestrianCrowdSim?.ClearAllAgents();
        visualizer.Clear();
        visualizer.BuildFromGraph(_graphData, defaultGroundY);
        IsRequestInProgress = false;
    }

    static string BuildSmallWorldRequestJson(GraphData directed)
    {
        var arr = new SmallWorldEdgeJson[directed.Edges.Count];
        for (var i = 0; i < directed.Edges.Count; i++)
        {
            var e = directed.Edges[i];
            arr[i] = new SmallWorldEdgeJson
            {
                vertices = new[] { e.From, e.To },
                weight = e.Weight
            };
        }

        var wrap = new SmallWorldRequestJson { edges = arr };
        return JsonUtility.ToJson(wrap);
    }

    static void EnsureNode(GraphData data, int id)
    {
        if (!data.Nodes.ContainsKey(id))
            data.Nodes[id] = new GraphNodeData(id, Vector3.zero);
    }
}
