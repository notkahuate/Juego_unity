using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Construye automaticamente las tres escenas del juego (Nexo, Era Pirata, Era Samurai)
/// usando los assets del package (personajes, armas, cristales, isla pirata).
/// Menu:  CartoGame > Construir juego (crear escenas)
/// </summary>
public static class CartoGameBuilder
{
    const string Gen = "Assets/CartoGame/Generated";
    const string MatDir = "Assets/CartoGame/Generated/Materials";
    const string SceneDir = "Assets/CartoGame/Generated/Scenes";

    static readonly List<string> missing = new List<string>();
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static AnimatorController controller;
    static List<CartoWeaponDef> weaponDefs;
    static System.Random rng = new System.Random(7);

    public static string HubPath { get { return SceneDir + "/" + CartoGameState.SceneHub + ".unity"; } }
    static string PiratePath { get { return SceneDir + "/" + CartoGameState.ScenePirate + ".unity"; } }
    static string SamuraiPath { get { return SceneDir + "/" + CartoGameState.SceneSamurai + ".unity"; } }

    [MenuItem("CartoGame/Construir juego (crear escenas)")]
    public static void BuildAll()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("CartoGame", "Deten el modo Play antes de construir el juego.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        missing.Clear();
        mats.Clear();
        rng = new System.Random(7);
        EnsureFolder(Gen);
        EnsureFolder(MatDir);
        EnsureFolder(SceneDir);

        try
        {
            EditorUtility.DisplayProgressBar("CartoGame", "Creando animaciones...", 0.1f);
            controller = BuildController();

            weaponDefs = CartoWeaponLibrary.CreateDefaults();
            for (int i = 0; i < weaponDefs.Count; i++)
            {
                weaponDefs[i].prefab = FindPrefab(weaponDefs[i].prefabName, weaponDefs[i].prefabHint);
                if (weaponDefs[i].prefab == null) AddMissing(weaponDefs[i].prefabName);
            }

            EditorUtility.DisplayProgressBar("CartoGame", "Construyendo el Nexo...", 0.3f);
            BuildHub();
            EditorUtility.DisplayProgressBar("CartoGame", "Construyendo la Era Pirata...", 0.55f);
            BuildPirate();
            EditorUtility.DisplayProgressBar("CartoGame", "Construyendo la Era Samurai...", 0.8f);
            BuildSamurai();

            AddScenesToBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(HubPath);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        string msg = "Juego construido.\n\nSe abrio la escena del Nexo (Carto_Hub). Pulsa Play para jugar.";
        if (missing.Count > 0)
        {
            msg += "\n\nAVISO: no se encontraron estos assets (se usaron figuras basicas en su lugar):\n- " +
                   string.Join("\n- ", missing.ToArray()) +
                   "\n\nImporta primero carto-game.unitypackage y vuelve a ejecutar este menu.";
        }
        EditorUtility.DisplayDialog("CartoGame", msg, "OK");
    }

    [MenuItem("CartoGame/Abrir escena del Nexo")]
    public static void OpenHub()
    {
        if (!File.Exists(HubPath))
        {
            EditorUtility.DisplayDialog("CartoGame", "Aun no existen las escenas. Usa primero: CartoGame > Construir juego.", "OK");
            return;
        }
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(HubPath);
    }

    // =====================================================================
    //  Utilidades de assets
    // =====================================================================

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string name = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    static void AddMissing(string name)
    {
        if (!missing.Contains(name)) missing.Add(name);
    }

    static GameObject FindPrefab(string name, string hint)
    {
        if (string.IsNullOrEmpty(name)) return null;
        string[] guids = AssetDatabase.FindAssets(name + " t:Prefab");
        string best = null;
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), name, System.StringComparison.OrdinalIgnoreCase)) continue;
            if (path.StartsWith(Gen)) continue;
            bool hintOk = !string.IsNullOrEmpty(hint) && path.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (hintOk) { best = path; break; }
            if (best == null) best = path;
        }
        if (best == null)
        {
            // Busqueda de respaldo: recorre todos los prefabs del proyecto.
            string[] all = AssetDatabase.FindAssets("t:Prefab");
            for (int i = 0; i < all.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(all[i]);
                if (path.StartsWith(Gen)) continue;
                if (!string.Equals(Path.GetFileNameWithoutExtension(path), name, System.StringComparison.OrdinalIgnoreCase)) continue;
                bool hintOk = !string.IsNullOrEmpty(hint) && path.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (hintOk) { best = path; break; }
                if (best == null) best = path;
            }
        }
        return best == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(best);
    }

    static AnimationClip FindClip(string name)
    {
        AnimationClip found = FindClipIn(AssetDatabase.FindAssets(name + " t:AnimationClip"), name);
        if (found == null) found = FindClipIn(AssetDatabase.FindAssets(name + " t:Model"), name);
        if (found == null) found = FindClipIn(AssetDatabase.FindAssets("t:AnimationClip"), name);
        if (found == null) AddMissing("Animacion " + name);
        return found;
    }

    static AnimationClip FindClipIn(string[] guids, string name)
    {
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int j = 0; j < all.Length; j++)
            {
                AnimationClip clip = all[j] as AnimationClip;
                if (clip != null && clip.name == name) return clip;
            }
        }
        return null;
    }

    static Material Mat(string name, Color color, float emission = 0f, float smoothness = 0.15f)
    {
        Material m;
        if (mats.TryGetValue(name, out m) && m != null) return m;
        string path = MatDir + "/Carto_" + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        m = CartoUtil.MakeMaterial(color, emission);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(m, path);
        mats[name] = m;
        return m;
    }

    // =====================================================================
    //  Animator
    // =====================================================================

    static AnimatorController BuildController()
    {
        string path = Gen + "/CartoHumanoid.controller";
        AssetDatabase.DeleteAsset(path);
        AnimatorController ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        AnimatorStateMachine sm = ctrl.layers[0].stateMachine;

        BlendTree tree;
        AnimatorState loco = ctrl.CreateBlendTreeInController("Locomotion", out tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        AnimationClip idle = FindClip("Idle");
        AnimationClip run = FindClip("RunForward");
        AnimationClip sprint = FindClip("Sprint");
        if (idle != null) tree.AddChild(idle, 0f);
        if (run != null) tree.AddChild(run, 1f);
        if (sprint != null) tree.AddChild(sprint, 1.6f);
        sm.defaultState = loco;

        OneShot(sm, loco, "Attack1H", FindClip("MeleeAttack_OneHanded"), 0.7f, true);
        OneShot(sm, loco, "Attack2H", FindClip("MeleeAttack_TwoHanded"), 0.9f, true);
        OneShot(sm, loco, "Shoot", FindClip("PunchRight"), 0.5f, true);
        OneShot(sm, loco, "Hit", FindClip("GetHit"), 0.45f, true);
        OneShot(sm, loco, "Roll", FindClip("RollForward"), 0.6f, true);
        OneShot(sm, loco, "Death", FindClip("Death"), 1.6f, false);

        EditorUtility.SetDirty(ctrl);
        return ctrl;
    }

    static void OneShot(AnimatorStateMachine sm, AnimatorState loco, string name, AnimationClip clip, float duration, bool returns)
    {
        AnimatorState st = sm.AddState(name);
        st.motion = clip;
        if (clip != null && clip.length > 0.01f) st.speed = Mathf.Clamp(clip.length / duration, 0.3f, 4f);
        if (returns)
        {
            AnimatorStateTransition t = st.AddTransition(loco);
            t.hasExitTime = true;
            t.exitTime = 0.9f;
            t.hasFixedDuration = true;
            t.duration = 0.12f;
        }
    }

    // =====================================================================
    //  Utilidades de escena
    // =====================================================================

    static Bounds GetBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    static void GroundTo(GameObject go, float y)
    {
        Bounds b = GetBounds(go);
        if (b.size.sqrMagnitude < 0.000001f) return;
        go.transform.position += Vector3.up * (y - b.min.y);
    }

    static void FitHeight(GameObject go, float height, float groundY)
    {
        Bounds b = GetBounds(go);
        if (b.size.y > 0.0001f) go.transform.localScale *= height / b.size.y;
        GroundTo(go, groundY);
    }

    /// <summary>Instancia un prefab del package como objeto normal (desempaquetado).</summary>
    static GameObject Spawn(string name, string hint)
    {
        GameObject prefab = FindPrefab(name, hint);
        if (prefab == null)
        {
            AddMissing(name);
            return null;
        }
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (go == null) return null;
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = prefab.name;
        return go;
    }

    /// <summary>Coloca un objeto de decoracion apoyado en el suelo.</summary>
    static GameObject Place(string name, string hint, Vector3 pos, float yaw, float scale, Transform parent,
        bool colliders = true, bool ground = true)
    {
        GameObject go = Spawn(name, hint);
        if (go == null)
        {
            go = new GameObject(name + " (falta)");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            return go;
        }
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;
        go.transform.localScale = go.transform.localScale * scale;
        if (ground) GroundTo(go, pos.y);

        Collider[] existing = go.GetComponentsInChildren<Collider>();
        if (!colliders)
        {
            for (int i = 0; i < existing.Length; i++) Object.DestroyImmediate(existing[i]);
        }
        else
        {
            MeshFilter[] mfs = go.GetComponentsInChildren<MeshFilter>();
            for (int i = 0; i < mfs.Length; i++)
            {
                if (mfs[i].sharedMesh == null) continue;
                if (mfs[i].GetComponent<Collider>() != null) continue;
                mfs[i].gameObject.AddComponent<MeshCollider>();
            }
        }
        return go;
    }

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale,
        Material mat, bool collider = true, float yaw = 0f)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = scale;
        Collider col = go.GetComponent<Collider>();
        if (!collider)
        {
            if (col != null) Object.DestroyImmediate(col);
        }
        else if (type == PrimitiveType.Cylinder)
        {
            if (col != null) Object.DestroyImmediate(col);
            go.AddComponent<MeshCollider>();
        }
        Renderer r = go.GetComponent<Renderer>();
        if (r != null && mat != null) r.sharedMaterial = mat;
        return go;
    }

    static void Wall(Transform parent, Vector3 center, Vector3 size, float yaw = 0f)
    {
        GameObject go = new GameObject("MuroInvisible");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        BoxCollider b = go.AddComponent<BoxCollider>();
        b.size = size;
    }

    static Light PointLight(Transform parent, Vector3 localPos, Color color, float range, float intensity)
    {
        GameObject go = new GameObject("Luz");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
        return l;
    }

    static void Sun(Vector3 euler, Color color, float intensity)
    {
        GameObject go = new GameObject("Sol");
        go.transform.rotation = Quaternion.Euler(euler);
        Light l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.color = color;
        l.intensity = intensity;
        l.shadows = LightShadows.Soft;
    }

    static void Ambient(Color sky, Color equator, Color groundColor, Color fog, float fogStart, float fogEnd)
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = sky;
        RenderSettings.ambientEquatorColor = equator;
        RenderSettings.ambientGroundColor = groundColor;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = fog;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance = fogEnd;
    }

    static float Rand(float a, float b)
    {
        return a + (float)rng.NextDouble() * (b - a);
    }

    /// <summary>Crea el controlador de la escena, la camara y el cartografo.</summary>
    static void SetupCommon(CartoGame.Zone zone, string title, string objective, Vector3 spawn, float yaw,
        bool skybox, Color background)
    {
        GameObject gm = new GameObject("_CartoGame");
        CartoGame game = gm.AddComponent<CartoGame>();
        game.zone = zone;
        game.zoneTitle = title;
        game.objective = objective;
        gm.AddComponent<CartoHUD>();
        CartoWeaponLibrary lib = gm.AddComponent<CartoWeaponLibrary>();
        lib.weapons = new List<CartoWeaponDef>(weaponDefs);

        GameObject camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        Camera cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 900f;
        cam.clearFlags = skybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        cam.backgroundColor = background;
        camGo.AddComponent<AudioListener>();
        CartoCamera cc = camGo.AddComponent<CartoCamera>();
        cc.yaw = yaw;
        camGo.transform.position = spawn + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 3.5f, -6f);
        camGo.transform.rotation = Quaternion.Euler(18f, yaw, 0f);

        BuildPlayer(spawn, yaw);
    }

    static void SetupAnimator(GameObject model)
    {
        Animator a = model.GetComponentInChildren<Animator>();
        if (a == null) a = model.AddComponent<Animator>();
        a.runtimeAnimatorController = controller;
        a.applyRootMotion = false;
        a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        Collider[] cols = model.GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++) Object.DestroyImmediate(cols[i]);
        Rigidbody[] rbs = model.GetComponentsInChildren<Rigidbody>();
        for (int i = 0; i < rbs.Length; i++) Object.DestroyImmediate(rbs[i]);
    }

    static GameObject CharacterModel(string prefabName, string hint, Transform parent, float scale, Material fallbackMat)
    {
        GameObject model = Spawn(prefabName, hint);
        if (model == null)
        {
            model = new GameObject(prefabName + " (falta)");
            model.transform.SetParent(parent, false);
            Prim(PrimitiveType.Capsule, "Cuerpo", model.transform, new Vector3(0f, 0.95f, 0f), new Vector3(0.8f, 0.95f, 0.8f), fallbackMat, false);
            model.transform.localScale = Vector3.one * scale;
            return model;
        }
        model.transform.SetParent(parent, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = model.transform.localScale * scale;
        SetupAnimator(model);
        return model;
    }

    static void BuildPlayer(Vector3 pos, float yaw)
    {
        GameObject root = new GameObject("Cartografo");
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        CharacterModel("HumanMale_Character_FREE", "Blink", root.transform, 1f, Mat("Jugador", new Color(0.2f, 0.5f, 0.9f)));

        CharacterController cc = root.AddComponent<CharacterController>();
        cc.height = 1.85f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0.95f, 0f);
        cc.stepOffset = 0.4f;
        cc.slopeLimit = 50f;
        CartoHealth h = root.AddComponent<CartoHealth>();
        h.team = 0;
        h.maxHealth = 100f;
        h.current = 100f;
        h.displayName = "Cartografo";
        CartoPlayer player = root.AddComponent<CartoPlayer>();
        CartoMount.ApplyOutfit(root.transform, player.outfitPieces);
    }

    static CartoEnemy SpawnEnemy(Transform parent, string prefabName, string hint, string display, Vector3 pos,
        float scale, float hp, float damage, float speed, int goldMin, int goldMax, int xp,
        string weaponName, string weaponHint, bool twoHanded, bool ranged, bool boss)
    {
        GameObject root = new GameObject((boss ? "Jefe_" : "Enemigo_") + display);
        root.transform.SetParent(parent, false);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        GameObject model = CharacterModel(prefabName, hint, root.transform, scale, Mat("EnemigoBase", new Color(0.8f, 0.2f, 0.2f)));

        float height = GetBounds(model).size.y;
        if (height < 0.8f || height > 6f) height = 1.8f * scale;
        CharacterController cc = root.AddComponent<CharacterController>();
        cc.height = height * 0.95f;
        cc.radius = Mathf.Clamp(height * 0.2f, 0.3f, 0.7f);
        cc.center = new Vector3(0f, cc.height * 0.5f + 0.03f, 0f);
        cc.stepOffset = 0.4f;

        CartoHealth h = root.AddComponent<CartoHealth>();
        h.team = 1;
        h.maxHealth = hp;
        h.current = hp;
        h.displayName = display;
        h.isBoss = boss;

        CartoEnemy e = root.AddComponent<CartoEnemy>();
        e.damage = damage;
        e.moveSpeed = speed;
        e.goldMin = goldMin;
        e.goldMax = goldMax;
        e.xp = xp;
        e.twoHandedAnim = twoHanded;
        e.ranged = ranged;
        e.attackRange = (twoHanded ? 2.8f : 2.3f) * Mathf.Max(1f, scale);
        e.windup = boss ? 0.6f : 0.5f;
        e.recover = boss ? 0.7f : 0.9f;
        e.aggroRange = boss ? 13f : 15f;
        e.weaponPrefab = FindPrefab(weaponName, weaponHint);
        if (ranged) e.projectilePrefab = e.weaponPrefab;
        return e;
    }

    static void WeaponPickup(Transform parent, string weaponId, Vector3 pos, Color color)
    {
        GameObject root = new GameObject("Arma_" + weaponId);
        root.transform.SetParent(parent, false);
        root.transform.position = pos;
        CartoWeaponPickup p = root.AddComponent<CartoWeaponPickup>();
        p.weaponId = weaponId;

        Prim(PrimitiveType.Cylinder, "Base", root.transform, new Vector3(0f, 0.06f, 0f), new Vector3(1.6f, 0.06f, 1.6f),
            Mat("Brillo_" + weaponId, color, 1.2f), false);
        GameObject holder = new GameObject("Giro");
        holder.transform.SetParent(root.transform, false);
        holder.transform.localPosition = new Vector3(0f, 1.3f, 0f);
        CartoSpin spin = holder.AddComponent<CartoSpin>();
        spin.degreesPerSecond = new Vector3(0f, 90f, 0f);
        spin.bobHeight = 0.15f;

        CartoWeaponDef def = null;
        for (int i = 0; i < weaponDefs.Count; i++) if (weaponDefs[i].id == weaponId) def = weaponDefs[i];
        if (def != null && def.prefab != null)
        {
            GameObject m = Spawn(def.prefabName, def.prefabHint);
            if (m != null)
            {
                m.transform.SetParent(holder.transform, false);
                m.transform.localPosition = Vector3.zero;
                m.transform.localScale = m.transform.localScale * 1.7f;
                Collider[] cols = m.GetComponentsInChildren<Collider>();
                for (int i = 0; i < cols.Length; i++) Object.DestroyImmediate(cols[i]);
            }
        }
        else
        {
            Prim(PrimitiveType.Cube, "Modelo", holder.transform, Vector3.zero, new Vector3(0.15f, 1f, 0.15f), Mat("Brillo_" + weaponId, color, 1.2f), false);
        }
        PointLight(root.transform, new Vector3(0f, 1.5f, 0f), color, 7f, 2f);
    }

    static void CrystalGoal(Transform parent, int era, string eraName, Vector3 pos, CartoHealth guardian,
        string crystalName, Color color)
    {
        GameObject root = new GameObject("CristalDelTiempo");
        root.transform.SetParent(parent, false);
        root.transform.position = pos;
        Prim(PrimitiveType.Cylinder, "Pedestal", root.transform, new Vector3(0f, 0.3f, 0f), new Vector3(2.4f, 0.3f, 2.4f),
            Mat("Piedra", new Color(0.42f, 0.4f, 0.45f)));

        GameObject visual = new GameObject("Cristal");
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        CartoSpin spin = visual.AddComponent<CartoSpin>();
        spin.degreesPerSecond = new Vector3(0f, 45f, 0f);
        spin.bobHeight = 0.12f;
        GameObject c = Place(crystalName, "BUILT_IN", pos + Vector3.up * 0.7f, 0f, 1f, visual.transform, false, false);
        FitHeight(c, 2.6f, pos.y + 0.7f);
        if (c.GetComponentsInChildren<Renderer>().Length == 0)
            Prim(PrimitiveType.Cube, "CristalBasico", visual.transform, new Vector3(0f, 1.4f, 0f), new Vector3(0.8f, 2.4f, 0.8f), Mat("CristalBasico", color, 1.5f), false);
        PointLight(visual.transform, new Vector3(0f, 1.6f, 0f), color, 14f, 3f);

        GameObject barrier = new GameObject("Sello");
        barrier.transform.SetParent(root.transform, false);
        Material sealMat = Mat("Sello", new Color(1f, 0.2f, 0.25f), 2f);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 2f / 8f;
            Prim(PrimitiveType.Cube, "Barra", barrier.transform, new Vector3(Mathf.Sin(a) * 1.7f, 1.9f, Mathf.Cos(a) * 1.7f),
                new Vector3(0.12f, 3.8f, 0.12f), sealMat, false);
        }

        CartoCrystalGoal goal = root.AddComponent<CartoCrystalGoal>();
        goal.era = era;
        goal.eraName = eraName;
        goal.guardian = guardian;
        goal.crystalVisual = visual;
        goal.barrier = barrier;
    }

    static void Portal(Transform parent, Vector3 pos, float yaw, Material frame, Material glow, Color lightColor,
        string scene, string label)
    {
        GameObject root = new GameObject("Portal_" + scene);
        root.transform.SetParent(parent, false);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Prim(PrimitiveType.Cube, "PilarIzq", root.transform, new Vector3(-1.8f, 2.1f, 0f), new Vector3(0.7f, 4.2f, 0.7f), frame);
        Prim(PrimitiveType.Cube, "PilarDer", root.transform, new Vector3(1.8f, 2.1f, 0f), new Vector3(0.7f, 4.2f, 0.7f), frame);
        Prim(PrimitiveType.Cube, "Dintel", root.transform, new Vector3(0f, 4.45f, 0f), new Vector3(5f, 0.55f, 0.9f), frame);
        GameObject g = Prim(PrimitiveType.Sphere, "Vortice", root.transform, new Vector3(0f, 2.1f, 0f), new Vector3(2.9f, 3.9f, 0.25f), glow, false);
        CartoSpin gs = g.AddComponent<CartoSpin>();
        gs.degreesPerSecond = Vector3.zero;
        gs.bobHeight = 0.12f;
        PointLight(root.transform, new Vector3(0f, 2.2f, 1.2f), lightColor, 10f, 2.5f);
        PointLight(root.transform, new Vector3(0f, 2.2f, -1.2f), lightColor, 10f, 2.5f);
        CartoPortal p = root.AddComponent<CartoPortal>();
        p.targetScene = scene;
        p.label = label;
    }

    static void Torii(Transform parent, Vector3 pos, float halfWidth, Material red, Material dark)
    {
        GameObject root = new GameObject("Torii");
        root.transform.SetParent(parent, false);
        root.transform.position = pos;
        Prim(PrimitiveType.Cylinder, "Pilar", root.transform, new Vector3(-halfWidth, 3f, 0f), new Vector3(0.6f, 3f, 0.6f), red);
        Prim(PrimitiveType.Cylinder, "Pilar", root.transform, new Vector3(halfWidth, 3f, 0f), new Vector3(0.6f, 3f, 0.6f), red);
        Prim(PrimitiveType.Cube, "VigaSuperior", root.transform, new Vector3(0f, 6.4f, 0f), new Vector3(halfWidth * 2f + 3f, 0.5f, 0.95f), dark, false);
        Prim(PrimitiveType.Cube, "VigaRoja", root.transform, new Vector3(0f, 5.95f, 0f), new Vector3(halfWidth * 2f + 2.2f, 0.4f, 0.75f), red, false);
        Prim(PrimitiveType.Cube, "VigaInferior", root.transform, new Vector3(0f, 4.9f, 0f), new Vector3(halfWidth * 2f + 1f, 0.35f, 0.5f), red, false);
    }

    static void Sakura(Transform parent, Vector3 pos, float size, Material trunk, Material leaves)
    {
        GameObject root = new GameObject("Sakura");
        root.transform.SetParent(parent, false);
        root.transform.position = pos;
        Prim(PrimitiveType.Cylinder, "Tronco", root.transform, new Vector3(0f, 1.7f * size, 0f), new Vector3(0.5f * size, 1.7f * size, 0.5f * size), trunk);
        for (int i = 0; i < 4; i++)
        {
            Vector3 off = new Vector3(Rand(-1.3f, 1.3f), Rand(3.6f, 5f), Rand(-1.3f, 1.3f)) * size;
            float s = Rand(2.6f, 3.8f) * size;
            Prim(PrimitiveType.Sphere, "Copa", root.transform, off, new Vector3(s, s * 0.8f, s), leaves, false);
        }
    }

    static void Lantern(Transform parent, Vector3 pos, Material stone, Material glow, bool light)
    {
        GameObject root = new GameObject("Farol");
        root.transform.SetParent(parent, false);
        root.transform.position = pos;
        Prim(PrimitiveType.Cube, "Base", root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(0.9f, 0.3f, 0.9f), stone);
        Prim(PrimitiveType.Cube, "Poste", root.transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.35f, 1.2f, 0.35f), stone);
        Prim(PrimitiveType.Cube, "Luz", root.transform, new Vector3(0f, 1.8f, 0f), new Vector3(0.7f, 0.6f, 0.7f), glow, false);
        Prim(PrimitiveType.Cube, "Techo", root.transform, new Vector3(0f, 2.2f, 0f), new Vector3(1.1f, 0.2f, 1.1f), stone, false);
        if (light) PointLight(root.transform, new Vector3(0f, 1.9f, 0f), new Color(1f, 0.75f, 0.4f), 8f, 1.6f);
    }

    static void SaveScene(Scene scene, string path)
    {
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, path);
    }

    static void AddScenesToBuildSettings()
    {
        string[] ours = { HubPath, PiratePath, SamuraiPath };
        List<EditorBuildSettingsScene> list = new List<EditorBuildSettingsScene>();
        for (int i = 0; i < ours.Length; i++) list.Add(new EditorBuildSettingsScene(ours[i], true));
        EditorBuildSettingsScene[] old = EditorBuildSettings.scenes;
        for (int i = 0; i < old.Length; i++)
        {
            bool dup = false;
            for (int j = 0; j < ours.Length; j++) if (old[i].path == ours[j]) dup = true;
            if (!dup) list.Add(old[i]);
        }
        EditorBuildSettings.scenes = list.ToArray();
    }

    // =====================================================================
    //  NEXO (hub)
    // =====================================================================

    static void BuildHub()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Color voidColor = new Color(0.07f, 0.04f, 0.16f);
        Sun(new Vector3(52f, -30f, 0f), new Color(0.82f, 0.78f, 1f), 1.1f);
        Ambient(new Color(0.5f, 0.45f, 0.75f), new Color(0.34f, 0.3f, 0.5f), new Color(0.14f, 0.12f, 0.24f), voidColor, 45f, 190f);

        Transform env = new GameObject("Entorno").transform;
        Material stone = Mat("Piedra", new Color(0.42f, 0.4f, 0.45f));
        Material stoneLight = Mat("PiedraClara", new Color(0.6f, 0.58f, 0.66f));
        Material cyan = Mat("BrilloCian", new Color(0.3f, 0.9f, 1f), 1.6f);
        Material orange = Mat("BrilloNaranja", new Color(1f, 0.6f, 0.15f), 1.8f);
        Material pink = Mat("BrilloRosa", new Color(1f, 0.35f, 0.7f), 1.8f);
        Material wood = Mat("Madera", new Color(0.42f, 0.28f, 0.17f));
        Material red = Mat("RojoTorii", new Color(0.75f, 0.12f, 0.1f));
        Material dark = Mat("Oscuro", new Color(0.12f, 0.1f, 0.12f));
        Material rift = Mat("Grieta", new Color(0.35f, 0.05f, 0.6f), 1.4f);
        Material trunk = Mat("Tronco", new Color(0.3f, 0.2f, 0.15f));
        Material sakura = Mat("Sakura", new Color(1f, 0.68f, 0.8f), 0.15f);

        // Plataforma flotante
        Prim(PrimitiveType.Cylinder, "Plataforma", env, new Vector3(0f, -0.5f, 0f), new Vector3(46f, 0.5f, 46f), stone);
        Prim(PrimitiveType.Cylinder, "AnilloBrillante", env, new Vector3(0f, -0.47f, 0f), new Vector3(19f, 0.495f, 19f), cyan, false);
        Prim(PrimitiveType.Cylinder, "PlataformaCentro2", env, new Vector3(0f, -0.46f, 0f), new Vector3(18f, 0.5f, 18f), stoneLight, false);
        for (int i = 0; i < 24; i++)
        {
            float a = i * 360f / 24f;
            Vector3 p = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 3f, 23.2f);
            Wall(env, p, new Vector3(6.6f, 8f, 0.8f), a);
        }

        // Altar de mejoras
        GameObject altar = new GameObject("AltarDeMejoras");
        altar.transform.SetParent(env, false);
        altar.transform.position = new Vector3(0f, 0f, 11f);
        Prim(PrimitiveType.Cube, "Base", altar.transform, new Vector3(0f, 0.3f, 0f), new Vector3(4.2f, 0.6f, 3f), stoneLight);
        Prim(PrimitiveType.Cube, "Mesa", altar.transform, new Vector3(0f, 0.9f, 0f), new Vector3(2.6f, 0.6f, 1.7f), stone);
        GameObject altarCrystalHolder = new GameObject("CristalAltar");
        altarCrystalHolder.transform.SetParent(altar.transform, false);
        altarCrystalHolder.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        CartoSpin aspin = altarCrystalHolder.AddComponent<CartoSpin>();
        aspin.bobHeight = 0.15f;
        GameObject ac = Place("Crystal_35", "BUILT_IN", new Vector3(0f, 1.5f, 11f), 0f, 1f, altarCrystalHolder.transform, false, false);
        FitHeight(ac, 1.5f, 1.5f);
        PointLight(altar.transform, new Vector3(0f, 2.6f, 0f), new Color(0.4f, 0.9f, 1f), 12f, 3f);
        altar.AddComponent<CartoShop>();

        // Guardiana del altar (decorativa)
        GameObject npc = new GameObject("GuardianaDelAltar");
        npc.transform.SetParent(env, false);
        npc.transform.position = new Vector3(3.6f, 0f, 10.4f);
        npc.transform.rotation = Quaternion.Euler(0f, 200f, 0f);
        CharacterModel("samurai_woman_v2", "Samurai", npc.transform, 1f, pink);
        CapsuleCollider npcCol = npc.AddComponent<CapsuleCollider>();
        npcCol.height = 1.8f;
        npcCol.radius = 0.4f;
        npcCol.center = new Vector3(0f, 0.9f, 0f);

        // Pedestales de los cristales recuperados
        HubPedestal(env, new Vector3(-6f, 0f, 10f), 0, "Crystal_1", new Color(0.3f, 0.9f, 1f), stone);
        HubPedestal(env, new Vector3(6f, 0f, 10f), 1, "Crystal_5", new Color(1f, 0.4f, 0.8f), stone);

        // Grieta temporal (desaparece al reunir todos los cristales)
        GameObject riftGo = Prim(PrimitiveType.Sphere, "GrietaTemporal", env, new Vector3(0f, 17f, 8f), new Vector3(7f, 7f, 7f), rift, false);
        riftGo.AddComponent<CartoSpin>().bobHeight = 0.6f;
        PointLight(riftGo.transform, Vector3.zero, new Color(0.7f, 0.3f, 1f), 40f, 2.5f);
        CartoCrystalDisplay rd = env.gameObject.AddComponent<CartoCrystalDisplay>();
        rd.era = -1;
        rd.invert = true;
        rd.target = riftGo;

        // Portales
        Portal(env, new Vector3(-15f, 0f, 1f), 90f, wood, orange, new Color(1f, 0.6f, 0.2f), CartoGameState.ScenePirate, "Viajar a la Era Pirata");
        Portal(env, new Vector3(15f, 0f, 1f), -90f, red, pink, new Color(1f, 0.4f, 0.7f), CartoGameState.SceneSamurai, "Viajar a la Era Samurái");

        // Fragmentos de la era pirata junto a su portal
        string pp = "3D Pirates";
        Place("02_Market_Barrel_Stack_3", pp, new Vector3(-16.5f, 0f, -4.5f), 30f, 1f, env);
        Place("02_Market_Crate_1", pp, new Vector3(-13.5f, 0f, -5.5f), 10f, 1f, env);
        Place("04_CannonShop_Cannon", pp, new Vector3(-15.5f, 0f, 6.5f), 70f, 1f, env);
        Place("04_CannonShop_CannonBall_Stack", pp, new Vector3(-13.6f, 0f, 7.6f), 0f, 1f, env);
        Place("02_Market_Barrel_Single_Closed", pp, new Vector3(-17.5f, 0f, 9f), 0f, 1f, env);
        Place("07_Tavern_UmbrelaTable_1", pp, new Vector3(-10f, 0f, -13f), 0f, 1f, env);

        // Fragmentos de la era samurai junto a su portal
        Sakura(env, new Vector3(16.5f, 0f, -5.5f), 0.9f, trunk, sakura);
        Sakura(env, new Vector3(16f, 0f, 8.5f), 1f, trunk, sakura);
        Lantern(env, new Vector3(12.5f, 0f, -3.5f), stone, orange, true);
        Lantern(env, new Vector3(12.5f, 0f, 5.5f), stone, orange, true);
        Torii(env, new Vector3(10f, 0f, -14f), 2.6f, red, dark);

        // Restos de eras flotando en el vacio
        string[] crystalNames = { "Crystal_46", "Crystal_50", "Crystal_40", "Crystal_35", "Crystal_2" };
        string[] debris = { "01_House_5", "99_Ship_L1", "01_House_6", "04_CannonShop_Cannon", "02_Market_Barrel_Stack_3" };
        for (int i = 0; i < 18; i++)
        {
            float a = i * 20f + Rand(-6f, 6f);
            float r = Rand(31f, 52f);
            Vector3 p = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, Rand(-9f, 13f), r);
            GameObject holder = new GameObject("Flotante");
            holder.transform.SetParent(env, false);
            holder.transform.position = p;
            CartoSpin sp = holder.AddComponent<CartoSpin>();
            sp.degreesPerSecond = new Vector3(Rand(-4f, 4f), Rand(-12f, 12f), Rand(-4f, 4f));
            sp.bobHeight = Rand(0.4f, 1.2f);
            sp.bobSpeed = Rand(0.3f, 0.8f);
            if (i % 3 == 2)
            {
                GameObject d = Place(debris[(i / 3) % debris.Length], pp, p, Rand(0f, 360f), 1f, holder.transform, false, false);
                d.transform.rotation = Quaternion.Euler(Rand(-25f, 25f), Rand(0f, 360f), Rand(-25f, 25f)) * d.transform.rotation;
            }
            else
            {
                GameObject c = Place(crystalNames[i % crystalNames.Length], "BUILT_IN", p, Rand(0f, 360f), 1f, holder.transform, false, false);
                FitHeight(c, Rand(3f, 7f), p.y);
                if (i % 2 == 0) PointLight(holder.transform, Vector3.up * 2f, i % 4 == 0 ? new Color(0.4f, 0.9f, 1f) : new Color(1f, 0.4f, 0.8f), 18f, 2f);
            }
        }

        SetupCommon(CartoGame.Zone.Hub, "Nexo del Tiempo", "Cruza un portal, recupera los cristales y mejora tus armas en el altar",
            new Vector3(0f, 0.1f, -13f), 0f, false, voidColor);
        SaveScene(scene, HubPath);
    }

    static void HubPedestal(Transform env, Vector3 pos, int era, string crystalName, Color color, Material stone)
    {
        GameObject root = new GameObject("PedestalCristal_" + era);
        root.transform.SetParent(env, false);
        root.transform.position = pos;
        Prim(PrimitiveType.Cylinder, "Pedestal", root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(1.5f, 0.5f, 1.5f), stone);
        GameObject holder = new GameObject("CristalRecuperado");
        holder.transform.SetParent(root.transform, false);
        holder.transform.localPosition = new Vector3(0f, 1.15f, 0f);
        holder.AddComponent<CartoSpin>().bobHeight = 0.1f;
        GameObject c = Place(crystalName, "BUILT_IN", pos + Vector3.up * 1.15f, 0f, 1f, holder.transform, false, false);
        FitHeight(c, 1.7f, pos.y + 1.15f);
        PointLight(holder.transform, new Vector3(0f, 1f, 0f), color, 10f, 3f);
        CartoCrystalDisplay d = root.AddComponent<CartoCrystalDisplay>();
        d.era = era;
        d.target = holder;
    }

    // =====================================================================
    //  ERA PIRATA
    // =====================================================================

    static void BuildPirate()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Color sky = new Color(0.62f, 0.82f, 0.95f);
        Sun(new Vector3(48f, 35f, 0f), new Color(1f, 0.96f, 0.85f), 1.25f);
        Ambient(new Color(0.6f, 0.74f, 0.92f), new Color(0.62f, 0.62f, 0.56f), new Color(0.38f, 0.33f, 0.24f), sky, 90f, 520f);

        Transform env = new GameObject("Entorno").transform;
        Transform enemies = new GameObject("Enemigos").transform;
        Material sand = Mat("Arena", new Color(0.88f, 0.78f, 0.54f));
        Material sandDark = Mat("ArenaCamino", new Color(0.74f, 0.63f, 0.42f));
        Material wood = Mat("Madera", new Color(0.42f, 0.28f, 0.17f));
        Material orange = Mat("BrilloNaranja", new Color(1f, 0.6f, 0.15f), 1.8f);
        Material cyan = Mat("BrilloCian", new Color(0.3f, 0.9f, 1f), 1.6f);
        string pp = "3D Pirates";

        // Suelo y limites
        Prim(PrimitiveType.Cube, "Playa", env, new Vector3(0f, -1f, 0f), new Vector3(64f, 2f, 112f), sand);
        Prim(PrimitiveType.Cube, "Camino", env, new Vector3(0f, 0.01f, 0f), new Vector3(7f, 0.04f, 104f), sandDark, false);
        Wall(env, new Vector3(0f, 4f, 56f), new Vector3(66f, 10f, 1f));
        Wall(env, new Vector3(0f, 4f, -56f), new Vector3(66f, 10f, 1f));
        Wall(env, new Vector3(32f, 4f, 0f), new Vector3(1f, 10f, 114f));
        Wall(env, new Vector3(-32f, 4f, 0f), new Vector3(1f, 10f, 114f));

        // Mar, islas y barcos
        GameObject sea = Place("99_Sea", pp, new Vector3(0f, -3.4f, 0f), 0f, 1f, env, false, false);
        Vector3 seaScale = sea.transform.localScale;
        sea.transform.localScale = new Vector3(seaScale.x * 4.5f, seaScale.y, seaScale.z * 4.5f);
        Place("00_Island_1", pp, new Vector3(-200f, -3.4f, 70f), 30f, 1f, env, false, false);
        Place("00_Island_3", pp, new Vector3(205f, -3.4f, -40f), 120f, 1f, env, false, false);
        Place("00_Island_7", pp, new Vector3(30f, -3.4f, 250f), 200f, 1f, env, false, false);
        Place("00_Island_2", pp, new Vector3(-110f, -3.4f, -230f), 75f, 1f, env, false, false);
        Place("99_Ship_L2", pp, new Vector3(0f, -2.4f, 78f), 90f, 3f, env, false, true);
        Place("99_Ship_L1", pp, new Vector3(-56f, -2.4f, 12f), 20f, 2.6f, env, false, true);
        Place("99_Ship_L1", pp, new Vector3(60f, -2.4f, -30f), 160f, 2.6f, env, false, true);

        // Pueblo
        string[] leftHouses = { "01_House_1", "01_House_4", "01_House_2", "01_House_6" };
        float[] leftZ = { -34f, -12f, 12f, 34f };
        string[] rightHouses = { "01_House_3", "01_House_5", "01_House_1", "01_House_4" };
        float[] rightZ = { -30f, -6f, 18f, 40f };
        for (int i = 0; i < 4; i++)
        {
            Place(leftHouses[i], pp, new Vector3(-25f, 0f, leftZ[i]), 90f, 1f, env);
            Place(rightHouses[i], pp, new Vector3(25f, 0f, rightZ[i]), -90f, 1f, env);
        }
        PlaceProp(env, pp, "02_Market_Barrel_Stack_3", -10f, -38f, 20f);
        PlaceProp(env, pp, "02_Market_Crate_1", -8f, -30f, 0f);
        PlaceProp(env, pp, "02_Market_Crate_3", 9f, -36f, 40f);
        PlaceProp(env, pp, "02_Market_Banner_Barrels", 13f, -24f, -90f);
        PlaceProp(env, pp, "03_FishermanHouse_FishermanStand_1", -14f, -22f, 90f);
        PlaceProp(env, pp, "03_FishermanHouse_FishermanCart", 11f, -14f, 30f);
        PlaceProp(env, pp, "03_FishermanHouse_Bucket_Fishes", -9f, -16f, 0f);
        PlaceProp(env, pp, "07_Tavern_UmbrelaTable_1", -13f, -4f, 0f);
        PlaceProp(env, pp, "07_Tavern_UmbrelaTable_2", -12f, 4f, 60f);
        PlaceProp(env, pp, "07_Tavern_Banner", -17f, 0f, 90f);
        PlaceProp(env, pp, "05_Destilery_BigTank", 14f, 2f, 0f);
        PlaceProp(env, pp, "05_Destilery_Rum_Crate_1", 10f, 6f, 15f);
        PlaceProp(env, pp, "05_Destilery_Rum_Bottle_1", 9f, -2f, 0f);
        PlaceProp(env, pp, "02_Market_Sack_Stack", -10f, 11f, 0f);
        PlaceProp(env, pp, "06_MusketShop_Stander_Full", 14f, 14f, -90f);
        PlaceProp(env, pp, "04_CannonShop_Cannon", -9f, 20f, 160f);
        PlaceProp(env, pp, "04_CannonShop_Cannon", 9f, 22f, 200f);
        PlaceProp(env, pp, "04_CannonShop_CannonBall_Stack_2", -11f, 24f, 0f);
        PlaceProp(env, pp, "04_CannonShop_CannonBalls_Crate", 12f, 26f, 0f);
        PlaceProp(env, pp, "06_MusketShop_Barrel_GunPowder", -14f, 30f, 0f);
        PlaceProp(env, pp, "02_Market_Barrel_Single_Closed", 8f, 42f, 0f);
        PlaceProp(env, pp, "02_Market_Barrel_Single_Closed", -8f, 43f, 0f);
        PlaceProp(env, pp, "04_CannonShop_Cannon", -7f, 51f, 180f);
        PlaceProp(env, pp, "04_CannonShop_Cannon", 7f, 51f, 180f);

        // Armas de la era
        WeaponPickup(env, "sable", new Vector3(0f, 0f, -33f), new Color(1f, 0.7f, 0.2f));
        WeaponPickup(env, "pistola", new Vector3(0f, 0f, -3f), new Color(1f, 0.7f, 0.2f));

        // Enemigos
        float[,] crew =
        {
            { -4f, -20f }, { 0f, -17f }, { 4f, -20f },
            { -5f, 5f }, { 5f, 5f }, { -2f, 10f }, { 3f, 11f },
            { -4f, 24f }, { 4f, 24f }, { 0f, 28f }
        };
        for (int i = 0; i < crew.GetLength(0); i++)
        {
            SpawnEnemy(enemies, "SharkCrew", "LowPolyPirate", "Pirata tiburón", new Vector3(crew[i, 0], 0.05f, crew[i, 1]),
                0.85f, 60f, 10f, 3.6f, 7, 13, 15, "Culass2", "Pirate Weapons", false, false, false);
        }
        CartoEnemy boss = SpawnEnemy(enemies, "Captain_Male", "LowPolyPirate", "Capitán Marea Negra", new Vector3(0f, 0.05f, 40f),
            1.15f, 340f, 22f, 4.1f, 70, 90, 120, "Culass1", "Pirate Weapons", false, false, true);

        CrystalGoal(env, 0, "Era Pirata", new Vector3(0f, 0f, 48f), boss.GetComponent<CartoHealth>(), "Crystal_46", new Color(0.3f, 0.9f, 1f));
        Portal(env, new Vector3(0f, 0f, -51f), 0f, wood, cyan, new Color(0.4f, 0.9f, 1f), CartoGameState.SceneHub, "Volver al Nexo");

        SetupCommon(CartoGame.Zone.Pirate, "Era Pirata", "Derrota al Capitán Marea Negra y toma el cristal del tiempo",
            new Vector3(0f, 0.1f, -44f), 0f, true, sky);
        SaveScene(scene, PiratePath);
    }

    static void PlaceProp(Transform env, string hint, string name, float x, float z, float yaw)
    {
        Place(name, hint, new Vector3(x, 0f, z), yaw, 1f, env);
    }

    // =====================================================================
    //  ERA SAMURAI
    // =====================================================================

    static void BuildSamurai()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Color dusk = new Color(0.96f, 0.64f, 0.52f);
        Sun(new Vector3(32f, -140f, 0f), new Color(1f, 0.74f, 0.56f), 1.2f);
        Ambient(new Color(0.78f, 0.55f, 0.58f), new Color(0.58f, 0.44f, 0.48f), new Color(0.24f, 0.2f, 0.22f), dusk, 60f, 420f);

        Transform env = new GameObject("Entorno").transform;
        Transform enemies = new GameObject("Enemigos").transform;
        Material grass = Mat("Hierba", new Color(0.34f, 0.46f, 0.28f));
        Material path = Mat("CaminoPiedra", new Color(0.64f, 0.62f, 0.6f));
        Material stone = Mat("Piedra", new Color(0.42f, 0.4f, 0.45f));
        Material red = Mat("RojoTorii", new Color(0.75f, 0.12f, 0.1f));
        Material dark = Mat("Oscuro", new Color(0.12f, 0.1f, 0.12f));
        Material wood = Mat("Madera", new Color(0.42f, 0.28f, 0.17f));
        Material trunk = Mat("Tronco", new Color(0.3f, 0.2f, 0.15f));
        Material sakura = Mat("Sakura", new Color(1f, 0.68f, 0.8f), 0.15f);
        Material bamboo = Mat("Bambu", new Color(0.45f, 0.62f, 0.25f));
        Material hill = Mat("Colina", new Color(0.3f, 0.26f, 0.4f));
        Material sunMat = Mat("SolPoniente", new Color(1f, 0.42f, 0.25f), 2.2f);
        Material warm = Mat("BrilloNaranja", new Color(1f, 0.6f, 0.15f), 1.8f);
        Material pink = Mat("BrilloRosa", new Color(1f, 0.35f, 0.7f), 1.8f);
        Material cyan = Mat("BrilloCian", new Color(0.3f, 0.9f, 1f), 1.6f);

        // Suelo y limites
        Prim(PrimitiveType.Cube, "Pradera", env, new Vector3(0f, -1f, 0f), new Vector3(64f, 2f, 112f), grass);
        Prim(PrimitiveType.Cube, "Camino", env, new Vector3(0f, 0.01f, 0f), new Vector3(6f, 0.04f, 104f), path, false);
        Prim(PrimitiveType.Cube, "Llanura", env, new Vector3(0f, -1.2f, 0f), new Vector3(900f, 2f, 900f), Mat("HierbaLejana", new Color(0.3f, 0.38f, 0.27f)), false);
        Wall(env, new Vector3(0f, 4f, 56f), new Vector3(66f, 10f, 1f));
        Wall(env, new Vector3(0f, 4f, -56f), new Vector3(66f, 10f, 1f));
        Wall(env, new Vector3(32f, 4f, 0f), new Vector3(1f, 10f, 114f));
        Wall(env, new Vector3(-32f, 4f, 0f), new Vector3(1f, 10f, 114f));

        // Fondo: colinas y sol poniente
        Prim(PrimitiveType.Sphere, "Colina", env, new Vector3(-130f, -30f, 150f), new Vector3(190f, 120f, 190f), hill, false);
        Prim(PrimitiveType.Sphere, "Colina", env, new Vector3(100f, -40f, 190f), new Vector3(240f, 170f, 240f), hill, false);
        Prim(PrimitiveType.Sphere, "Colina", env, new Vector3(180f, -30f, 30f), new Vector3(170f, 100f, 170f), hill, false);
        Prim(PrimitiveType.Sphere, "Colina", env, new Vector3(-185f, -35f, -30f), new Vector3(180f, 120f, 180f), hill, false);
        Prim(PrimitiveType.Sphere, "Colina", env, new Vector3(0f, -40f, -210f), new Vector3(260f, 130f, 200f), hill, false);
        Prim(PrimitiveType.Sphere, "Sol", env, new Vector3(-30f, 46f, 215f), new Vector3(46f, 46f, 46f), sunMat, false);

        // Torii, cerezos, bambu y faroles
        Torii(env, new Vector3(0f, 0f, -38f), 4.2f, red, dark);
        Torii(env, new Vector3(0f, 0f, -8f), 4.2f, red, dark);
        Torii(env, new Vector3(0f, 0f, 20f), 4.2f, red, dark);
        for (int i = 0; i < 9; i++)
        {
            float z = -46f + i * 11.5f;
            Sakura(env, new Vector3(-Rand(10f, 19f), 0f, z + Rand(-2f, 2f)), Rand(0.85f, 1.25f), trunk, sakura);
            Sakura(env, new Vector3(Rand(10f, 19f), 0f, z + Rand(-2f, 2f) + 5f), Rand(0.85f, 1.25f), trunk, sakura);
        }
        for (int i = 0; i < 22; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            Vector3 c = new Vector3(side * Rand(22f, 29f), 0f, Rand(-52f, 52f));
            for (int j = 0; j < 5; j++)
            {
                float hgt = Rand(3f, 5.5f);
                Prim(PrimitiveType.Cylinder, "Bambu", env, c + new Vector3(Rand(-1f, 1f), hgt, Rand(-1f, 1f)), new Vector3(0.18f, hgt, 0.18f), bamboo, false);
            }
        }
        float[] lanternZ = { -28f, -17f, 3f, 12f, 30f };
        for (int i = 0; i < lanternZ.Length; i++)
        {
            Lantern(env, new Vector3(-4.6f, 0f, lanternZ[i]), stone, warm, i % 2 == 0);
            Lantern(env, new Vector3(4.6f, 0f, lanternZ[i]), stone, warm, i % 2 == 1);
        }

        // Dojo del jefe
        Prim(PrimitiveType.Cube, "TarimaDojo", env, new Vector3(0f, 0.1f, 42f), new Vector3(22f, 0.2f, 18f), wood);
        float[,] corners = { { -10f, 34f }, { 10f, 34f }, { -10f, 50f }, { 10f, 50f } };
        for (int i = 0; i < 4; i++)
            Prim(PrimitiveType.Cylinder, "PilarDojo", env, new Vector3(corners[i, 0], 4.6f, corners[i, 1]), new Vector3(0.7f, 4.6f, 0.7f), red);
        Prim(PrimitiveType.Cube, "TechoDojo", env, new Vector3(0f, 9.5f, 42f), new Vector3(25f, 0.6f, 21f), dark, false);
        Prim(PrimitiveType.Cube, "TechoDojo2", env, new Vector3(0f, 10.3f, 42f), new Vector3(17f, 1f, 13f), red, false);
        Prim(PrimitiveType.Cube, "TechoDojo3", env, new Vector3(0f, 11.2f, 42f), new Vector3(20f, 0.5f, 16f), dark, false);

        // Armas de la era
        WeaponPickup(env, "katana", new Vector3(0f, 0f, -33f), new Color(1f, 0.4f, 0.7f));
        WeaponPickup(env, "kunai", new Vector3(0f, 0f, -14f), new Color(1f, 0.4f, 0.7f));

        // Enemigos
        string sp = "LowPolySamurai";
        string sw = "Samurai Warrior";
        float[,] g1 = { { -4f, -22f }, { 4f, -22f }, { 0f, -19f } };
        for (int i = 0; i < 3; i++)
            SpawnEnemy(enemies, "ashigaru_v2", sp, "Ashigaru", new Vector3(g1[i, 0], 0.05f, g1[i, 1]), 1f, 80f, 14f, 3.6f, 9, 15, 20, "Yari", sw, true, false, false);
        SpawnEnemy(enemies, "ashigaru_v2", sp, "Ashigaru", new Vector3(-2.5f, 0.05f, 2f), 1f, 80f, 14f, 3.6f, 9, 15, 20, "Yari", sw, true, false, false);
        SpawnEnemy(enemies, "ashigaru_v2", sp, "Ashigaru", new Vector3(2.5f, 0.05f, 2f), 1f, 80f, 14f, 3.6f, 9, 15, 20, "Yari", sw, true, false, false);
        SpawnEnemy(enemies, "Ninja_v2", sp, "Ninja", new Vector3(-8f, 0.05f, 7f), 1f, 55f, 9f, 5f, 10, 16, 22, "Kunai", sw, false, true, false);
        SpawnEnemy(enemies, "Ninja_v2", sp, "Ninja", new Vector3(8f, 0.05f, 8f), 1f, 55f, 9f, 5f, 10, 16, 22, "Kunai", sw, false, true, false);
        SpawnEnemy(enemies, "Samurai_v2", sp, "Samurái", new Vector3(-3f, 0.05f, 24f), 1f, 120f, 18f, 3.9f, 14, 20, 30, "Katana", sw, true, false, false);
        SpawnEnemy(enemies, "Samurai_v2", sp, "Samurái", new Vector3(3f, 0.05f, 24f), 1f, 120f, 18f, 3.9f, 14, 20, 30, "Katana", sw, true, false, false);
        SpawnEnemy(enemies, "Ninja_v2", sp, "Ninja", new Vector3(0f, 0.05f, 29f), 1f, 55f, 9f, 5f, 10, 16, 22, "Kunai", sw, false, true, false);
        CartoEnemy boss = SpawnEnemy(enemies, "Akudaikan_v2", sp, "Akudaikan, Señor de la Guerra", new Vector3(0f, 0.25f, 41f),
            1.3f, 520f, 28f, 4.2f, 110, 130, 200, "Kanabo", sw, true, false, true);

        CrystalGoal(env, 1, "Era Samurái", new Vector3(0f, 0.2f, 48f), boss.GetComponent<CartoHealth>(), "Crystal_50", new Color(1f, 0.4f, 0.8f));
        Portal(env, new Vector3(0f, 0f, -51f), 0f, red, cyan, new Color(0.4f, 0.9f, 1f), CartoGameState.SceneHub, "Volver al Nexo");

        SetupCommon(CartoGame.Zone.Samurai, "Era Samurái", "Derrota a Akudaikan y toma el cristal del tiempo",
            new Vector3(0f, 0.1f, -44f), 0f, false, dusk);
        SaveScene(scene, SamuraiPath);
    }
}

/// <summary>Al importar el package por primera vez, ofrece construir el juego.</summary>
[InitializeOnLoad]
public static class CartoGameAutoPrompt
{
    static CartoGameAutoPrompt()
    {
        EditorApplication.delayCall += Check;
    }

    static void Check()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists(CartoGameBuilder.HubPath)) return;
        string key = "CartoGame.Prompted." + Application.dataPath;
        if (EditorPrefs.GetBool(key, false)) return;
        EditorPrefs.SetBool(key, true);
        bool go = EditorUtility.DisplayDialog("El Cartógrafo del Tiempo",
            "¿Construir ahora las escenas del juego (Nexo, Era Pirata y Era Samurái)?\n\n" +
            "Asegúrate de haber importado antes carto-game.unitypackage con los assets.\n" +
            "También puedes hacerlo luego desde el menú  CartoGame > Construir juego.",
            "Construir ahora", "Más tarde");
        if (go) CartoGameBuilder.BuildAll();
    }
}
