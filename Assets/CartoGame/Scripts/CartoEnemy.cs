using System.Collections.Generic;
using UnityEngine;

/// <summary>Enemigo: persigue al cartografo, ataca, y al morir suelta oro y da XP.</summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(CartoHealth))]
public class CartoEnemy : MonoBehaviour
{
    public static readonly List<CartoEnemy> All = new List<CartoEnemy>();

    public float moveSpeed = 3.6f;
    public float aggroRange = 15f;
    public float attackRange = 2.3f;
    public float damage = 10f;
    public float windup = 0.5f;        // tiempo de aviso antes del golpe
    public float recover = 0.9f;       // pausa despues de atacar
    public bool ranged = false;
    public float keepDistance = 9f;
    public float projectileSpeed = 16f;
    public int goldMin = 6;
    public int goldMax = 12;
    public int xp = 15;
    public bool twoHandedAnim = false;
    public GameObject weaponPrefab;
    public Vector3 weaponEuler;
    public GameObject projectilePrefab;
    public Vector3 projectileEuler = new Vector3(90f, 0f, 0f);

    [HideInInspector] public CartoHealth health;

    enum State { Idle, Chase, Windup, Recover, Dead }

    CharacterController cc;
    Animator anim;
    State state = State.Idle;
    float timer;
    float nextAttack;
    float staggerUntil;
    float vy;
    float animSpeed;
    bool aggro;

