using System.Globalization;
using System.Collections.Generic;
using UnityEngine;

public class GraphManager : MonoBehaviour
{
    [SerializeField] GraphVisualizer visualizer;
    [SerializeField] float defaultGroundY;

    GraphData _graphData;

    public GraphData CurrentGraph => _graphData;

    public void BuildGraphFromInput(string inputText)
    {
        if (!TryParseGraph(inputText, out var data, out var error))
        {
            Debug.LogWarning("[GraphManager] " + error);
            return;
        }

        _graphData = data;
        visualizer.Clear();
        visualizer.BuildFromGraph(_graphData, defaultGroundY);
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
    }

    public void ClearAll()
    {
        _graphData = null;
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

    static void EnsureNode(GraphData data, int id)
    {
        if (!data.Nodes.ContainsKey(id))
            data.Nodes[id] = new GraphNodeData(id, Vector3.zero);
    }
}
