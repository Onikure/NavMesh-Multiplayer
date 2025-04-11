using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(ClientNetworkTransform))]
[RequireComponent(typeof(NetworkAnimator))]
public class CharacterMovement : NetworkBehaviour
{
    public NavMeshAgent Agent;
    public NetworkAnimator NetworkAnimator;
    private Animator _animator;

    private bool _isOffMesh;
    [SerializeField] private float raycastDistance = 100f;
    

    // Fields for position synchronization
    private Vector3 _serverPosition;
    private bool _receivedServerPosition = false;

    private void Awake()
    {
        if (!Agent) Agent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();
        if (!NetworkAnimator) NetworkAnimator = GetComponent<NetworkAnimator>();

        // Disable direct transform updates
        if (Agent)
        {
            Agent.updatePosition = false;
            Agent.updateRotation = false;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Always enable the agent
        if (Agent)
        {
            Agent.enabled = true;
        }

        // Ensure the agent is placed on the NavMesh
        StartCoroutine(InitializeNavMesh());
    }

    private IEnumerator InitializeNavMesh()
    {
        // Wait for physics/navmesh initialization
        yield return new WaitForSeconds(0.2f);

        // Try to place on NavMesh multiple times
        for (int attempts = 0; attempts < 5; attempts++)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 10f, NavMesh.AllAreas))
            {
                transform.position = hit.position;

                if (Agent && Agent.enabled)
                {
                    Agent.Warp(hit.position);
                    Debug.Log($"Agent successfully placed on NavMesh at {hit.position} (attempt {attempts + 1})");
                    yield break;
                }
            }

            Debug.Log($"Attempt {attempts + 1} to place agent on NavMesh failed, retrying...");
            yield return new WaitForSeconds(0.1f);
        }

        Debug.LogError("Failed to place agent on NavMesh after multiple attempts");
    }

    private void Update()
    {
        if (!IsOwner) return; // Only allow the owner to control their character
        if (Camera.main == null) return;

        UpdateMovementAnimation();
        HandleMouseInput();
    }

    private void LateUpdate()
    {
        if (!IsSpawned || !Agent || !Agent.enabled) return;

        if (IsServer)
        {
            // Server controls authoritative position
            transform.position = Agent.nextPosition;

            // Send position updates to clients
            UpdatePositionClientRpc(transform.position);
        }
        else if (IsClient && IsOwner)
        {
            // Client receives position updates from server and interpolates locally
            if (_receivedServerPosition)
            {
                transform.position = Vector3.Lerp(transform.position, _serverPosition, Time.deltaTime * 10f);
                Agent.nextPosition = transform.position; // Update agent's next position
            }
        }
    }

    private void UpdateMovementAnimation()
    {
        if (!Agent || !Agent.enabled || !NetworkAnimator || !NetworkAnimator.Animator) return;

        if (Agent.hasPath)
        {
            if (Agent.isOnOffMeshLink)
            {
                if (!_isOffMesh)
                {
                    _isOffMesh = true;
                    StartCoroutine(DoOffMeshLink(Agent.currentOffMeshLinkData));
                }
            }
            else
            {
                _isOffMesh = false;
                var dir = (Agent.steeringTarget - transform.position).normalized;
                var animDir = transform.InverseTransformDirection(dir);
                var isFacingMoveDirection = Vector3.Dot(dir, transform.forward) > .5f;

                NetworkAnimator.Animator.SetFloat("Horizontal",
                    isFacingMoveDirection ? animDir.x : 0, .5f, Time.deltaTime);
                NetworkAnimator.Animator.SetFloat("Vertical",
                    isFacingMoveDirection ? animDir.z : 0, .5f, Time.deltaTime);

                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(dir),
                    180 * Time.deltaTime);

                if (Vector3.Distance(transform.position, Agent.destination) < Agent.radius)
                {
                    Agent.ResetPath();
                }
            }
        }
        else
        {
            NetworkAnimator.Animator.SetFloat("Horizontal", 0, .5f, Time.deltaTime);
            NetworkAnimator.Animator.SetFloat("Vertical", 0, .5f, Time.deltaTime);
        }
    }

    private void HandleMouseInput()
    {
        if (Input.GetMouseButtonDown(1))
        {
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, raycastDistance))
            {
                if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 5.0f, NavMesh.AllAreas))
                {
                    SetDestinationServerRpc(navHit.position);
                }
                else
                {
                    Debug.LogWarning("Clicked position is not on NavMesh");
                }
            }
        }
    }

    [ServerRpc]
    private void SetDestinationServerRpc(Vector3 position)
    {
        if (!Agent || !Agent.enabled)
        {
            Debug.LogError("NavMeshAgent is null or disabled on server");
            return;
        }

        try
        {
            // Validate NavMesh status
            if (!Agent.isOnNavMesh)
            {
                Debug.LogWarning("Server agent not on NavMesh");

                // Try to find a valid position and warp there
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 10f, NavMesh.AllAreas))
                {
                    transform.position = navHit.position;
                    Agent.Warp(navHit.position);
                    Debug.Log($"Server agent warped to NavMesh at {navHit.position}");
                }
                else
                {
                    Debug.LogError("Server couldn't find valid NavMesh position");
                    return;
                }
            }

            // Set destination now that we are safely on the NavMesh
            Agent.SetDestination(position);

            // Inform clients about the new destination
            UpdateDestinationClientRpc(position);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error in SetDestinationServerRpc: {e.Message}");
        }
    }

    [ClientRpc]
    private void UpdateDestinationClientRpc(Vector3 position)
    {
        if (!IsOwner) return; // Skip updating for the owner

        if (Agent && Agent.enabled && Agent.isOnNavMesh)
        {
            try
            {
                Agent.SetDestination(position);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Error in UpdateDestinationClientRpc: {e.Message}");
            }
        }
    }

    [ClientRpc]
    private void UpdatePositionClientRpc(Vector3 position)
    {
        if (IsOwner)
        {
            _serverPosition = position;
            _receivedServerPosition = true;
        }
    }

    // Added the missing DoOffMeshLink method
    private IEnumerator DoOffMeshLink(OffMeshLinkData link)
    {
        if (IsServer)
        {
            PlayJumpAnimationClientRpc();
        }

        while (true)
        {
            transform.position = Vector3.Lerp(transform.position, link.startPos, Time.deltaTime);
            var dir = (link.endPos - link.startPos).normalized;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 180 * Time.deltaTime);

            var isRotationGood = Vector3.Dot(dir, transform.forward) > .8f;

            if (isRotationGood)
            {
                break;
            }

            yield return null;
        }

        NetworkAnimator.Animator.CrossFade("Jump", .2f);

        var time = .7f;
        var totalTime = time;

        while (time > 0)
        {
            time = Mathf.Max(0, time - Time.deltaTime);
            var goal = Vector3.Lerp(link.startPos, link.endPos, 1 - time / totalTime);
            var elapsedTime = totalTime - time;
            transform.position = elapsedTime > .3f ? goal : Vector3.Lerp(transform.position, goal, elapsedTime / .3f);
            yield return null;
        }

        transform.position = link.endPos;

        Agent.CompleteOffMeshLink();
        _isOffMesh = false;
    }

    [ClientRpc]
    private void PlayJumpAnimationClientRpc()
    {
        if (!IsOwner) // Server already handles owner's animation
        {
            StartCoroutine(DoOffMeshLink(Agent.currentOffMeshLinkData));
        }
    }

    
}
