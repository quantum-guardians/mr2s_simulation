using System.Collections.Generic;
using UnityEngine;

public class GraphNodeData
{
    public int Id;
    public Vector3 Position;

    public GraphNodeData(int id, Vector3 position)
    {
        Id = id;
        Position = position;
    }
}

public class GraphEdgeData
{
    public int From;
    public int To;
    public float Weight;

    public GraphEdgeData(int from, int to, float weight)
    {
        From = from;
        To = to;
        Weight = weight;
    }
}

public class GraphData
{
    public Dictionary<int, GraphNodeData> Nodes = new();
    public List<GraphEdgeData> Edges = new();
}
