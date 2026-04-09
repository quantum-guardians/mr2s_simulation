using UnityEngine;

/// <summary>도로 위 보행용 한 방향 구간 (월드 좌표, Y는 포장면).</summary>
public struct WalkSegment
{
    public Vector3 A;
    public Vector3 B;
    public int FromNode;
    public int ToNode;
    public float Length;
}
