using System.Collections.Generic;
using UnityEngine;

public class PlanarGraphGenerator
{
    public const int DefaultVertexCount = 10;
    public const float DefaultRemoveRatio = 0.5f;
    public const int DefaultSeed = 41;
    public const float SpreadPerVertex = 2.2f;
    public const float MinEdgeAngleDeg = 35f;

    readonly int _vertexCount;
    readonly float _removeRatio;
    readonly System.Random _rng;

    public PlanarGraphGenerator(int vertexCount = DefaultVertexCount, float removeRatio = DefaultRemoveRatio)
    {
        _vertexCount = Mathf.Max(3, vertexCount);
        _removeRatio = Mathf.Clamp(removeRatio, 0f, 0.8f);
        _rng = new System.Random(DefaultSeed);
    }

    public PlanarGraphGenerator(int vertexCount, float removeRatio, int seed)
    {
        _vertexCount = Mathf.Max(3, vertexCount);
        _removeRatio = Mathf.Clamp(removeRatio, 0f, 0.8f);
        _rng = new System.Random(seed);
    }

    public GraphData Generate()
    {
        var points = GenerateUniformPoints();
        var edges = DelaunayTriangulation(points);
        var keptEdges = RemoveEdgesMaintainingBiconnectivity(edges);
        keptEdges = FilterMinAngle(points, keptEdges);
        return BuildGraphData(points, keptEdges);
    }

    List<Vector2> GenerateUniformPoints()
    {
        var points = new List<Vector2>(_vertexCount);
        var spread = Mathf.Sqrt(_vertexCount) * SpreadPerVertex;
        var innerCount = Mathf.Max(1, _vertexCount / 3);
        var midCount = Mathf.Max(1, (_vertexCount - innerCount) / 2);
        var outerCount = _vertexCount - innerCount - midCount;

        void AddRing(int count, float radius, float jitter)
        {
            var angleOff = (float)_rng.NextDouble() * Mathf.PI * 2f;
            for (var i = 0; i < count; i++)
            {
                var angle = angleOff + (float)i / count * Mathf.PI * 2f
                            + (float)(_rng.NextDouble() - 0.5) * jitter;
                var r = radius * (0.82f + (float)_rng.NextDouble() * 0.36f);
                points.Add(new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r));
            }
        }

        AddRing(innerCount, spread * 0.25f, 0.5f);
        AddRing(midCount,   spread * 0.52f, 0.4f);
        AddRing(outerCount, spread * 0.80f, 0.3f);

