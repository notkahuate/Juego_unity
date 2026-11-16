using UnityEngine;

/// <summary>El cartografo: movimiento, esquiva, ataque cuerpo a cuerpo y a distancia.</summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(CartoHealth))]
public class CartoPlayer : MonoBehaviour
{
    public static CartoPlayer Instance;

    public float walkSpeed = 5.5f;
    public float sprintSpeed = 8.5f;
    public float rollSpeed = 11f;
    public float rollDuration = 0.55f;
    public float rollCooldown = 0.9f;
    public float gravity = -25f;
    public float turnSpeed = 14f;
    public float attackRecovery = 0.8f;   // parte del 'cooldown' del arma que bloquea el siguiente golpe
    public float inputBuffer = 0.2f;      // segundos que se recuerda un clic de ataque dado antes de tiempo

    // Piezas de ropa del modelo que se activan al empezar (nombres de los objetos dentro
    // de "Armors"). Disponibles: Starter_Chest, Starter_Pants, Starter_Boots,
    // PlateSet1_Chest, PlateSet1_Pants, PlateSet1_Boots, PlateSet1_Gloves,
    // PlateSet1_Shoulders, PlateSet1_Helmet.
    public string[] outfitPieces = { "Starter_Chest", "Starter_Pants", "Starter_Boots" };

    [HideInInspector] public CartoHealth health;

    CharacterController cc;
    Animator anim;
    Transform grip;
    GameObject meleeModel;
    GameObject rangedModel;
    float vy;
    float busyUntil;
    float rollUntil;
    float nextRoll;
    Vector3 rollDir;
    float hitAt = -1f;
    float fireAt = -1f;
    float nextShoot;
    float showRangedUntil;
    Vector2 animMove;     // x = lateral, y = avance (parametros Strafe y Speed del Animator)
    bool dead;
    float meleeBuffer = -1f;   // ataque pedido mientras aun no se podia: se lanza en cuanto se pueda
    float shootBuffer = -1f;
    CartoEnemy aimTarget;      // a quien mira el cartografo mientras dura el golpe
    Vector3 aimDir;
    float aimUntil;
    bool hasUpper;             // el Animator tiene la capa "UpperBody" (torso y brazos)
    bool hasStrafe;            // el Animator tiene el parametro "Strafe" (correr de lado / hacia atras)
    float upperUntil;
    float upperWeight;

    const int UpperLayer = 1;
    float safeUntil;      // invulnerable un momento al reaparecer en un punto de control
    Vector3 spawnPos;

    public bool IsDead { get { return dead; } }
    public float Height { get { return cc != null ? cc.height : 1.8f; } }

    void Awake()
    {
        Instance = this;
        cc = GetComponent<CharacterController>();
        health = GetComponent<CartoHealth>();
        anim = GetComponentInChildren<Animator>();
        if (anim != null) anim.applyRootMotion = false;
        CartoMount.ApplyOutfit(transform, outfitPieces);

        health.team = 0;
        health.maxHealth = CartoGameState.maxHealth;
        health.current = Mathf.Clamp(CartoGameState.health, 1f, CartoGameState.maxHealth);
        health.Damaged += OnDamaged;
        health.Died += OnDied;
        spawnPos = transform.position;
    }

