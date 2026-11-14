using System;
using System.Threading;
using Cysharp.Threading.Tasks;
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

    private NavMeshAgent agent;
    private CancellationTokenSource cts = new CancellationTokenSource();

    private RaycastHit hit;
    private bool isFindedPlayer = false;
    private Vector3 moveDirection = Vector3.zero;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.stoppingDistance = stopDistance;
    }

    private async void Start()
    {   
        try
        {
            await UpdateEnemy(cts.Token);
        }
        catch (OperationCanceledException e)
        {
            //good 
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message);
        }
    }

    private async UniTask UpdateEnemy(CancellationToken cancellationToken = default)
    {
        if (!agent.isOnNavMesh)
            return;

        while (!cancellationToken.IsCancellationRequested)
        {
            isFindedPlayer = Physics.SphereCast(
                transform.position,
                detectionDistance,
                Vector3.down,
                out hit,
                detectionDistance,
                playerLayer
            );
            
            if (isFindedPlayer)
            {
                float distance = Vector3.Distance(
                    transform.position,
                    hit.transform.position
                );
                if (distance > stopDistance)
                {
                    agent.isStopped = false;
                    moveDirection = (hit.transform.position - transform.position).normalized;
                }
                else
                {
                    agent.isStopped = true;
                }
            }
            else
            {
                bool getRandom;
                do
                {
                    getRandom = TryGetRandomPoint(Vector3.zero, patrolDistance, out moveDirection);
                }while (!getRandom);
            }

            await UniTask.Delay(TimeSpan.FromSeconds(3), cancellationToken: cancellationToken);
        }
    }

    private void FixedUpdate()
    {
        agent.SetDestination(transform.position + moveDirection);
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

    private void OnDestroy()
    {
        cts.Cancel();
        cts.Dispose();
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
