using System;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public class SmallWorldApiClient : MonoBehaviour
{
    [SerializeField] string apiUrl = "https://quantum.yunseong.dev/api/v1/mr2s";

    public bool IsRequestInProgress { get; private set; }

    public event Action<GraphData> OnResponseReceived;
    public event Action<string> OnRequestFailed;

    public void SendRequest(GraphData directedInput)
    {
        if (IsRequestInProgress)
        {
            Debug.LogWarning("[SmallWorldApiClient] 이미 요청 중입니다.");
            return;
        }

        if (directedInput == null || directedInput.Edges.Count == 0)
        {
            Debug.LogWarning("[SmallWorldApiClient] 전송할 간선이 없습니다.");
            return;
        }

        StartCoroutine(PostRoutine(directedInput));
    }

    IEnumerator PostRoutine(GraphData directedInput)
    {
        IsRequestInProgress = true;
        var json = BuildRequestJson(directedInput);
        var url = string.IsNullOrWhiteSpace(apiUrl)
            ? "http://localhost:8000/api/v1/mr2s"
            : apiUrl.Trim();

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
            var errorMsg = $"Small-world 요청 실패: {req.error} / HTTP {req.responseCode}\n{req.downloadHandler.text}";
            Debug.LogWarning("[SmallWorldApiClient] " + errorMsg);
            IsRequestInProgress = false;
            OnRequestFailed?.Invoke(errorMsg);
            yield break;
        }

        var text = req.downloadHandler.text;
        if (!TryParseResponseBody(text, out var result, out var parseErr))
        {
            var errorMsg = "응답 파싱 실패: " + parseErr + "\n본문:\n" + text;
            Debug.LogWarning("[SmallWorldApiClient] " + errorMsg);
            IsRequestInProgress = false;
            OnRequestFailed?.Invoke(errorMsg);
            yield break;
        }

        IsRequestInProgress = false;
        OnResponseReceived?.Invoke(result);
    }

    static string BuildRequestJson(GraphData directed)
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

    static bool TryParseResponseBody(string body, out GraphData data, out string error)
    {
        data = new GraphData { Directed = true };
        error = null;

        if (string.IsNullOrWhiteSpace(body))
        {
            error = "응답 본문이 비어 있습니다.";
            return false;
        }

        body = body.Trim().TrimStart('\uFEFF');

        if (body.StartsWith("{", StringComparison.Ordinal))
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
                    data.EnsureNode(from);
                    data.EnsureNode(to);
                    data.Edges.Add(new GraphEdgeData(from, to, 1f));
                }

                if (data.Edges.Count > 0)
                {
                    error = null;
                    return true;
                }
            }

            TryParseEdgesRegex(body, data);
            if (data.Edges.Count > 0)
            {
                error = null;
                return true;
            }

            error = "JSON 응답에서 간선을 읽지 못했습니다. (edges / _from, to 형식 확인)";
            return false;
        }

        if (TryParseResponsePlainLines(body, data, out error))
            return true;

        if (data.Edges.Count == 0)
            error ??= "응답에서 유효한 간선을 찾지 못했습니다.";
        return data.Edges.Count > 0;
    }

    static void TryParseEdgesRegex(string json, GraphData data)
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
            data.EnsureNode(from);
            data.EnsureNode(to);
            data.Edges.Add(new GraphEdgeData(from, to, w));
        }
    }

    static bool TryParseResponsePlainLines(string body, GraphData data, out string error)
    {
        error = null;
        data.Edges.Clear();
        data.Nodes.Clear();

        var lines = body.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line == "{" || line == "}") continue;
            if (line.StartsWith("{", StringComparison.Ordinal)) continue;

            var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
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

            data.EnsureNode(from);
            data.EnsureNode(to);
            data.Edges.Add(new GraphEdgeData(from, to, weight));
        }

        return data.Edges.Count > 0;
    }
}
