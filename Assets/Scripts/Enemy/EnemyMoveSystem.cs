using UnityEngine;
using UnityEngine.AI;

internal sealed class EnemyMoveSystem
{
    private readonly Transform owner;
    private readonly NavMeshAgent agent;
    private readonly Vector3 patrolOrigin;
    private readonly NavMeshPath patrolPath;
    private float detectionTimer;

    public bool IsPlayerInRange { get; private set; }

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

    // player: dragged-in player transform (can be null)
    public void Tick(Transform player, float speed, float stopDistance,
        float patrolDistance, float detectionInterval)
    {
        IsPlayerInRange = false;

        if (!CanNavigate) { detectionTimer = 0f; return; }

        agent.speed = speed;

        if (player != null)
        {
            float dist = Vector3.Distance(owner.position, player.position);
            if (dist <= stopDistance)
            {
                // Stop and face player
                IsPlayerInRange = true;
                agent.isStopped = true;
                Vector3 dir = Vector3.ProjectOnPlane(player.position - owner.position, Vector3.up);
                if (dir.sqrMagnitude > 0.01f)
                    owner.rotation = Quaternion.LookRotation(dir);
                return;
            }
        }

        // Resume patrol
        agent.isStopped = false;
        if (agent.isOnOffMeshLink) return;

        detectionTimer -= Time.deltaTime;
        if (detectionTimer > 0f) return;
        detectionTimer = detectionInterval;

        if (agent.pathPending ||
            (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathComplete &&
             agent.remainingDistance > agent.stoppingDistance + 0.05f))
            return;

        TryStartPatrol(patrolDistance);
    }

    private void TryStartPatrol(float patrolDistance)
    {
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector3 offset = Random.insideUnitSphere * patrolDistance;
            offset.y = 0f;
            Vector3 candidate = patrolOrigin + offset;
            if (NavMesh.SamplePosition(candidate, out var hit, 2f, filter)
                && agent.CalculatePath(hit.position, patrolPath)
                && patrolPath.status == NavMeshPathStatus.PathComplete
                && agent.SetPath(patrolPath))
                return;
        }
    }

    // Legacy overload used by MonsterController (layer-based detection)
    public void Tick(bool canMove, float speed, LayerMask playerLayer, float stopDistance,
        float detectionDistance, float patrolDistance, float detectionInterval)
    {
        if (!CanNavigate) { IsPlayerInRange = false; detectionTimer = 0f; return; }
        if (!canMove) { IsPlayerInRange = false; Stop(); return; }

        agent.isStopped = false;
        if (agent.isOnOffMeshLink) return;

        detectionTimer -= Time.deltaTime;
        if (detectionTimer > 0f) return;
        detectionTimer = detectionInterval;

        Transform nearest = FindNearest(detectionDistance, playerLayer);
        agent.speed = speed;

        if (nearest != null)
        {
            float dist = Vector3.Distance(owner.position, nearest.position);
            IsPlayerInRange = dist <= stopDistance;
            agent.stoppingDistance = stopDistance;
            if (IsPlayerInRange)
            {
                agent.isStopped = true;
                Vector3 dir = Vector3.ProjectOnPlane(nearest.position - owner.position, Vector3.up);
                if (dir.sqrMagnitude > 0.01f) owner.rotation = Quaternion.LookRotation(dir);
            }
            else
            {
                if (!agent.SetDestination(nearest.position)) agent.ResetPath();
            }
            return;
        }

        IsPlayerInRange = false;
        agent.stoppingDistance = 0.1f;
        TryStartPatrol(patrolDistance);
    }

    private Transform FindNearest(float detectionDistance, LayerMask playerLayer)
    {
        Transform nearest = null;
        float nearestDist = float.PositiveInfinity;
        foreach (Collider col in Physics.OverlapSphere(owner.position, detectionDistance, playerLayer))
        {
            if (col.transform.IsChildOf(owner)) continue;
            float d = (col.transform.position - owner.position).sqrMagnitude;
            if (d < nearestDist) { nearestDist = d; nearest = col.transform; }
        }
        return nearest;
    }

    public void MoveWithoutAgent(Vector3 forward, float distance, float circleRadius)
    {
        owner.position += Vector3.ProjectOnPlane(forward, Vector3.up).normalized * distance;
        if (circleRadius > 0.1f)
            owner.Rotate(0f, distance / circleRadius * Mathf.Rad2Deg, 0f, Space.World);
    }
}