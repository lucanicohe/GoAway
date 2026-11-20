using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyController : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private LayerMask playerLayer;

    [Header("Settings")]
    [SerializeField] private float stopDistance = 2f;
    [SerializeField] private float detectionDistance = 20f;
    [SerializeField] private float patrolDistance = 10f;
    [SerializeField] private Transform[] patrolPoints = Array.Empty<Transform>();

    private NavMeshAgent agent;
    private int nextPatrolPoint;
    private bool isChasingPlayer;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateUpAxis = true;
    }

    private IEnumerator Start()
    {
        var interval = new WaitForSeconds(0.25f);
        while (true)
        {
            if (agent.isOnNavMesh && !agent.isOnOffMeshLink)
            {
                UpdateDestination();
            }
            yield return interval;
        }
    }

    private void UpdateDestination()
    {
        if (Physics.SphereCast(transform.position, detectionDistance, Vector3.down,
                out RaycastHit hit, detectionDistance, playerLayer))
        {
            isChasingPlayer = true;
            agent.stoppingDistance = stopDistance;
            agent.isStopped = Vector3.Distance(transform.position, hit.transform.position) <= stopDistance;
            if (!agent.isStopped)
                agent.SetDestination(hit.transform.position);
            return;
        }

        if (isChasingPlayer)
        {
            agent.ResetPath();
            isChasingPlayer = false;
        }

        agent.isStopped = false;
        agent.stoppingDistance = 0f;
        if (agent.pathPending || (agent.hasPath && agent.remainingDistance > 0.1f))
            return;

        if (patrolPoints.Length > 0)
        {
            Transform point = patrolPoints[nextPatrolPoint];
            nextPatrolPoint = (nextPatrolPoint + 1) % patrolPoints.Length;
            if (point != null)
                agent.SetDestination(point.position);
        }
        else if (TryGetRandomPoint(Vector3.zero, patrolDistance, out Vector3 patrolPoint))
        {
            agent.SetDestination(patrolPoint);
        }
    }
    
    public static bool TryGetRandomPoint(Vector3 center, float radius, out Vector3 result)
    {
        Vector2 randomDirection = Random.insideUnitCircle * radius;
        Vector3 targetPosition = center + new Vector3(randomDirection.x, 0f, randomDirection.y);
        NavMeshHit hit;
        
        if (NavMesh.SamplePosition(targetPosition, out hit, 2.0f, NavMesh.AllAreas))
        {
            result = hit.position;
            return true;
        }
        result = center;
        return false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, stopDistance);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, patrolDistance);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, detectionDistance);
    }
}
