using System;

[Serializable]
public class SmallWorldEdgeJson
{
    public int[] vertices;
    public float weight;
}

[Serializable]
public class SmallWorldRequestJson
{
    public SmallWorldEdgeJson[] edges;
}

[Serializable]
public class SmallWorldResponseBody
{
    public SmallWorldResponseEdgeDto[] edges;
    public float optimized_graph_score;
    public float bidirectional_graph_score;
}

[Serializable]
public class SmallWorldResponseEdgeDto
{
    public int _from;
    public int to;
}