    void Start()
    {
        // Escenas sin regenerar no tienen la capa ni el parametro: en ese caso se anima como antes.
        if (anim != null && anim.runtimeAnimatorController != null)
        {
            hasUpper = anim.layerCount > UpperLayer;
            AnimatorControllerParameter[] ps = anim.parameters;
            for (int i = 0; i < ps.Length; i++) if (ps[i].name == "Strafe") hasStrafe = true;
        }
        RefreshWeapons();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Vuelve a crear los modelos de las armas equipadas en la mano.</summary>
    public void RefreshWeapons()
    {
        if (meleeModel != null) Destroy(meleeModel);
        if (rangedModel != null) Destroy(rangedModel);
        meleeModel = null;
        rangedModel = null;

        CartoWeaponLibrary lib = CartoWeaponLibrary.Instance;
        if (lib == null) return;
        if (grip == null) grip = CartoMount.GetGrip(anim, transform);
        if (grip == null) return;

        CartoWeaponDef m = lib.Get(CartoGameState.meleeId);
        if (m != null) meleeModel = CartoMount.Attach(grip, m.prefab, m.gripEuler, m.gripOffset, m.modelScale);
        CartoWeaponDef r = lib.Get(CartoGameState.rangedId);
        if (r != null) rangedModel = CartoMount.Attach(grip, r.prefab, r.gripEuler, r.gripOffset, r.modelScale);
        UpdateWeaponVisibility();
    }

    void UpdateWeaponVisibility()
    {
        bool showRanged = rangedModel != null && Time.time < showRangedUntil;
        if (rangedModel != null && rangedModel.activeSelf != showRanged) rangedModel.SetActive(showRanged);
        if (meleeModel != null && meleeModel.activeSelf == showRanged) meleeModel.SetActive(!showRanged);
    }

    void Update()
    {
        // Sincroniza la vida con el estado global.
        health.maxHealth = CartoGameState.maxHealth;
        if (health.current > health.maxHealth) health.current = health.maxHealth;
        CartoGameState.health = health.current;

        if (dead)
        {
            ApplyGravityOnly();
            return;
        }

        CartoGameState.playTime += Time.deltaTime;
        bool blocked = CartoHUD.Blocking;
        float now = Time.time;
        bool rolling = now < rollUntil;
        health.invulnerable = now < rollUntil - 0.1f || now < safeUntil;

        // --- Direccion de movimiento relativa a la camara ---
        Vector2 input = blocked ? Vector2.zero : CartoInput.Move;
        Vector3 fwd = CameraForward();
        Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
        Vector3 wish = fwd * input.y + right * input.x;
        if (wish.sqrMagnitude > 1f) wish.Normalize();

        if (blocked)
        {
            meleeBuffer = -1f;
            shootBuffer = -1f;
        }
        else
        {
            // Cambiar de arma
            if (CartoInput.NextMeleeDown) CycleWeapon(false);
            if (CartoInput.NextRangedDown) CycleWeapon(true);

            // El clic se recuerda un instante: si llega justo antes de poder atacar, no se pierde.
            if (CartoInput.AttackDown)
            {
                meleeBuffer = now + inputBuffer;
                shootBuffer = -1f;
            }
            else if (CartoInput.ShootDown)
            {
                shootBuffer = now + inputBuffer;
                meleeBuffer = -1f;
            }

            // Esquivar: cancela el ataque en curso.
            if (CartoInput.RollDown && !rolling && now >= nextRoll)
            {
                rollDir = wish.sqrMagnitude > 0.01f ? wish.normalized : transform.forward;
                rollUntil = now + rollDuration;
                nextRoll = now + rollCooldown;
                busyUntil = rollUntil;
                CancelAttack();
                rolling = true;
                transform.rotation = Quaternion.LookRotation(rollDir);
                Play("Roll");
            }

            bool free = !rolling && now >= busyUntil;
            if (free && now <= meleeBuffer)
            {
                meleeBuffer = -1f;
                StartMelee(wish);
            }
            else if (free && now <= shootBuffer && now >= nextShoot)
            {
                shootBuffer = -1f;
                StartShoot();
            }
        }

        if (hitAt > 0f && now >= hitAt)
        {
            hitAt = -1f;
            DoMeleeHit();
        }
        if (fireAt > 0f && now >= fireAt)
        {
            fireAt = -1f;
            DoShoot();
        }

        // --- Movimiento: se puede caminar y correr tambien mientras se ataca ---
        Vector3 velocity;
        Vector2 targetAnim = Vector2.zero;
        bool aiming = now < aimUntil;
        if (rolling)
        {
            velocity = rollDir * rollSpeed;
        }
        else
        {
            bool sprint = CartoInput.Sprint && !blocked;
            float gait = sprint ? 1.6f : 1f;
            velocity = wish * (sprint ? sprintSpeed : walkSpeed);
            bool moving = wish.sqrMagnitude > 0.01f;

            // Mientras dura el golpe el cuerpo gira hacia el objetivo; si no, hacia donde se camina.
            Vector3 face = Vector3.zero;
            if (aiming)
            {
                if (aimTarget != null && !aimTarget.IsDead) face = aimTarget.transform.position - transform.position;
                else face = aimDir;
            }
            else if (moving) face = wish;
            face.y = 0f;
            if (face.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(face);
                float turn = aiming ? turnSpeed * 1.5f : turnSpeed;
                transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-turn * Time.deltaTime));
            }

            if (moving)
            {
                if (aiming && hasStrafe)
                {
                    // Mirando al enemigo y caminando hacia otro lado: las piernas corren de lado o hacia atras.
                    Vector3 local = transform.InverseTransformDirection(wish);
                    targetAnim = new Vector2(local.x, local.z) * gait;
                }
                else targetAnim = new Vector2(0f, gait);
            }
        }

