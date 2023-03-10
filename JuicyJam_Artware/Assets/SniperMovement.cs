using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class SniperMovement : MonoBehaviour
{
    private FMOD.Studio.EventInstance reelSound;
    [SerializeField] FMODUnity.EventReference fmodEvent;
    enum EnemyState { CHASING, GRAPPLING, NOAGGRO }
    EnemyState currentState = EnemyState.CHASING;

    CharacterController charCtrl;
    NavMeshAgent navAgent;
    Animator animator;
    GameObject player;

    float aggroRange = 30;
    bool canGrapple = true;
    bool falling;
    bool isGrappling = false;

    private Vector3 grapplePoint;
    RaycastHit hit;

    Coroutine grappleCooldownCoroutine;
    Coroutine aggroCoroutine;

    void Start()
    {
        charCtrl = GetComponent<CharacterController>();
        navAgent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        player = GameObject.FindGameObjectWithTag("Player");
        if (!player)
            Destroy(gameObject);

        if (Physics.Raycast(transform.position, Vector3.down, out hit, 10, 1 << 8))
        {
            navAgent.Warp(hit.point);
            Debug.DrawLine(transform.position, new Vector3(hit.point.x, hit.point.y - 100, hit.point.z), Color.red, 10);
        }

        animator.SetBool("running", true);
        grapplePoint = transform.position;
    }

    private void Update()
    {
        if (currentState == EnemyState.CHASING)
        {
            Chase();
        }
        else if (currentState == EnemyState.GRAPPLING)
        {
            Grapple();
        }
        else if (currentState == EnemyState.NOAGGRO)
        {
            IsPlayerReturn();
        }

        if (falling)
            Gravity();
    }

    //If close enough to the player, go grapple. 
    //Otherwise, chase the player wherever they are on the map.
    void Chase()
    {
        animator.SetBool("running", true);
        float distanceFromPlayer = Vector3.Distance(transform.position, player.transform.position);

        if (distanceFromPlayer < aggroRange && canGrapple)
        {
            navAgent.ResetPath();
            navAgent.enabled = false;
            grapplePoint = transform.position;
            currentState = EnemyState.GRAPPLING;
        }
        else
        {
            navAgent.enabled = true;
            navAgent.destination = player.transform.position;
        }
    }

    //Moves toward grapple point if there is any.
    void Grapple()
    {
        animator.SetBool("running", false);

        //float singleStep = 1f * Time.deltaTime;
        //Vector3 playerDirection = Vector3.RotateTowards(transform.position, player.transform.position, singleStep, 0.1f);
        //transform.rotation = Quaternion.LookRotation(-playerDirection);

        var lookPos = player.transform.position - transform.position;
        var rotation = Quaternion.LookRotation(lookPos);
        transform.rotation = Quaternion.Slerp(transform.rotation, rotation, Time.deltaTime * 10);

        float distFromGrapplePoint = Vector3.Distance(transform.position, grapplePoint);
        float distFromPlayer = Vector3.Distance(transform.position, player.transform.position);

        //Move toward grapple point if not there yet.
        if (distFromGrapplePoint > 0.5f)
        {
            if (!isGrappling)
            {
                reelSound = FMODUnity.RuntimeManager.CreateInstance(fmodEvent);
                Debug.Log("YEYEYEY");
                reelSound.start();
                isGrappling = true;
            }
            reelSound.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(gameObject));

            var step = 9f * Time.fixedDeltaTime;
            transform.position = Vector3.MoveTowards(transform.position, grapplePoint, step);
        }
        else
        {
            if(isGrappling == true)
            {
                FMODUnity.RuntimeManager.PlayOneShot("event:/Cyborg/Cyborg_Wall_Hit");
            }
            isGrappling = false;
            reelSound.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        }

        //If player exists range, starts an aggro cooldown where it eventually chases after.
        if (distFromPlayer > aggroRange && distFromGrapplePoint <= 0.5f)
        {
            aggroCoroutine = StartCoroutine(AggroCooldown());
            return;
        }

        if (!canGrapple || currentState != EnemyState.GRAPPLING)
            return;

        //Find new grapple point if it can grapple again.
        if (distFromGrapplePoint <= 0.5f)
            grapplePoint = NewGrapplePoint();
    }

    //Stops the aggro cooldown if player returns.
    void IsPlayerReturn()
    {
        float distFromPlayer = Vector3.Distance(transform.position, player.transform.position);
        if (distFromPlayer < aggroRange)
        {
            Debug.Log("player came back before i could go wacko mode :)");
            StopCoroutine(aggroCoroutine);
            falling = false;
            currentState = EnemyState.GRAPPLING;
        }
    }

    //Finds a new point to grapple onto.
    Vector3 NewGrapplePoint()
    {
        FMODUnity.RuntimeManager.PlayOneShot("event:/Cyborg/Cyborg_Gun_Wall_Hook", transform.position);
        Vector3 newPos = transform.position;
        float distanceFromOrigin = 0;
        LayerMask grappleMask = 1 << 7 | 1 << 8 | 1 << 9;

        Vector3 characterCenter = transform.position + charCtrl.center;
        while (distanceFromOrigin < 20)
        {
            Vector3 randomDirection = Random.onUnitSphere;
            if (Physics.Raycast(characterCenter, randomDirection * 50, out hit, 30, grappleMask))
            {
                if (hit.transform.gameObject.layer == 7)
                {
                    Debug.DrawLine(transform.position, hit.point, Color.red, 20);
                    newPos = hit.point;
                }
                else
                {
                    Debug.DrawLine(transform.position, hit.point, Color.black, 20);
                    newPos = transform.position;
                }
            }
            distanceFromOrigin = Vector3.Distance(newPos, transform.position);
        }
        Debug.DrawLine(transform.position, hit.point, Color.blue, 20);
        FMODUnity.RuntimeManager.PlayOneShot("event:/Cyborg/Cyborg_Gun_Attachment_Shot", transform.position);
        grappleCooldownCoroutine = StartCoroutine(GrappleCooldown());
        return newPos;
    }

    //Cooldown between grapples.
    IEnumerator GrappleCooldown()
    {
        canGrapple = false;
        yield return new WaitForSeconds(Random.Range(4, 8));
        canGrapple = true;
    }

    //Drops enemy onto ground while not aggro & too far, then chases player.
    IEnumerator AggroCooldown()
    {
        currentState = EnemyState.NOAGGRO;
        yield return new WaitForSeconds(2);
        falling = true;
        yield return new WaitForSeconds(2);
        currentState = EnemyState.CHASING;
    }

    //Raycasts and sends to ground slowly...
    //Didnt want to add a rigidbody lol.
    //Works well enough.
    void Gravity()
    {
        RaycastHit groundPos;
        LayerMask grappleMask = 1 << 7 | 1 << 8 | 1 << 9;
        float distFromGround = 100;

        if (Physics.Raycast(transform.position, Vector3.down * 50, out groundPos, 50, grappleMask))
        {
            Debug.DrawLine(transform.position, groundPos.point, Color.yellow, 3);
            distFromGround = Vector3.Distance(transform.position, groundPos.point);
        }

        if (distFromGround > 1f)
        {
            var step = 6f * Time.fixedDeltaTime;
            transform.position = Vector3.MoveTowards(transform.position, groundPos.point, step);
        }
        else
        {
            animator.SetBool("running", true);
            falling = false;
            currentState = EnemyState.CHASING;
        }
    }
}