        Shuffle(points);
        return points;
    }

    static GraphData BuildGraphData(List<Vector2> points, List<(int from, int to)> edges)
    {
        var data = new GraphData();
        for (var i = 0; i < points.Count; i++)
        {
            data.Nodes[i] = new GraphNodeData(i, new Vector3(points[i].x, 0f, points[i].y));
        }

        foreach (var e in edges)
        {
            var dist = Vector2.Distance(points[e.from], points[e.to]);
            data.Edges.Add(new GraphEdgeData(e.from, e.to, dist));
        }

        return data;
    }

    #region Delaunay Triangulation

    List<(int from, int to)> DelaunayTriangulation(List<Vector2> points)
    {
        var n = points.Count;
        if (n < 3)
        {
            var r = new List<(int, int)>();
            for (var i = 0; i < n; i++)
                for (var j = i + 1; j < n; j++)
                    r.Add((i, j));
            return r;
        }

        (float minX, float minY, float maxX, float maxY) = Bounds(points);
        var dx = maxX - minX;
        var dy = maxY - minY;
        var dmax = Mathf.Max(dx, dy) * 5f;
        var midX = (minX + maxX) * 0.5f;
        var midY = (minY + maxY) * 0.5f;

        var s0 = new Vector2(midX - dmax, midY - dmax);
        var s1 = new Vector2(midX + dmax, midY - dmax);
        var s2 = new Vector2(midX, midY + dmax);

        var sIdx0 = n;
        var sIdx1 = n + 1;
        var sIdx2 = n + 2;

        var triangles = new List<DelaunayTriangle> { new(s0, s1, s2, sIdx0, sIdx1, sIdx2) };

        for (var pi = 0; pi < n; pi++)
        {
            var p = points[pi];
            var bad = new List<int>();

            for (var ti = 0; ti < triangles.Count; ti++)
            {
                if (InCircumcircle(triangles[ti], p))
                    bad.Add(ti);
            }

            var holeEdges = new List<(int a, int b)>();
            for (var bi = 0; bi < bad.Count; bi++)
            {
                var tri = triangles[bad[bi]];
                AddHoleEdge(holeEdges, tri.AIdx, tri.BIdx);
                AddHoleEdge(holeEdges, tri.BIdx, tri.CIdx);
                AddHoleEdge(holeEdges, tri.CIdx, tri.AIdx);
            }

            bad.Sort((x, y) => y.CompareTo(x));
            foreach (var bi in bad)
                triangles.RemoveAt(bi);

            foreach (var he in holeEdges)
            {
                triangles.Add(new DelaunayTriangle(
                    GetVertexPoint(points, s0, s1, s2, he.a),
                    GetVertexPoint(points, s0, s1, s2, he.b),
                    p,
                    he.a, he.b, pi));
            }
        }

        var result = new HashSet<(int, int)>();
        foreach (var tri in triangles)
        {
            if (tri.IsConnectedToSuper(n)) continue;

            result.Add(NormalizeEdge(tri.AIdx, tri.BIdx));
            result.Add(NormalizeEdge(tri.BIdx, tri.CIdx));
            result.Add(NormalizeEdge(tri.CIdx, tri.AIdx));
        }

        return new List<(int, int)>(result);
    }

    static (int, int) NormalizeEdge(int a, int b) => a < b ? (a, b) : (b, a);

    static Vector2 GetVertexPoint(List<Vector2> points, Vector2 s0, Vector2 s1, Vector2 s2, int idx)
    {
        var n = points.Count;
        if (idx < n) return points[idx];
        if (idx == n) return s0;
        if (idx == n + 1) return s1;
        return s2;
    }

    static void AddHoleEdge(List<(int a, int b)> edges, int a, int b)
    {
        var fwd = (a, b);
        var rev = (b, a);

        for (var i = 0; i < edges.Count; i++)
        {
            if (edges[i].Equals(rev))
            {
                edges.RemoveAt(i);
                return;
            }
        }

        edges.Add(fwd);
    }

    static bool InCircumcircle(DelaunayTriangle tri, Vector2 p)
    {
        var ax = tri.A.x - p.x;
        var ay = tri.A.y - p.y;
        var bx = tri.B.x - p.x;
        var by = tri.B.y - p.y;
        var cx = tri.C.x - p.x;
        var cy = tri.C.y - p.y;

        var det = ax * (by * (cx * cx + cy * cy) - (bx * bx + by * by) * cy)
                - ay * (bx * (cx * cx + cy * cy) - (bx * bx + by * by) * cx)
                + (ax * ax + ay * ay) * (bx * cy - by * cx);

        return det > 0f;
    }

    static (float minX, float minY, float maxX, float maxY) Bounds(List<Vector2> points)
    {
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;

        foreach (var p in points)
        {
            if (p.x < minX) minX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.x > maxX) maxX = p.x;
            if (p.y > maxY) maxY = p.y;
        }

        return (minX, minY, maxX, maxY);
    }

    struct DelaunayTriangle
    {
        public Vector2 A, B, C;
        public int AIdx, BIdx, CIdx;

        public DelaunayTriangle(Vector2 a, Vector2 b, Vector2 c, int ai, int bi, int ci)
        {
            A = a; B = b; C = c;
            AIdx = ai; BIdx = bi; CIdx = ci;
        }

        public bool IsConnectedToSuper(int vertexCount)
        {
            return AIdx >= vertexCount || BIdx >= vertexCount || CIdx >= vertexCount;
        }
    }

    #endregion

    #region Min-Angle Filter

    List<(int from, int to)> FilterMinAngle(List<Vector2> points, List<(int from, int to)> edges)
    {
        var minAngleRad = MinEdgeAngleDeg * Mathf.Deg2Rad;
        var active = new HashSet<(int, int)>(edges);
        var adj = BuildAdjacency(points.Count, active);

        var candidates = new List<((int from, int to) edge, float angle)>();
        for (var u = 0; u < _vertexCount; u++)
        {
            var nbrs = adj[u];
            if (nbrs.Count < 2) continue;

            var angled = new List<(int v, float ang)>(nbrs.Count);
            foreach (var v in nbrs)
            {
                var d = points[v] - points[u];
                angled.Add((v, Mathf.Atan2(d.y, d.x)));
            }
            angled.Sort((a, b) => a.ang.CompareTo(b.ang));

            for (var i = 0; i < angled.Count; i++)
            {
                var j = (i + 1) % angled.Count;
                var angle = angled[j].ang - angled[i].ang;
                if (i == angled.Count - 1) angle += Mathf.PI * 2f;
                if (angle < minAngleRad && angle > 0.001f)
                {
                    var e1 = NormalizeEdge(u, angled[i].v);
                    var e2 = NormalizeEdge(u, angled[j].v);
                    var len1 = Vector2.Distance(points[e1.Item1], points[e1.Item2]);
                    var len2 = Vector2.Distance(points[e2.Item1], points[e2.Item2]);
                    var shorter = len1 <= len2 ? e1 : e2;
                    candidates.Add((shorter, angle));
                }
            }
        }

        candidates.Sort((a, b) => a.angle.CompareTo(b.angle));

        foreach (var (edge, _) in candidates)
        {
            if (!active.Contains(edge)) continue;

            active.Remove(edge);
            if (!IsBiconnected(active, _vertexCount))
                active.Add(edge);
        }

        return new List<(int, int)>(active);
    }

    #endregion

    #region Biconnectivity & Edge Removal

    List<(int from, int to)> RemoveEdgesMaintainingBiconnectivity(List<(int from, int to)> edges)
    {
        var candidates = new List<(int from, int to)>(edges);
        Shuffle(candidates);

        var active = new HashSet<(int, int)>(edges);
        var targetRemove = (int)(edges.Count * _removeRatio);
        var removed = 0;

        foreach (var e in candidates)
        {
            if (removed >= targetRemove) break;

            active.Remove(e);
            if (IsBiconnected(active, _vertexCount))
            {
                removed++;
            }
            else
            {
                active.Add(e);
            }
        }

        return new List<(int, int)>(active);
    }

    void Shuffle<T>(List<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    static bool IsBiconnected(HashSet<(int, int)> edges, int vertexCount)
    {
        var adj = BuildAdjacency(vertexCount, edges);

        var visited = new bool[vertexCount];
        var disc = new int[vertexCount];
        var low = new int[vertexCount];
        var parent = new int[vertexCount];
        var isArt = new bool[vertexCount];
        var time = 0;

        for (var i = 0; i < vertexCount; i++)
            parent[i] = -1;

        DfsArticulation(0, adj, visited, disc, low, parent, isArt, ref time);

        for (var i = 0; i < vertexCount; i++)
        {
            if (!visited[i])
                return false;
        }

        for (var i = 0; i < vertexCount; i++)
        {
            if (isArt[i])
                return false;
        }

        return true;
    }

    static List<int>[] BuildAdjacency(int vertexCount, HashSet<(int, int)> edges)
    {
        var adj = new List<int>[vertexCount];
        for (var i = 0; i < vertexCount; i++)
            adj[i] = new List<int>();

        foreach (var e in edges)
        {
            adj[e.Item1].Add(e.Item2);
            adj[e.Item2].Add(e.Item1);
        }

        return adj;
    }

    static void DfsArticulation(int u, List<int>[] adj, bool[] visited, int[] disc, int[] low,
        int[] parent, bool[] isArt, ref int time)
    {
        visited[u] = true;
        disc[u] = low[u] = ++time;
        var children = 0;

        foreach (var v in adj[u])
        {
            if (!visited[v])
            {
                children++;
                parent[v] = u;
                DfsArticulation(v, adj, visited, disc, low, parent, isArt, ref time);
                low[u] = Mathf.Min(low[u], low[v]);

                if (parent[u] == -1 && children > 1)
                    isArt[u] = true;
                if (parent[u] != -1 && low[v] >= disc[u])
                    isArt[u] = true;
            }
            else if (v != parent[u])
            {
                low[u] = Mathf.Min(low[u], disc[v]);
            }
        }
    }

    #endregion
}