        if (cc.isGrounded && vy < 0f) vy = -2f;
        vy += gravity * Time.deltaTime;
        velocity.y = vy;
        cc.Move(velocity * Time.deltaTime);

        animMove = Vector2.Lerp(animMove, targetAnim, 1f - Mathf.Exp(-12f * Time.deltaTime));
        if (anim != null)
        {
            anim.SetFloat("Speed", hasStrafe ? animMove.y : animMove.magnitude);
            if (hasStrafe) anim.SetFloat("Strafe", animMove.x);
            if (hasUpper)
            {
                // La capa de torso y brazos solo pesa mientras dura el ataque.
                float target = now < upperUntil && !rolling ? 1f : 0f;
                upperWeight = Mathf.MoveTowards(upperWeight, target, Time.deltaTime / (target > upperWeight ? 0.06f : 0.14f));
                anim.SetLayerWeight(UpperLayer, upperWeight);
            }
        }
        UpdateWeaponVisibility();

        // Red de seguridad por si cae fuera del mapa.
        if (transform.position.y < -20f) Teleport(spawnPos);
    }

    void ApplyGravityOnly()
    {
        if (cc == null || !cc.enabled) return;
        if (cc.isGrounded && vy < 0f) vy = -2f;
        vy += gravity * Time.deltaTime;
        cc.Move(new Vector3(0f, vy, 0f) * Time.deltaTime);
    }

    public void Teleport(Vector3 pos)
    {
        cc.enabled = false;
        transform.position = pos;
        cc.enabled = true;
        vy = 0f;
    }

    /// <summary>Devuelve al cartografo a la vida, con la vida completa, en un punto de control.</summary>
    public void Revive(Vector3 pos, float yaw)
    {
        dead = false;
        health.maxHealth = CartoGameState.maxHealth;
        health.current = CartoGameState.maxHealth;
        CartoGameState.health = health.current;
        busyUntil = 0f;
        rollUntil = 0f;
        CancelAttack();
        ResetUpperBody();
        animMove = Vector2.zero;
        safeUntil = Time.time + 2f;
        Teleport(pos);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        spawnPos = pos;
        Play("Locomotion");
    }

    Vector3 CameraForward()
    {
        Camera cam = Camera.main;
        Vector3 f = cam != null ? cam.transform.forward : transform.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 0.0001f) f = transform.forward;
        return f.normalized;
    }

    void Play(string state)
    {
        if (anim != null) anim.CrossFadeInFixedTime(state, 0.08f, 0, 0f);
    }

    /// <summary>
    /// Reproduce el estado de animacion propio del arma (estilo de su era) en la capa "UpperBody":
    /// solo mueve torso y brazos, asi las piernas siguen caminando o corriendo. Si el arma no define
    /// estado o el Animator no lo tiene (escenas sin regenerar), usa el de respaldo; y si no hay capa
    /// de torso, lo reproduce a cuerpo entero como antes. 'seconds' = cuanto dura el gesto.
    /// </summary>
    void PlayWeapon(CartoWeaponDef def, string fallback, float seconds)
    {
        if (anim == null) return;
        int layer = hasUpper ? UpperLayer : 0;
        string state = def != null ? def.attackState : null;
        if (string.IsNullOrEmpty(state) || !anim.HasState(layer, Animator.StringToHash(state))) state = fallback;
        if (layer == UpperLayer && !anim.HasState(UpperLayer, Animator.StringToHash(state))) layer = 0;
        if (layer == 0)
        {
            Play(state);
            return;
        }
        // Desde reposo entra directo (la capa aun no pesa); encadenando golpes, con una mezcla corta.
        if (upperWeight < 0.05f) anim.Play(state, UpperLayer, 0f);
        else anim.CrossFadeInFixedTime(state, 0.06f, UpperLayer, 0f);
        upperUntil = Time.time + seconds;
    }

    /// <summary>Corta el ataque en curso (al esquivar, morir o reaparecer): ni golpe ni disparo pendientes.</summary>
    void CancelAttack()
    {
        hitAt = -1f;
        fireAt = -1f;
        meleeBuffer = -1f;
        shootBuffer = -1f;
        aimUntil = 0f;
        aimTarget = null;
        upperUntil = 0f;
    }

    void ResetUpperBody()
    {
        upperWeight = 0f;
        if (anim != null && hasUpper) anim.SetLayerWeight(UpperLayer, 0f);
    }

    // ------------------------------------------------------------------ armas

    void CycleWeapon(bool ranged)
    {
        CartoWeaponLibrary lib = CartoWeaponLibrary.Instance;
        if (lib == null) return;
        var list = new System.Collections.Generic.List<string>();
        for (int i = 0; i < CartoGameState.owned.Count; i++)
        {
            CartoWeaponDef d = lib.Get(CartoGameState.owned[i]);
            if (d != null && d.ranged == ranged) list.Add(d.id);
        }
        if (list.Count == 0)
        {
            if (ranged) CartoHUD.Toast("Aún no tienes armas a distancia", 2f);
            return;
        }
        string currentId = ranged ? CartoGameState.rangedId : CartoGameState.meleeId;
        int idx = list.IndexOf(currentId);
        string next = list[(idx + 1) % list.Count];
        if (ranged) CartoGameState.rangedId = next; else CartoGameState.meleeId = next;
        RefreshWeapons();
        CartoWeaponDef nd = lib.Get(next);
        if (nd != null && list.Count > 1) CartoHUD.Toast("Equipado: " + nd.displayName, 1.5f);
    }

    CartoEnemy NearestEnemy(float maxDist, float maxAngle, Vector3 forward)
    {
        CartoEnemy best = null;
        float bestD = maxDist;
        for (int i = 0; i < CartoEnemy.All.Count; i++)
        {
            CartoEnemy e = CartoEnemy.All[i];
            if (e == null || e.IsDead) continue;
            Vector3 to = e.transform.position - transform.position;
            to.y = 0f;
            float d = to.magnitude;
            if (d > bestD) continue;
            if (d > 0.01f && Vector3.Angle(forward, to) > maxAngle) continue;
            bestD = d;
            best = e;
        }
        return best;
    }

    void FaceDirection(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir.normalized);
    }

    void StartMelee(Vector3 wish)
    {
        CartoWeaponLibrary lib = CartoWeaponLibrary.Instance;
        CartoWeaponDef def = lib != null ? lib.Get(CartoGameState.meleeId) : null;
        float cooldown = def != null ? def.cooldown : 0.65f;
        float delay = def != null ? def.hitDelay : 0.26f;
        float range = def != null ? def.range : 2f;

        // El cuerpo gira (suave) hacia el enemigo mas cercano hasta que cae el golpe. Sin enemigo
        // cerca, golpea hacia donde se camina o, estando quieto, hacia donde mira la camara.
        aimTarget = NearestEnemy(range + 3f, 180f, transform.forward);
        aimDir = wish.sqrMagnitude > 0.01f ? wish.normalized : CameraForward();
        aimUntil = Time.time + delay + 0.12f;

        showRangedUntil = 0f;
        PlayWeapon(def, def != null && def.twoHanded ? "Attack2H" : "Attack1H", cooldown);
        hitAt = Time.time + delay;
        // Recuperacion corta: el siguiente golpe (o el que quedo en el buffer) sale antes de que
        // termine el gesto, pero nunca antes de que haya caido este.
        busyUntil = Time.time + Mathf.Max(delay + 0.12f, cooldown * attackRecovery);
    }

    void DoMeleeHit()
    {
        CartoWeaponLibrary lib = CartoWeaponLibrary.Instance;
        CartoWeaponDef def = lib != null ? lib.Get(CartoGameState.meleeId) : null;
        float range = def != null ? def.range : 2f;
        float dmg = (def != null ? def.damage : 10f) * CartoGameState.DamageMultiplier(CartoGameState.meleeId);

        // Copia la lista porque un enemigo puede morir (y salir de la lista) durante el bucle.
        CartoEnemy[] enemies = CartoEnemy.All.ToArray();
        for (int i = 0; i < enemies.Length; i++)
        {
            CartoEnemy e = enemies[i];
            if (e == null || e.IsDead) continue;
            Vector3 to = e.transform.position - transform.position;
            if (Mathf.Abs(to.y) > 3f) continue;
            to.y = 0f;
            float d = to.magnitude;
            if (d > range + e.Radius) continue;
            if (d > 0.6f && Vector3.Angle(transform.forward, to) > 80f) continue;
            float final = Mathf.Round(dmg * Random.Range(0.9f, 1.1f));
            e.health.TakeDamage(final, transform.position);
        }
    }

    void StartShoot()
    {
        CartoWeaponLibrary lib = CartoWeaponLibrary.Instance;
        CartoWeaponDef def = lib != null ? lib.Get(CartoGameState.rangedId) : null;
        if (def == null)
        {
            CartoHUD.Toast("Aún no tienes armas a distancia", 2f);
            return;
        }
        Vector3 camF = CameraForward();
        CartoEnemy target = NearestEnemy(30f, 35f, camF);
        if (target != null) FaceDirection(target.transform.position - transform.position);
        else FaceDirection(camF);
        aimTarget = target;
        aimDir = transform.forward;
        aimUntil = Time.time + def.hitDelay + 0.1f;

        showRangedUntil = Time.time + 0.7f;
        UpdateWeaponVisibility();
        PlayWeapon(def, "Shoot", Mathf.Min(0.5f, def.cooldown));
        fireAt = Time.time + def.hitDelay;
        busyUntil = Time.time + Mathf.Min(0.45f, def.cooldown);
        nextShoot = Time.time + def.cooldown;
    }

    void DoShoot()
    {
        CartoWeaponLibrary lib = CartoWeaponLibrary.Instance;
        CartoWeaponDef def = lib != null ? lib.Get(CartoGameState.rangedId) : null;
        if (def == null) return;

        Vector3 origin = transform.position + Vector3.up * (Height * 0.72f) + transform.forward * 0.6f;
        Vector3 dir = transform.forward;
        CartoEnemy target = NearestEnemy(30f, 40f, transform.forward);
        if (target != null)
        {
            Vector3 aim = target.transform.position + Vector3.up * (target.Height * 0.6f);
            dir = (aim - origin).normalized;
        }
        float dmg = Mathf.Round(def.damage * CartoGameState.DamageMultiplier(def.id));
        CartoProjectile.Spawn(origin, dir, def.projectileSpeed, dmg, 0,
            def.projectileUsesModel ? def.prefab : null, def.projectileEuler,
            def.projectileUsesModel ? new Color(0.8f, 0.9f, 1f) : new Color(1f, 0.75f, 0.2f));
    }

    // ------------------------------------------------------------------ dano

    void OnDamaged(float amount, Vector3 from)
    {
        CartoHUD.DamageFlash();
        CartoFeel.Shake(0.13f, 0.22f);
        CartoHUD.AddFloater(transform.position + Vector3.up * Height, "-" + Mathf.RoundToInt(amount), new Color(1f, 0.35f, 0.3f));
    }

    void OnDied()
    {
        dead = true;
        CancelAttack();
        ResetUpperBody();
        Play("Death");
        if (anim != null) anim.SetFloat("Speed", 0f);
        if (CartoGame.Instance != null) CartoGame.Instance.OnPlayerDied();
    }
}
