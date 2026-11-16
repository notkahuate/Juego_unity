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
    Vector3 homePos;
    Quaternion homeRot;
    Vector3 knock;                 // retroceso al recibir un golpe (se apaga solo)

    // Destello al recibir dano: se aclara el color de sus materiales un instante (sin duplicarlos).
    const float FlashTime = 0.14f;
    Renderer[] flashRenderers;
    Color[][] flashBase;
    MaterialPropertyBlock flashBlock;
    float flashUntil;
    bool flashing;

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
        homePos = transform.position;
        homeRot = transform.rotation;
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
        if (flashing) UpdateFlash();
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
            Vector3 v = move + knock;
            v.y = vy;
            cc.Move(v * Time.deltaTime);
            knock = Vector3.Lerp(knock, Vector3.zero, 1f - Mathf.Exp(-14f * Time.deltaTime));
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

    /// <summary>
    /// Al reaparecer el cartografo en un punto de control: el enemigo (si sigue vivo) olvida la pelea,
    /// vuelve a su posicion inicial y recupera toda la vida.
    /// </summary>
    public void ResetToHome()
    {
        if (state == State.Dead) return;
        aggro = false;
        state = State.Idle;
        staggerUntil = 0f;
        vy = 0f;
        knock = Vector3.zero;
        animSpeed = 0f;
        nextAttack = Time.time + Random.Range(0.2f, 0.8f);
        health.current = health.maxHealth;
        cc.enabled = false;
        transform.position = homePos;
        transform.rotation = homeRot;
        cc.enabled = true;
        Play("Locomotion");
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
        // Sensacion de impacto: destello, congelado breve y temblor (mas fuerte si es el golpe final).
        bool heavy = health.IsDead || health.isBoss;
        CartoHUD.AddFloater(transform.position + Vector3.up * (Height + 0.2f), Mathf.RoundToInt(amount).ToString(),
            health.IsDead ? CartoArt.Gold : CartoArt.Parchment, health.IsDead ? 1.35f : 1.1f);
        StartFlash();
        CartoFeel.HitStop(heavy ? 0.08f : 0.06f);
        CartoFeel.Shake(heavy ? 0.11f : 0.07f, heavy ? 0.2f : 0.14f);

        Vector3 push = transform.position - from;
        push.y = 0f;
        if (push.sqrMagnitude > 0.001f) knock = push.normalized * (health.isBoss ? 1.5f : 5.5f);

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
        }
    }

    // ------------------------------------------------------------------ destello

    void StartFlash()
    {
        if (flashRenderers == null)
        {
            // Se buscan aqui (y no en Awake) para incluir el arma, que se coloca en Start.
            List<Renderer> list = new List<Renderer>();
            Renderer[] all = GetComponentsInChildren<Renderer>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] is SkinnedMeshRenderer || all[i] is MeshRenderer) list.Add(all[i]);
            flashRenderers = list.ToArray();
            flashBase = new Color[flashRenderers.Length][];
            for (int i = 0; i < flashRenderers.Length; i++)
            {
                Material[] ms = flashRenderers[i].sharedMaterials;
                flashBase[i] = new Color[ms.Length];
                for (int j = 0; j < ms.Length; j++)
                {
                    Color c = Color.white;
                    if (ms[j] != null)
                    {
                        if (ms[j].HasProperty("_Color")) c = ms[j].GetColor("_Color");
                        else if (ms[j].HasProperty("_BaseColor")) c = ms[j].GetColor("_BaseColor");
                    }
                    flashBase[i][j] = c;
                }
            }
            flashBlock = new MaterialPropertyBlock();
        }
        flashUntil = Time.unscaledTime + FlashTime;
        flashing = true;
        ApplyFlash(1f);
    }

    void UpdateFlash()
    {
        // En tiempo real: el destello se ve tambien durante el congelado del golpe.
        float k = (flashUntil - Time.unscaledTime) / FlashTime;
        if (k <= 0f)
        {
            flashing = false;
            k = 0f;
        }
        ApplyFlash(k);
    }

    void ApplyFlash(float k)
    {
        for (int i = 0; i < flashRenderers.Length; i++)
        {
            Renderer r = flashRenderers[i];
            if (r == null) continue;
            for (int j = 0; j < flashBase[i].Length; j++)
            {
                if (k <= 0f)
                {
                    r.SetPropertyBlock(null, j);
                    continue;
                }
                Color c = flashBase[i][j];
                Color hot = Color.Lerp(c, new Color(2.2f, 2.15f, 2f, c.a), k);
                flashBlock.Clear();
                flashBlock.SetColor("_Color", hot);
                flashBlock.SetColor("_BaseColor", hot);
                flashBlock.SetColor("_EmissionColor", CartoArt.Parchment * (0.8f * k));
                r.SetPropertyBlock(flashBlock, j);
            }
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
        CartoHUD.AddFloater(transform.position + Vector3.up * (Height + 0.7f), "+" + xp + " XP", CartoArt.Cyan, 0.85f);
        if (leveled) CartoHUD.Toast("¡Nivel " + CartoGameState.level + "! Vida máxima y daño aumentados", 3f);
        if (health.isBoss) CartoHUD.Toast("¡" + health.displayName + " ha caído! El cristal está libre", 4f);

        Destroy(gameObject, 6f);
    }
}
