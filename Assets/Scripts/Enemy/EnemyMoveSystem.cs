using UnityEngine;
using UnityEngine.AI;

internal sealed class EnemyMoveSystem
{
    private readonly Transform owner;
    private readonly NavMeshAgent agent;
    private readonly Vector3 patrolOrigin;
    private readonly NavMeshPath patrolPath;
    private Transform detectedPlayer;
    private float detectionTimer;

    public EnemyMoveSystem(Transform owner)
    {
        this.owner = owner;
        agent = owner.GetComponent<NavMeshAgent>();
        patrolOrigin = owner.position;
        if (agent != null)
            patrolPath = new NavMeshPath();
    }

    public bool HasAgent => agent != null;
    private bool CanNavigate => agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;

    public void Stop()
    {
        if (CanNavigate)
            agent.isStopped = true;
    }

    public void Tick(bool canMove, float speed, LayerMask playerLayer, float stopDistance,
        float detectionDistance, float patrolDistance, float detectionInterval)
    {
        if (!CanNavigate)
        {
            detectedPlayer = null;
            detectionTimer = 0f;
            return;
        }

        if (!canMove)
        {
            Stop();
            return;
        }

        bool resumeMovement = agent.isStopped;
        agent.isStopped = false;
        if (agent.isOnOffMeshLink)
            return;

        detectionTimer -= Time.deltaTime;
        if (detectionTimer > 0f && !resumeMovement)
            return;
        detectionTimer = detectionInterval;

        bool wasChasing = !ReferenceEquals(detectedPlayer, null);
        detectedPlayer = FindPlayer(detectionDistance, playerLayer);
        agent.speed = speed;
        if (detectedPlayer != null)
        {
            agent.stoppingDistance = stopDistance;
            if (!agent.SetDestination(detectedPlayer.position))
                agent.ResetPath();
            return;
        }

        agent.stoppingDistance = 0.1f;
        if (wasChasing || resumeMovement)
            agent.ResetPath();

        if (agent.pathPending ||
            (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathComplete &&
             agent.remainingDistance > agent.stoppingDistance + 0.05f))
            return;

        TryStartPatrol(patrolDistance);
    }

    private Transform FindPlayer(float detectionDistance, LayerMask playerLayer)
    {
        Transform nearestPlayer = null;
        float nearestDistance = float.PositiveInfinity;
        Vector3 origin = owner.position;
        foreach (Collider collider in Physics.OverlapSphere(origin, detectionDistance, playerLayer))
        {
            Transform candidate = collider.transform;
            if (candidate.IsChildOf(owner))
                continue;

            float distance = (candidate.position - origin).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestPlayer = candidate;
            }
        }
        return nearestPlayer;
    }

    private void TryStartPatrol(float patrolDistance)
    {
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector3 offset = Random.insideUnitSphere * patrolDistance;
            Vector3 candidate = patrolOrigin + offset;
            if (NavMesh.SamplePosition(candidate, out var hit, 2f, filter)
                && agent.CalculatePath(hit.position, patrolPath)
                && patrolPath.status == NavMeshPathStatus.PathComplete
                && agent.SetPath(patrolPath))
                return;
        }
    }

    public void MoveWithoutAgent(Vector3 forward, float distance, float circleRadius)
    {
        owner.position += Vector3.ProjectOnPlane(forward, Vector3.up).normalized * distance;
        if (circleRadius > 0.1f)
            owner.Rotate(0f, distance / circleRadius * Mathf.Rad2Deg, 0f, Space.World);
    }
}
