using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 도로 교차점(그래프 노드)을 정점으로 하는 A* 탐색.
/// </summary>
public static class RoadPathfinding
{
    public static bool TryFindPath(
        int start,
        int goal,
        Dictionary<int, List<(int to, float cost)>> edges,
        Dictionary<int, Vector3> nodePositions,
        out List<int> path)
    {
        path = null;
        if (!nodePositions.ContainsKey(start) || !nodePositions.ContainsKey(goal))
            return false;

        if (start == goal)
        {
            path = new List<int> { start };
            return true;
        }

        var open = new HashSet<int>();
        var closed = new HashSet<int>();
        var gScore = new Dictionary<int, float>();
        var cameFrom = new Dictionary<int, int>();
        var fScore = new Dictionary<int, float>();

        foreach (var id in nodePositions.Keys)
            gScore[id] = float.PositiveInfinity;

        gScore[start] = 0f;
        var h0 = Heuristic(start, goal, nodePositions);
        fScore[start] = h0;
        open.Add(start);

        while (open.Count > 0)
        {
            var current = TakeLowestF(open, fScore);
            open.Remove(current);

            if (current == goal)
            {
                path = Reconstruct(cameFrom, start, goal);
                return path != null && path.Count > 0;
            }

            closed.Add(current);

            if (!edges.TryGetValue(current, out var nbrs))
                continue;

            foreach (var (to, cost) in nbrs)
            {
                if (closed.Contains(to))
                    continue;

                var tentative = gScore[current] + cost;
                if (tentative < gScore[to])
                {
                    cameFrom[to] = current;
                    gScore[to] = tentative;
                    fScore[to] = tentative + Heuristic(to, goal, nodePositions);
                    open.Add(to);
                }
            }
        }

        return false;
    }

    static float Heuristic(int a, int b, Dictionary<int, Vector3> pos)
    {
        var pa = pos[a];
        var pb = pos[b];
        return new Vector2(pa.x - pb.x, pa.z - pb.z).magnitude;
    }

    static int TakeLowestF(HashSet<int> open, Dictionary<int, float> fScore)
    {
        var best = -1;
        var bestF = float.PositiveInfinity;
        foreach (var id in open)
        {
            if (!fScore.TryGetValue(id, out var f))
                f = float.PositiveInfinity;
            if (f < bestF)
            {
                bestF = f;
                best = id;
            }
        }

        return best;
    }

    static List<int> Reconstruct(Dictionary<int, int> cameFrom, int start, int goal)
    {
        var path = new List<int>();
        var c = goal;
        var guard = 0;
        const int maxGuard = 4096;
        while (c != start && guard++ < maxGuard)
        {
            path.Add(c);
            if (!cameFrom.TryGetValue(c, out c))
                return null;
        }

        if (c != start)
            return null;

        path.Add(start);
        path.Reverse();
        return path;
    }
}