    public bool IsDead { get { return state == State.Dead; } }
    public bool Aggro { get { return aggro; } }
    public float Height { get { return cc != null ? cc.height : 1.8f; } }
    public float Radius { get { return cc != null ? cc.radius : 0.4f; } }

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        health = GetComponent<CartoHealth>();
        anim = GetComponentInChildren<Animator>();
        if (anim != null) anim.applyRootMotion = false;
        health.team = 1;
        health.Damaged += OnDamaged;
        health.Died += OnDied;
    }

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    void Start()
    {
        if (weaponPrefab != null)
        {
            Transform grip = CartoMount.GetGrip(anim, transform);
            CartoMount.Attach(grip, weaponPrefab, weaponEuler, Vector3.zero, 1f);
        }
        nextAttack = Time.time + Random.Range(0.2f, 0.8f);
    }

    void Update()
    {
        if (state == State.Dead) return;

        CartoPlayer player = CartoPlayer.Instance;
        Vector3 move = Vector3.zero;

        if (player != null && !player.IsDead)
        {
            Vector3 to = player.transform.position - transform.position;
            to.y = 0f;
            float d = to.magnitude;
            Vector3 dir = d > 0.01f ? to / d : transform.forward;

            if (!aggro && d < aggroRange) Alert();

            if (Time.time >= staggerUntil)
            {
                switch (state)
                {
                    case State.Idle:
                        if (aggro) state = State.Chase;
                        break;

                    case State.Chase:
                        Face(dir, 10f);
                        if (ranged)
                        {
                            if (d > keepDistance + 1.5f) move = dir * moveSpeed;
                            else if (d < keepDistance - 3.5f) move = -dir * moveSpeed * 0.7f;
                            if (d <= keepDistance + 5f && Time.time >= nextAttack) BeginAttack();
                        }
                        else
                        {
                            if (d > attackRange * 0.8f) move = dir * moveSpeed;
                            else if (Time.time >= nextAttack) BeginAttack();
                        }
                        break;

                    case State.Windup:
                        Face(dir, 5f);
                        if (Time.time >= timer)
                        {
                            Strike(player, d, dir);
                            state = State.Recover;
                            timer = Time.time + recover;
                        }
                        break;

                    case State.Recover:
                        if (Time.time >= timer)
                        {
                            state = State.Chase;
                            nextAttack = Time.time + Random.Range(0.1f, 0.5f);
                        }
                        break;
                }
            }
        }

        if (cc.enabled)
        {
            if (cc.isGrounded && vy < 0f) vy = -2f;
            vy += -25f * Time.deltaTime;
            Vector3 v = move;
            v.y = vy;
            cc.Move(v * Time.deltaTime);
        }

        float targetAnim = move.sqrMagnitude > 0.05f ? (moveSpeed > 4.6f ? 1.6f : 1f) : 0f;
        animSpeed = Mathf.Lerp(animSpeed, targetAnim, 1f - Mathf.Exp(-10f * Time.deltaTime));
        if (anim != null) anim.SetFloat("Speed", animSpeed);
    }

    void Face(Vector3 dir, float speed)
    {
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion look = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-speed * Time.deltaTime));
    }

    void Play(string stateName)
    {
        if (anim != null) anim.CrossFadeInFixedTime(stateName, 0.1f, 0, 0f);
    }

    /// <summary>Se pone en guardia y avisa a los companeros cercanos.</summary>
    public void Alert()
    {
        if (aggro || state == State.Dead) return;
        aggro = true;
        if (health.isBoss) CartoHUD.Toast("¡" + health.displayName + " te ha visto!", 2.5f);
        CartoEnemy[] others = All.ToArray();
        for (int i = 0; i < others.Length; i++)
        {
            CartoEnemy o = others[i];
            if (o == null || o == this || o.aggro || o.health.isBoss) continue;
            if ((o.transform.position - transform.position).sqrMagnitude < 9f * 9f) o.Alert();
        }
    }

    void BeginAttack()
    {
        state = State.Windup;
        timer = Time.time + windup;
        Play(ranged ? "Shoot" : (twoHandedAnim ? "Attack2H" : "Attack1H"));
    }

    void Strike(CartoPlayer player, float dist, Vector3 dir)
    {
        if (ranged)
        {
            Vector3 origin = transform.position + Vector3.up * (Height * 0.7f) + transform.forward * 0.6f;
            Vector3 aim = player.transform.position + Vector3.up * (player.Height * 0.55f);
            Vector3 d = (aim - origin).normalized;
            CartoProjectile.Spawn(origin, d, projectileSpeed, damage, 1, projectilePrefab, projectileEuler, new Color(1f, 0.3f, 0.3f));
        }
        else
        {
            if (dist <= attackRange + 0.9f && Vector3.Angle(transform.forward, dir) < 75f)
                player.health.TakeDamage(damage, transform.position);
        }
    }

    void OnDamaged(float amount, Vector3 from)
    {
        Alert();
        CartoHUD.AddFloater(transform.position + Vector3.up * (Height + 0.2f), Mathf.RoundToInt(amount).ToString(), new Color(1f, 0.9f, 0.3f));
        if (health.IsDead) return;
        if (!health.isBoss)
        {
            staggerUntil = Time.time + 0.3f;
            if (state == State.Windup)
            {
                // El golpe interrumpe el ataque de los enemigos normales.
                state = State.Chase;
                nextAttack = Time.time + 0.5f;
            }
            Play("Hit");
            Vector3 push = transform.position - from;
            push.y = 0f;
            if (cc.enabled && push.sqrMagnitude > 0.001f) cc.Move(push.normalized * 0.35f);
        }
    }

    void OnDied()
    {
        state = State.Dead;
        All.Remove(this);
        Play("Death");
        if (anim != null) anim.SetFloat("Speed", 0f);
        cc.enabled = false;

        // Botin: oro, a veces un corazon, y experiencia.
        int gold = Random.Range(goldMin, goldMax + 1);
        int coins = Mathf.Clamp(gold / 5, 1, 12);
        int perCoin = Mathf.Max(1, gold / coins);
        int rest = gold - perCoin * coins;
        Vector3 basePos = transform.position + Vector3.up * 1f;
        for (int i = 0; i < coins; i++)
            CartoLoot.Spawn(basePos, CartoLoot.Kind.Gold, perCoin + (i == 0 ? rest : 0));
        if (health.isBoss || Random.value < 0.3f)
            CartoLoot.Spawn(basePos, CartoLoot.Kind.Heart, health.isBoss ? 60 : 25);

        CartoGameState.kills++;
        bool leveled = CartoGameState.AddXp(xp);
        CartoHUD.AddFloater(transform.position + Vector3.up * (Height + 0.7f), "+" + xp + " XP", new Color(0.5f, 0.8f, 1f));
        if (leveled) CartoHUD.Toast("¡Nivel " + CartoGameState.level + "! Vida máxima y daño aumentados", 3f);
        if (health.isBoss) CartoHUD.Toast("¡" + health.displayName + " ha caído! El cristal está libre", 4f);

        Destroy(gameObject, 6f);
    }
}
