using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemigo: persigue al cartografo, ataca, y al morir suelta oro y da XP.
/// Las eras estan fusionadas, asi que cada tipo mezcla dos epocas: ademas del cuerpo a cuerpo y del
/// disparo a distancia hay drones que orbitan mientras disparan ('orbits') y ciber-ninjas que
/// embisten ('dasher'). Los fusionados parpadean con una grieta temporal ('fused').
/// </summary>
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
    public Color projectileColor = new Color(1f, 0.3f, 0.3f);
    public int goldMin = 6;
    public int goldMax = 12;
    public int xp = 15;
    public bool twoHandedAnim = false;
    public GameObject weaponPrefab;
    public Vector3 weaponEuler;
    public GameObject projectilePrefab;
    public Vector3 projectileEuler = new Vector3(90f, 0f, 0f);

    // --- Fusion de eras ---
    public Color typeColor = new Color(0.878f, 0.259f, 0.247f);   // color de su tipo: nombre, aura y grieta
    public bool fused = false;          // parpadea con una grieta temporal
    public GameObject fusionFx;         // aura y esquirla (se apagan al morir)
    public Transform model;             // lo que se ve; vacio = el modelo del Animator
    public float barHeight = 0f;        // altura de la barra de vida (0 = la del personaje)
    public float aimHeight = 0f;        // altura a la que hay que apuntarle (0 = a medio cuerpo)
    public bool orbits = false;         // dron: gira alrededor del cartografo mientras dispara
    public bool dasher = false;         // ciber-ninja: embestida corta para acortar distancia
    public float dashSpeed = 15f;
    public float dashTime = 0.32f;
    public float dashCooldown = 4f;

    // --- Fases de jefe: a media vida cambia de ataque y gana un elemento de la otra era ---
    public bool phased = false;
    public Color phaseColor = new Color(0.302f, 0.902f, 1f);
    [TextArea] public string phaseMessage = "";
    public CartoEnemy summonTemplate;   // fase 2: invoca copias de esta plantilla (los drones del capitan)
    public int summonCount = 2;
    public float summonEvery = 13f;
    public int volley = 0;              // fase 2: balas de polvora por descarga (el jefe samurai)
    public float volleyEvery = 4f;

    [HideInInspector] public CartoHealth health;

    enum State { Idle, Chase, Windup, Recover, DashWind, Dash, Dead }

    // Ralentizacion global (la "Vision temporal" del cartografo): los enemigos y sus proyectiles
    // corren con su propio reloj, que puede ir mas lento que el del juego.
    static float slowUntil;
    static float slowFactor = 1f;

    public static float TimeFactor { get { return Time.time < slowUntil ? slowFactor : 1f; } }
    public static float SlowLeft { get { return Mathf.Max(0f, slowUntil - Time.time); } }

    public static void SlowAll(float factor, float seconds)
    {
        slowFactor = Mathf.Clamp(factor, 0.05f, 1f);
        slowUntil = Time.time + seconds;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void DomainReset()
    {
        All.Clear();
        slowUntil = 0f;
        slowFactor = 1f;
    }

    CharacterController cc;
    Animator anim;
    State state = State.Idle;
    float clock;                   // reloj propio: avanza mas despacio con la Vision temporal
    float timer;
    float nextAttack;
    float nextDash;
    float staggerUntil;
    float vy;
    float animSpeed;
    bool aggro;
    Vector3 homePos;
    Quaternion homeRot;
    Vector3 knock;                 // retroceso al recibir un golpe (se apaga solo)
    Vector3 dashDir;
    float orbitSign = 1f;
    float nextFlip;
    float deathAt;

    bool phase2;
    bool volleyShot;               // el ataque que esta preparando es una descarga, no un golpe
    float nextSummon;
    float nextVolley;
    float baseSpeed;
    float baseRecover;
    bool baseDasher;
    bool baseFused;
    Color baseColor;
    readonly List<CartoEnemy> summoned = new List<CartoEnemy>();

    // Grieta temporal de los fusionados: de vez en cuando el modelo tiembla y se tine de su color.
    Vector3 modelPos;
    Vector3 modelScale = Vector3.one;
    bool glitching;
    float glitchUntil;
    float nextGlitch;
    int glitchSeed;

    // Destello al recibir dano: se aclara el color de sus materiales un instante (sin duplicarlos).
    const float FlashTime = 0.14f;
    Renderer[] flashRenderers;
    Color[][] flashBase;
    Color[][] flashEmission;
    MaterialPropertyBlock flashBlock;
    float flashUntil;
    bool flashing;

    public bool IsDead { get { return state == State.Dead; } }
    public bool Aggro { get { return aggro; } }
    public float Height { get { return cc != null ? cc.height : 1.8f; } }
    public float Radius { get { return cc != null ? cc.radius : 0.4f; } }
    public float BarHeight { get { return barHeight > 0f ? barHeight : Height; } }
    /// <summary>Punto al que apuntan los disparos del cartografo.</summary>
    public Vector3 AimPoint { get { return transform.position + Vector3.up * (aimHeight > 0f ? aimHeight : Height * 0.6f); } }
    /// <summary>El jefe ya ha pasado a su segunda fase.</summary>
    public bool Phase2 { get { return phase2; } }

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        health = GetComponent<CartoHealth>();
        anim = GetComponentInChildren<Animator>();
        if (anim != null) anim.applyRootMotion = false;
        health.team = 1;
        homePos = transform.position;
        homeRot = transform.rotation;
        baseSpeed = moveSpeed;
        baseRecover = recover;
        baseDasher = dasher;
        baseFused = fused;
        baseColor = typeColor;
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
        if (model == null && anim != null) model = anim.transform;
        if (model != null)
        {
            modelPos = model.localPosition;
            modelScale = model.localScale;
        }
        orbitSign = Random.value < 0.5f ? -1f : 1f;
        nextAttack = Random.Range(0.2f, 0.8f);
        nextDash = Random.Range(0.5f, 2f);
        nextGlitch = Time.time + Random.Range(0.5f, 3.5f);
        glitchSeed = Random.Range(0, 100000);
    }

    void Update()
    {
        if (flashing) UpdateFlash();
        if (state == State.Dead)
        {
            UpdateDeath();
            return;
        }

        float factor = TimeFactor;
        clock += Time.deltaTime * factor;
        if (anim != null) anim.speed = factor;
        if (fused) UpdateGlitch();

        CartoPlayer player = CartoPlayer.Instance;
        Vector3 move = Vector3.zero;
        bool rushing = false;

        if (player != null && !player.IsDead)
        {
            Vector3 to = player.transform.position - transform.position;
            to.y = 0f;
            float d = to.magnitude;
            Vector3 dir = d > 0.01f ? to / d : transform.forward;
            Vector3 tangent = new Vector3(dir.z, 0f, -dir.x) * orbitSign;

            if (!aggro && d < aggroRange) Alert();
            if (phase2 && aggro && summonTemplate != null && clock >= nextSummon) Summon();

            if (clock >= staggerUntil)
            {
                switch (state)
                {
                    case State.Idle:
                        if (aggro) state = State.Chase;
                        break;

                    case State.Chase:
                        Face(dir, 10f * factor);
                        if (orbits)
                        {
                            move = Orbit(dir, tangent, d);
                            if (d <= keepDistance + 6f && clock >= nextAttack) BeginAttack();
                        }
                        else if (ranged)
                        {
                            if (d > keepDistance + 1.5f) move = dir * moveSpeed;
                            else if (d < keepDistance - 3.5f) move = -dir * moveSpeed * 0.7f;
                            if (d <= keepDistance + 5f && clock >= nextAttack) BeginAttack();
                        }
                        else
                        {
                            if (phase2 && volley > 0 && clock >= nextVolley && d > 3.5f && d < 20f) BeginVolley();
                            else if (dasher && clock >= nextDash && d > 4.5f && d < 11f) BeginDash();
                            else if (d > attackRange * 0.8f) move = dir * moveSpeed;
                            else if (clock >= nextAttack) BeginAttack();
                        }
                        break;

                    case State.Windup:
                        Face(dir, 5f * factor);
                        if (orbits) move = Orbit(dir, tangent, d) * 0.5f;   // el dron no se para a apuntar
                        if (clock >= timer)
                        {
                            if (volleyShot) FireVolley(player);
                            else Strike(player, d, dir);
                            volleyShot = false;
                            state = State.Recover;
                            timer = clock + recover;
                        }
                        break;

                    case State.Recover:
                        if (orbits)
                        {
                            Face(dir, 10f * factor);
                            move = Orbit(dir, tangent, d);
                        }
                        if (clock >= timer)
                        {
                            state = State.Chase;
                            nextAttack = clock + Random.Range(0.1f, 0.5f);
                        }
                        break;

                    case State.DashWind:
                        // Aviso de la embestida: se queda quieto un instante, parpadeando.
                        Face(dir, 14f * factor);
                        if (clock >= timer)
                        {
                            state = State.Dash;
                            timer = clock + dashTime;
                            dashDir = dir;
                        }
                        break;

                    case State.Dash:
                        move = dashDir * dashSpeed;
                        rushing = true;
                        if (clock >= timer || d < attackRange * 0.9f)
                        {
                            state = State.Chase;
                            nextAttack = clock;   // llega y golpea
                            nextDash = clock + dashCooldown * Random.Range(0.8f, 1.3f);
                        }
                        break;
                }
            }
        }
        else if (state == State.DashWind || state == State.Dash)
        {
            state = State.Chase;
        }

        if (cc.enabled)
        {
            if (cc.isGrounded && vy < 0f) vy = -2f;
            vy += -25f * Time.deltaTime;
            Vector3 v = move * factor + knock;
            v.y = vy;
            CollisionFlags hit = cc.Move(v * Time.deltaTime);
            // Un dron que choca con algo da la vuelta y orbita hacia el otro lado.
            if (orbits && (hit & CollisionFlags.Sides) != 0 && clock >= nextFlip)
            {
                orbitSign = -orbitSign;
                nextFlip = clock + 1.2f;
            }
            knock = Vector3.Lerp(knock, Vector3.zero, 1f - Mathf.Exp(-14f * Time.deltaTime));
        }

        float targetAnim = move.sqrMagnitude > 0.05f ? (rushing || moveSpeed > 4.6f ? 1.6f : 1f) : 0f;
        animSpeed = Mathf.Lerp(animSpeed, targetAnim, 1f - Mathf.Exp(-10f * Time.deltaTime));
        if (anim != null) anim.SetFloat("Speed", animSpeed);
    }

    /// <summary>Movimiento del dron: gira alrededor del cartografo y corrige la distancia a 'keepDistance'.</summary>
    Vector3 Orbit(Vector3 dir, Vector3 tangent, float distance)
    {
        float radial = Mathf.Clamp((distance - keepDistance) * 0.5f, -1f, 1f);
        return (tangent + dir * radial).normalized * moveSpeed;
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
        nextAttack = clock + Random.Range(0.2f, 0.8f);
        nextDash = clock + Random.Range(0.5f, 2f);
        volleyShot = false;
        if (phase2) LeavePhase2();
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
        volleyShot = false;
        timer = clock + windup;
        Play(ranged ? "Shoot" : (twoHandedAnim ? "Attack2H" : "Attack1H"));
    }

    void BeginDash()
    {
        state = State.DashWind;
        timer = clock + 0.3f;
        // La grieta parpadea justo antes: es el aviso para esquivar.
        glitching = true;
        glitchUntil = Time.time + 0.3f;
        nextGlitch = glitchUntil + Random.Range(1.4f, 4.2f);
    }

    void Strike(CartoPlayer player, float dist, Vector3 dir)
    {
        if (ranged)
        {
            Vector3 origin = transform.position + Vector3.up * (aimHeight > 0f ? aimHeight : Height * 0.7f) + transform.forward * 0.6f;
            Vector3 aim = player.transform.position + Vector3.up * (player.Height * 0.55f);
            Vector3 d = (aim - origin).normalized;
            CartoProjectile.Spawn(origin, d, projectileSpeed, damage, 1, projectilePrefab, projectileEuler, projectileColor);
            CartoAudio.Play(CartoAudio.Sfx.Shoot, 0.45f, orbits ? 1.5f : 0.9f);
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
        CartoHUD.AddFloater(transform.position + Vector3.up * (BarHeight + 0.2f), Mathf.RoundToInt(amount).ToString(),
            health.IsDead ? CartoArt.Gold : CartoArt.Parchment, health.IsDead ? 1.35f : 1.1f);
        StartFlash();
        CartoAudio.Play(CartoAudio.Sfx.Hit, heavy ? 1f : 0.85f, heavy ? 0.8f : Random.Range(0.95f, 1.15f));
        CartoFeel.HitStop(heavy ? 0.08f : 0.06f);
        CartoFeel.Shake(heavy ? 0.11f : 0.07f, heavy ? 0.2f : 0.14f);

        Vector3 push = transform.position - from;
        push.y = 0f;
        if (push.sqrMagnitude > 0.001f) knock = push.normalized * (health.isBoss ? 1.5f : 5.5f);

        if (health.IsDead) return;
        if (phased && !phase2 && health.Percent <= 0.5f) EnterPhase2();
        if (!health.isBoss)
        {
            staggerUntil = clock + 0.3f;
            if (state == State.Windup || state == State.DashWind || state == State.Dash)
            {
                // El golpe interrumpe el ataque (o la embestida) de los enemigos normales.
                state = State.Chase;
                nextAttack = clock + 0.5f;
                nextDash = clock + dashCooldown * 0.5f;
            }
            Play("Hit");
        }
    }

    // ------------------------------------------------------------------ fases de jefe

    /// <summary>
    /// Media vida: la grieta alcanza al jefe. Se vuelve mas rapido, empieza a parpadear con el color
    /// de la otra era y gana su ataque: invocar drones (y embestir) o disparar descargas de polvora.
    /// </summary>
    void EnterPhase2()
    {
        phase2 = true;
        typeColor = phaseColor;
        fused = true;
        if (fusionFx != null) fusionFx.SetActive(true);
        moveSpeed = baseSpeed * 1.15f;
        recover = baseRecover * 0.75f;
        if (summonTemplate != null)
        {
            dasher = true;
            nextSummon = clock + 1.2f;
        }
        nextDash = clock + 2.5f;
        nextVolley = clock + 1.5f;
        glitching = true;
        glitchUntil = Time.time + 0.9f;
        nextGlitch = glitchUntil + 1f;
        CartoHUD.PhaseWarning(health.displayName, phaseMessage, phaseColor);
        CartoAudio.Play(CartoAudio.Sfx.Phase);
        CartoFeel.Shake(0.25f, 0.6f);
    }

    /// <summary>Vuelve a la primera fase (al reaparecer el cartografo en el punto de control).</summary>
    void LeavePhase2()
    {
        phase2 = false;
        typeColor = baseColor;
        fused = baseFused;
        dasher = baseDasher;
        moveSpeed = baseSpeed;
        recover = baseRecover;
        if (glitching)
        {
            glitching = false;
            EndGlitch();
        }
        if (fusionFx != null) fusionFx.SetActive(baseFused);
        DismissSummoned();
    }

    void Summon()
    {
        summoned.RemoveAll(s => s == null || s.IsDead);
        nextSummon = clock + summonEvery;
        if (summoned.Count > summonCount) return;   // ya hay bastantes en pie
        for (int i = 0; i < summonCount; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            Vector3 pos = transform.position + transform.right * side * (2.6f + i / 2) + transform.forward * 1.5f;
            GameObject go = Instantiate(summonTemplate.gameObject, pos, transform.rotation, transform.parent);
            go.name = "Enemigo_Invocado";
            go.SetActive(true);
            CartoEnemy e = go.GetComponent<CartoEnemy>();
            if (e == null) continue;
            e.Alert();
            summoned.Add(e);
        }
        CartoHUD.Toast("¡" + health.displayName + " invoca drones de otra época!", 2.5f);
        CartoAudio.Play(CartoAudio.Sfx.Skill, 0.7f, 0.6f);
        CartoFeel.Shake(0.12f, 0.3f);
    }

    /// <summary>Sin su amo (o al reiniciar la pelea) los invocados se desvanecen.</summary>
    void DismissSummoned()
    {
        for (int i = 0; i < summoned.Count; i++)
            if (summoned[i] != null) Destroy(summoned[i].gameObject);
        summoned.Clear();
    }

    void BeginVolley()
    {
        state = State.Windup;
        volleyShot = true;
        timer = clock + 0.55f;
        nextVolley = clock + volleyEvery;
        Play("Shoot");
        // La grieta parpadea mientras carga: es el aviso.
        glitching = true;
        glitchUntil = Time.time + 0.55f;
        nextGlitch = glitchUntil + Random.Range(1.4f, 4.2f);
    }

    /// <summary>Descarga de polvora: varias balas en abanico hacia el cartografo.</summary>
    void FireVolley(CartoPlayer player)
    {
        Vector3 origin = transform.position + Vector3.up * (Height * 0.7f) + transform.forward * 0.7f;
        Vector3 aim = player.transform.position + Vector3.up * (player.Height * 0.55f);
        Vector3 d = (aim - origin).normalized;
        float dmg = Mathf.Round(damage * 0.55f);
        for (int i = 0; i < volley; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, (i - (volley - 1) * 0.5f) * 12f, 0f) * d;
            CartoProjectile.Spawn(origin, dir, 21f, dmg, 1, null, Vector3.zero, phaseColor);
        }
        CartoAudio.Play(CartoAudio.Sfx.Shoot, 0.8f, 0.7f);
        CartoFeel.Shake(0.08f, 0.15f);
    }

    // ------------------------------------------------------------------ grieta temporal

    void UpdateGlitch()
    {
        float now = Time.time;
        if (glitching && now >= glitchUntil)
        {
            glitching = false;
            EndGlitch();
        }
        if (!glitching)
        {
            if (now < nextGlitch) return;
            glitching = true;
            glitchUntil = now + Random.Range(0.12f, 0.28f);
            nextGlitch = glitchUntil + Random.Range(1.4f, 4.2f);
        }
        // Cambia a saltos (no cada fotograma), y distinto en cada enemigo.
        int step = Mathf.FloorToInt(Time.unscaledTime * 30f) + glitchSeed;
        float n = CartoCinematic.Hash(step);
        if (model != null)
            model.localPosition = modelPos + new Vector3((n - 0.5f) * 0.3f, 0f, (CartoCinematic.Hash(step + 3) - 0.5f) * 0.12f);
        if (!flashing)
        {
            CacheRenderers();
            Color hot = typeColor * 2.2f;
            hot.a = 1f;
            ApplyTint(n > 0.35f ? 0.75f : 0f, hot, typeColor * 1.2f);
        }
    }

    void EndGlitch()
    {
        if (model != null) model.localPosition = modelPos;
        if (!flashing && flashRenderers != null) ApplyTint(0f, Color.white, Color.black);
    }

    // ------------------------------------------------------------------ destello

    void CacheRenderers()
    {
        if (flashRenderers != null) return;
        // Se buscan aqui (y no en Awake) para incluir el arma, que se coloca en Start.
        // Solo dentro del modelo: el aura y la esquirla de la fusion no destellan.
        Transform from = model != null ? model : transform;
        List<Renderer> list = new List<Renderer>();
        Renderer[] all = from.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < all.Length; i++)
            if (all[i] is SkinnedMeshRenderer || all[i] is MeshRenderer) list.Add(all[i]);
        flashRenderers = list.ToArray();
        flashBase = new Color[flashRenderers.Length][];
        flashEmission = new Color[flashRenderers.Length][];
        for (int i = 0; i < flashRenderers.Length; i++)
        {
            Material[] ms = flashRenderers[i].sharedMaterials;
            flashBase[i] = new Color[ms.Length];
            flashEmission[i] = new Color[ms.Length];
            for (int j = 0; j < ms.Length; j++)
            {
                Color c = Color.white;
                Color e = Color.black;
                if (ms[j] != null)
                {
                    if (ms[j].HasProperty("_Color")) c = ms[j].GetColor("_Color");
                    else if (ms[j].HasProperty("_BaseColor")) c = ms[j].GetColor("_BaseColor");
                    if (ms[j].HasProperty("_EmissionColor") && ms[j].IsKeywordEnabled("_EMISSION")) e = ms[j].GetColor("_EmissionColor");
                }
                flashBase[i][j] = c;
                flashEmission[i][j] = e;
            }
        }
        flashBlock = new MaterialPropertyBlock();
    }

    void StartFlash()
    {
        CacheRenderers();
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
        ApplyTint(k, new Color(2.2f, 2.15f, 2f, 1f), CartoArt.Parchment * 0.8f);
    }

    /// <summary>Lleva el color de todos sus materiales hacia 'hot' (k = 0 lo deja como estaba).</summary>
    void ApplyTint(float k, Color hot, Color emission)
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
                Color tinted = Color.Lerp(c, new Color(hot.r, hot.g, hot.b, c.a), k);
                flashBlock.Clear();
                flashBlock.SetColor("_Color", tinted);
                flashBlock.SetColor("_BaseColor", tinted);
                flashBlock.SetColor("_EmissionColor", flashEmission[i][j] + emission * k);
                r.SetPropertyBlock(flashBlock, j);
            }
        }
    }

    // ------------------------------------------------------------------ muerte

    void OnDied()
    {
        state = State.Dead;
        deathAt = Time.time;
        All.Remove(this);
        if (anim != null)
        {
            anim.speed = 1f;
            anim.SetFloat("Speed", 0f);
        }
        Play("Death");
        cc.enabled = false;
        glitching = false;
        if (model != null) model.localPosition = modelPos;
        if (fusionFx != null) fusionFx.SetActive(false);
        DismissSummoned();

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
        CartoHUD.AddFloater(transform.position + Vector3.up * (BarHeight + 0.7f), "+" + xp + " XP", CartoArt.Cyan, 0.85f);
        if (leveled) CartoHUD.Toast("¡Nivel " + CartoGameState.level + "! Vida máxima y daño aumentados", 3f);
        if (health.isBoss) CartoHUD.Toast("¡" + health.displayName + " ha caído! El cristal está libre", 4f);

        Destroy(gameObject, 6f);
    }

    /// <summary>Lo que no tiene animacion de muerte (los drones) se encoge hasta desaparecer.</summary>
    void UpdateDeath()
    {
        if (anim != null || model == null || !model.gameObject.activeSelf) return;
        float k = (Time.time - deathAt) / 0.4f;
        if (k >= 1f)
        {
            model.gameObject.SetActive(false);
            return;
        }
        model.localScale = modelScale * (1f - k);
        model.Rotate(0f, 900f * Time.deltaTime, 0f, Space.Self);
    }
}
