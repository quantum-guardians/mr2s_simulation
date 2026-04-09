using UnityEngine;

/// <summary>보행자 인스턴스 — 레이캐스트 선택용.</summary>
[RequireComponent(typeof(Collider))]
public class PedestrianUnit : MonoBehaviour
{
    public int AgentId { get; private set; }
    public PedestrianCrowdSim Crowd { get; private set; }

    public void Initialize(PedestrianCrowdSim crowd, int agentId)
    {
        Crowd = crowd;
        AgentId = agentId;
    }
}
