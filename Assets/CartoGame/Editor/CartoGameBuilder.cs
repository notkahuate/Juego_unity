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
        softDot = null;
        particleMat = null;
        rng = new System.Random(7);
        bool pipelineChanged = EnsureBuiltInPipeline();
        ConfigurePlayer();
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
        if (pipelineChanged)
        {
            msg += "\n\nEl proyecto tenia un Render Pipeline Asset (URP/HDRP) activo y se cambio a Built-in " +
                   "(Graphics y Quality), porque los materiales del juego usan el shader Standard.";
        }
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

    /// <summary>
    /// El juego esta hecho para el Built-in Render Pipeline (shader Standard). Con un Render Pipeline
    /// Asset activo (URP/HDRP) todo se ve magenta, asi que se quita de Graphics y de cada nivel de Quality.
    /// Devuelve true si tuvo que cambiar algo.
    /// </summary>
    static bool EnsureBuiltInPipeline()
    {
        bool changed = false;
        if (GraphicsSettings.defaultRenderPipeline != null)
        {
            GraphicsSettings.defaultRenderPipeline = null;
            changed = true;
        }
        int current = QualitySettings.GetQualityLevel();
        int levels = QualitySettings.names.Length;
        for (int i = 0; i < levels; i++)
        {
            if (QualitySettings.GetRenderPipelineAssetAt(i) == null) continue;
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = null;
            changed = true;
        }
        if (changed)
        {
            QualitySettings.SetQualityLevel(current, false);
            Debug.Log("CartoGame: se desactivo el Render Pipeline Asset; el proyecto usa ahora el Built-in Render Pipeline.");
        }
        return changed;
    }

    /// <summary>
    /// Ajustes del Player para el ejecutable: arranca en pantalla completa a la resolucion del
    /// monitor. En el juego, F11 alterna con una ventana de 1280x720 (CartoGame.ToggleFullscreen).
    /// </summary>
    static void ConfigurePlayer()
    {
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.defaultIsNativeResolution = true;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.resizableWindow = true;
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

    static AnimationClip FindClip(string name, bool optional = false)
    {
        AnimationClip found = FindClipIn(AssetDatabase.FindAssets(name + " t:AnimationClip"), name);
        if (found == null) found = FindClipIn(AssetDatabase.FindAssets(name + " t:Model"), name);
        if (found == null) found = FindClipIn(AssetDatabase.FindAssets("t:AnimationClip"), name);
        if (found == null && !optional) AddMissing("Animacion " + name);
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

    static Texture2D softDot;
    static Material particleMat;

    /// <summary>
    /// Punto de luz difuminado para las particulas. Se dibuja aqui pixel a pixel y se guarda en
    /// Generated: no hace falta importar ninguna textura.
    /// </summary>
    static Texture2D SoftDot()
    {
        if (softDot != null) return softDot;
        string path = Gen + "/Carto_Destello.asset";
        AssetDatabase.DeleteAsset(path);
        const int n = 64;
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, true);
        t.wrapMode = TextureWrapMode.Clamp;
        Color[] px = new Color[n * n];
        float half = (n - 1) * 0.5f;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);
                px[y * n + x] = new Color(1f, 1f, 1f, a * a);
            }
        }
        t.SetPixels(px);
        t.Apply(true);
        AssetDatabase.CreateAsset(t, path);
        softDot = t;
        return t;
    }

    /// <summary>Material aditivo de las particulas (shader incluido en Unity, canal Built-in).</summary>
    static Material ParticleMat()
    {
        if (particleMat != null) return particleMat;
        string path = MatDir + "/Carto_Particula.mat";
        AssetDatabase.DeleteAsset(path);
        Shader sh = Shader.Find("Legacy Shaders/Particles/Additive");
        if (sh == null) sh = Shader.Find("Mobile/Particles/Additive");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        Material m = new Material(sh);
        m.mainTexture = SoftDot();
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        AssetDatabase.CreateAsset(m, path);
        particleMat = m;
        return m;
    }

    /// <summary>
    /// Particulas de la biblia de arte: puntos de luz pequenos que flotan con una deriva suave.
    /// Nacen dentro de una caja de tamano 'area' centrada en 'localPos', con un color entre 'a' y 'b',
    /// y se mueven con la velocidad 'drift' (en el mundo) mas un vaiven 'wander'.
    /// </summary>
    static ParticleSystem Particles(Transform parent, string name, Vector3 localPos, Vector3 area, Color a, Color b,
        float rate, float lifetime, float size, Vector3 drift, float wander)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.45f, size);
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        main.maxParticles = Mathf.CeilToInt(rate * lifetime * 1.3f) + 8;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = rate;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = area;

        ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(drift.x - wander, drift.x + wander);
        vel.y = new ParticleSystem.MinMaxCurve(drift.y - wander * 0.5f, drift.y + wander * 0.5f);
        vel.z = new ParticleSystem.MinMaxCurve(drift.z - wander, drift.z + wander);

        // Aparecen y se apagan poco a poco.
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = ParticleMat();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
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
        ctrl.AddParameter("Strafe", AnimatorControllerParameterType.Float);
        AnimatorStateMachine sm = ctrl.layers[0].stateMachine;

        // Locomocion en dos ejes: Speed = avance (0 quieto, 1 correr, 1.6 sprint, -1 hacia atras) y
        // Strafe = lateral. Los enemigos solo usan Speed (Strafe se queda en 0), igual que antes; el
        // cartografo usa Strafe para correr de lado mientras ataca mirando a su objetivo.
        BlendTree tree;
        AnimatorState loco = ctrl.CreateBlendTreeInController("Locomotion", out tree, 0);
        tree.blendType = BlendTreeType.FreeformCartesian2D;
        tree.blendParameter = "Strafe";
        tree.blendParameterY = "Speed";
        AnimationClip idle = FindClip("Idle");
        AnimationClip run = FindClip("RunForward");
        AnimationClip sprint = FindClip("Sprint");
        AnimationClip back = FindClip("RunBackward", true);
        AnimationClip left = FindClip("RunLeft", true);
        AnimationClip right = FindClip("RunRight", true);
        if (idle != null) tree.AddChild(idle, new Vector2(0f, 0f));
        if (run != null) tree.AddChild(run, new Vector2(0f, 1f));
        if (sprint != null) tree.AddChild(sprint, new Vector2(0f, 1.6f));
        if (back != null) tree.AddChild(back, new Vector2(0f, -1f));
        if (left != null) tree.AddChild(left, new Vector2(-1f, 0f));
        if (right != null) tree.AddChild(right, new Vector2(1f, 0f));
        sm.defaultState = loco;

        AnimationClip oneHanded = FindClip("MeleeAttack_OneHanded");
        AnimationClip twoHanded = FindClip("MeleeAttack_TwoHanded");
        // Disparo / lanzamiento: el arma va en la mano derecha, asi que se usa el brazo derecho al frente.
        // Otros clips del pack para probar aqui: "SpellCast", "BowShot".
        AnimationClip thrust = FindClip("PunchRight");

        // Estados genericos: los usan los enemigos y sirven de respaldo al jugador.
        OneShot(sm, loco, "Attack1H", oneHanded, 0.7f, true);
        OneShot(sm, loco, "Attack2H", twoHanded, 0.9f, true);
        OneShot(sm, loco, "Shoot", thrust, 0.5f, true);

        // Estilo de pelea por era (CartoWeaponDef.attackState). Solo clips Humanoid del proyecto.
        OneShot(sm, loco, "Atk_Cartografo", thrust, 0.5f, true);      // golpe corto con el catalejo
        OneShot(sm, loco, "Atk_Pirata", oneHanded, 0.6f, true);       // tajo rapido a una mano
        OneShot(sm, loco, "Atk_Samurai", twoHanded, 0.85f, true);     // corte amplio a dos manos
        OneShot(sm, loco, "Shoot_Pirata", thrust, 0.5f, true);        // apuntar y disparar la pistola
        OneShot(sm, loco, "Shoot_Samurai", thrust, 0.32f, true);      // lanzamiento seco del kunai
        OneShot(sm, loco, "Hit", FindClip("GetHit"), 0.45f, true);
        OneShot(sm, loco, "Roll", FindClip("RollForward"), 0.6f, true);
        OneShot(sm, loco, "Death", FindClip("Death"), 1.6f, false);

        // Capa "UpperBody": los mismos ataques, pero solo sobre torso y brazos (AvatarMask), para que
        // las piernas sigan con la locomocion de la capa base. Nace con peso 0: CartoPlayer lo sube
        // mientras ataca; los enemigos no la usan y se animan a cuerpo entero como siempre.
        ctrl.AddLayer("UpperBody");
        AnimatorControllerLayer[] layers = ctrl.layers;
        layers[1].avatarMask = BuildUpperBodyMask();
        layers[1].defaultWeight = 0f;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
        ctrl.layers = layers;
        AnimatorStateMachine up = ctrl.layers[1].stateMachine;
        AnimatorState rest = up.AddState("Empty");   // sin clip: deja pasar la capa base
        up.defaultState = rest;
        OneShot(up, rest, "Attack1H", oneHanded, 0.7f, true);
        OneShot(up, rest, "Attack2H", twoHanded, 0.9f, true);
        OneShot(up, rest, "Shoot", thrust, 0.5f, true);
        OneShot(up, rest, "Atk_Cartografo", thrust, 0.5f, true);
        OneShot(up, rest, "Atk_Pirata", oneHanded, 0.6f, true);
        OneShot(up, rest, "Atk_Samurai", twoHanded, 0.85f, true);
        OneShot(up, rest, "Shoot_Pirata", thrust, 0.5f, true);
        OneShot(up, rest, "Shoot_Samurai", thrust, 0.32f, true);

        EditorUtility.SetDirty(ctrl);
        return ctrl;
    }

    /// <summary>Mascara de torso, cabeza y brazos (sin piernas ni raiz) para la capa "UpperBody".</summary>
    static AvatarMask BuildUpperBodyMask()
    {
        string path = Gen + "/CartoUpperBody.mask";
        AssetDatabase.DeleteAsset(path);
        AvatarMask mask = new AvatarMask();
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
        AssetDatabase.CreateAsset(mask, path);
        return mask;
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

    // Los muros de los limites: gruesos y altos para que nada los cruce (ni rodando ni empujado).
    const float LimitThickness = 2f;
    const float LimitHeight = 14f;
    const float LimitSkin = 0.4f;   // hueco entre el limite que alcanza el cartografo y la cara del muro

    /// <summary>
    /// Limite circular del area jugable (Nexo): anillo de muros invisibles alrededor de 'center' y un
    /// CartoBounds que avisa en pantalla al acercarse. 'radius' = hasta donde llega el cartografo.
    /// </summary>
    static void LimitCircle(Transform parent, Vector3 center, float radius, string message)
    {
        GameObject root = new GameObject("Limites");
        root.transform.SetParent(parent, false);
        const int segments = 32;
        float wallR = radius + LimitSkin + LimitThickness * 0.5f;
        float width = 2f * (wallR + LimitThickness) * Mathf.Tan(Mathf.PI / segments) + 0.5f;   // se solapan
        for (int i = 0; i < segments; i++)
        {
            float a = i * 360f / segments;
            Vector3 p = center + Quaternion.Euler(0f, a, 0f) * new Vector3(0f, LimitHeight * 0.5f - 1f, wallR);
            Wall(root.transform, p, new Vector3(width, LimitHeight, LimitThickness), a);
        }
        CartoBounds b = root.AddComponent<CartoBounds>();
        b.circular = true;
        b.center = center;
        b.radius = radius;
        b.message = message;
    }

    /// <summary>
    /// Limite rectangular del area jugable (eras): cuatro muros invisibles y un CartoBounds con el
    /// aviso. 'halfX' y 'halfZ' = hasta donde llega el cartografo desde 'center'.
    /// </summary>
    static void LimitRect(Transform parent, Vector3 center, float halfX, float halfZ, string message)
    {
        GameObject root = new GameObject("Limites");
        root.transform.SetParent(parent, false);
        float x = halfX + LimitSkin + LimitThickness * 0.5f;
        float z = halfZ + LimitSkin + LimitThickness * 0.5f;
        float y = LimitHeight * 0.5f - 1f;
        float lenX = 2f * (x + LimitThickness);
        float lenZ = 2f * (z + LimitThickness);
        Wall(root.transform, center + new Vector3(0f, y, z), new Vector3(lenX, LimitHeight, LimitThickness));
        Wall(root.transform, center + new Vector3(0f, y, -z), new Vector3(lenX, LimitHeight, LimitThickness));
        Wall(root.transform, center + new Vector3(x, y, 0f), new Vector3(LimitThickness, LimitHeight, lenZ));
        Wall(root.transform, center + new Vector3(-x, y, 0f), new Vector3(LimitThickness, LimitHeight, lenZ));
        CartoBounds b = root.AddComponent<CartoBounds>();
        b.circular = false;
        b.center = center;
        b.halfSize = new Vector2(halfX, halfZ);
        b.message = message;
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

    /// <summary>Luz de relleno: direccional sin sombras, para aclarar el lado que el sol deja oscuro.</summary>
    static void FillLight(Vector3 euler, Color color, float intensity)
    {
        GameObject go = new GameObject("LuzDeRelleno");
        go.transform.rotation = Quaternion.Euler(euler);
        Light l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.color = color;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
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
        CartoMount.ApplyOutfit(root.transform, player.outfitPieces);   // armadura de placas, sin casco
        root.AddComponent<CartoBreath>();

        // Aura del cartografo: motas de luz cian y oro que suben desde sus pies y un resplandor frio.
        GameObject aura = new GameObject("Aura");
        aura.transform.SetParent(root.transform, false);
        Particles(aura.transform, "Motas", new Vector3(0f, 0.25f, 0f), new Vector3(0.9f, 0.3f, 0.9f),
            CartoArt.Cyan, CartoArt.Gold, 8f, 1.9f, 0.13f, new Vector3(0f, 0.8f, 0f), 0.12f);
        PointLight(aura.transform, new Vector3(0f, 1.3f, 0.3f), Color.Lerp(CartoArt.Cyan, Color.white, 0.35f), 4.5f, 0.9f);
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
        Prim(PrimitiveType.Cube, "Umbral", root.transform, new Vector3(0f, 0.08f, 0f), new Vector3(5.6f, 0.16f, 2.2f), frame, false);
        Prim(PrimitiveType.Cube, "PilarIzq", root.transform, new Vector3(-1.8f, 2.1f, 0f), new Vector3(0.7f, 4.2f, 0.7f), frame);
        Prim(PrimitiveType.Cube, "PilarDer", root.transform, new Vector3(1.8f, 2.1f, 0f), new Vector3(0.7f, 4.2f, 0.7f), frame);
        Prim(PrimitiveType.Cube, "Dintel", root.transform, new Vector3(0f, 4.45f, 0f), new Vector3(5f, 0.55f, 0.9f), frame);
        Prim(PrimitiveType.Cube, "Remate", root.transform, new Vector3(0f, 4.9f, 0f), new Vector3(3.2f, 0.35f, 0.7f), frame, false);
        // Runas de luz en las dos caras de cada pilar y sobre el dintel
        for (int side = -1; side <= 1; side += 2)
        {
            Prim(PrimitiveType.Cube, "Runa", root.transform, new Vector3(side * 1.8f, 2.1f, 0.36f), new Vector3(0.16f, 3.2f, 0.05f), glow, false);
            Prim(PrimitiveType.Cube, "Runa", root.transform, new Vector3(side * 1.8f, 2.1f, -0.36f), new Vector3(0.16f, 3.2f, 0.05f), glow, false);
            Prim(PrimitiveType.Cube, "Gema", root.transform, new Vector3(side * 2.1f, 5f, 0f), new Vector3(0.4f, 0.4f, 0.4f), glow, false, 45f);
        }
        GameObject g = Prim(PrimitiveType.Sphere, "Vortice", root.transform, new Vector3(0f, 2.1f, 0f), new Vector3(2.9f, 3.9f, 0.25f), glow, false);
        CartoSpin gs = g.AddComponent<CartoSpin>();
        gs.degreesPerSecond = Vector3.zero;
        gs.bobHeight = 0.12f;
        // Nucleo mas claro en el centro del vortice
        Color coreColor = Color.Lerp(lightColor, Color.white, 0.6f);
        GameObject core = Prim(PrimitiveType.Sphere, "Nucleo", root.transform, new Vector3(0f, 2.1f, 0f), new Vector3(1.6f, 2.5f, 0.32f),
            Mat("Nucleo_" + scene, coreColor, 2.2f), false);
        CartoSpin cs = core.AddComponent<CartoSpin>();
        cs.degreesPerSecond = Vector3.zero;
        cs.bobHeight = 0.07f;
        cs.bobSpeed = 2.3f;
        PointLight(root.transform, new Vector3(0f, 2.2f, 1.2f), lightColor, 11f, 2.6f).renderMode = LightRenderMode.ForcePixel;
        PointLight(root.transform, new Vector3(0f, 2.2f, -1.2f), lightColor, 11f, 2.6f);
        Particles(root.transform, "Chispas", new Vector3(0f, 2.2f, 0f), new Vector3(2.8f, 3.8f, 0.8f),
            lightColor, coreColor, 12f, 1.7f, 0.16f, new Vector3(0f, 0.55f, 0f), 0.35f);
        CartoPortal p = root.AddComponent<CartoPortal>();
        p.targetScene = scene;
        p.label = label;
    }

    /// <summary>
    /// Punto de control: dos postes a los lados del camino que se encienden al cruzar entre ellos.
    /// El cartografo reaparece en el centro (pos) mirando hacia +Z, donde espera el jefe.
    /// </summary>
    static void Checkpoint(Transform parent, Vector3 pos)
    {
        GameObject root = new GameObject("PuntoDeControl");
        root.transform.SetParent(parent, false);
        root.transform.position = pos;
        Material stone = Mat("Piedra", new Color(0.42f, 0.4f, 0.45f));
        Material off = Mat("ControlApagado", new Color(0.2f, 0.22f, 0.24f));
        Color glow = new Color(0.4f, 1f, 0.65f);
        Material on = Mat("BrilloControl", glow, 1.8f);

        GameObject offGroup = new GameObject("Apagado");
        offGroup.transform.SetParent(root.transform, false);
        GameObject onGroup = new GameObject("Encendido");
        onGroup.transform.SetParent(root.transform, false);
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * 4.2f;
            Prim(PrimitiveType.Cylinder, "Base", root.transform, new Vector3(x, 0.12f, 0f), new Vector3(1.3f, 0.12f, 1.3f), stone, false);
            Prim(PrimitiveType.Cube, "Poste", root.transform, new Vector3(x, 1.25f, 0f), new Vector3(0.32f, 2.1f, 0.32f), stone, false);
            Prim(PrimitiveType.Cube, "Faro", offGroup.transform, new Vector3(x, 2.6f, 0f), new Vector3(0.6f, 0.6f, 0.6f), off, false, 45f);
            GameObject lit = Prim(PrimitiveType.Cube, "Faro", onGroup.transform, new Vector3(x, 2.6f, 0f), new Vector3(0.6f, 0.6f, 0.6f), on, false, 45f);
            lit.AddComponent<CartoSpin>().bobHeight = 0.08f;
            PointLight(onGroup.transform, new Vector3(x, 2.7f, 0f), glow, 9f, 2.4f);
        }
        onGroup.SetActive(false);

        CartoCheckpoint cp = root.AddComponent<CartoCheckpoint>();
        cp.activeVisual = onGroup;
        cp.inactiveVisual = offGroup;
    }

    static CartoShot Shot(Vector3 from, Vector3 to, Vector3 lookAt, float seconds, string text)
    {
        CartoShot s = new CartoShot();
        s.from = from;
        s.to = to;
        s.lookAt = lookAt;
        s.seconds = seconds;
        s.text = text;
        return s;
    }

    /// <summary>Tarjeta de cinematica: pantalla en negro con una frase en el centro (y glitch si se pide).</summary>
    static CartoShot Card(float seconds, string text, float glitch)
    {
        CartoShot s = new CartoShot();
        s.card = true;
        s.seconds = seconds;
        s.text = text;
        s.glitch = glitch;
        return s;
    }

    /// <summary>
    /// Cinematica de historia de la escena: planos de camara con texto narrativo, guardados en
    /// "_CartoGame". Hay que llamarlo despues de SetupCommon.
    /// </summary>
    static void AddCinematic(params CartoShot[] shots)
    {
        GameObject gm = GameObject.Find("_CartoGame");
        if (gm == null) return;
        gm.AddComponent<CartoCinematic>().shots = shots;
    }

    static void Torii(Transform parent, Vector3 pos, float halfWidth, Material red, Material dark, float yaw = 0f)
    {
        GameObject root = new GameObject("Torii");
        root.transform.SetParent(parent, false);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
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

        // --- Luz: principal calida + relleno frio desde el lado contrario, ambiente oscuro (contraste)
        //     y niebla suave del color del vacio ---
        Color voidColor = new Color(0.075f, 0.055f, 0.19f);
        Sun(new Vector3(44f, -38f, 0f), new Color(1f, 0.86f, 0.66f), 1.2f);
        FillLight(new Vector3(26f, 142f, 0f), new Color(0.38f, 0.58f, 1f), 0.5f);
        Ambient(new Color(0.24f, 0.22f, 0.44f), new Color(0.17f, 0.15f, 0.32f), new Color(0.07f, 0.06f, 0.15f), voidColor, 30f, 165f);

        Transform env = new GameObject("Entorno").transform;
        Material stoneDark = Mat("PiedraNexo", new Color(0.25f, 0.24f, 0.33f));
        Material stoneMid = Mat("PiedraNexoMedia", new Color(0.33f, 0.32f, 0.42f));
        Material stone = Mat("Piedra", new Color(0.42f, 0.4f, 0.45f));
        Material stoneLight = Mat("PiedraClara", new Color(0.6f, 0.58f, 0.66f));
        Material cyan = Mat("BrilloCian", new Color(0.3f, 0.9f, 1f), 1.6f);
        Material gold = Mat("BrilloOro", CartoArt.Gold, 1.5f);
        Material magenta = Mat("BrilloMagenta", CartoArt.Magenta, 1.8f);
        Material lineCyan = Mat("LineaCian", CartoArt.Cyan, 0.9f);
        Material lineGold = Mat("LineaOro", CartoArt.Gold, 0.8f);
        Material lineMagenta = Mat("LineaMagenta", CartoArt.Magenta, 0.9f);
        Material wood = Mat("Madera", new Color(0.42f, 0.28f, 0.17f));
        Material red = Mat("RojoTorii", new Color(0.75f, 0.12f, 0.1f));
        Material dark = Mat("Oscuro", new Color(0.12f, 0.1f, 0.12f));
        Material rift = Mat("Grieta", new Color(0.35f, 0.05f, 0.6f), 1.4f);
        Material trunk = Mat("Tronco", new Color(0.3f, 0.2f, 0.15f));
        Material sakura = Mat("Sakura", new Color(1f, 0.68f, 0.8f), 0.15f);
        Material grass = Mat("Hierba", new Color(0.34f, 0.46f, 0.28f));
        string pp = "3D Pirates";

        // =================================================================
        //  Suelo en anillos, de fuera hacia dentro. Entre anillo y anillo asoma una linea de luz.
        //    borde (pilares)  ->  anillo medio (portales y altar)  ->  plaza central (rosa de los vientos)
        // =================================================================
        Prim(PrimitiveType.Cylinder, "Plataforma", env, new Vector3(0f, -0.5f, 0f), new Vector3(46f, 0.5f, 46f), stoneDark);
        Prim(PrimitiveType.Cylinder, "RocaInferior1", env, new Vector3(0f, -2.4f, 0f), new Vector3(37f, 1.4f, 37f), stoneDark, false);
        Prim(PrimitiveType.Cylinder, "RocaInferior2", env, new Vector3(0f, -5.2f, 0f), new Vector3(24f, 1.4f, 24f), stoneDark, false);
        Prim(PrimitiveType.Cylinder, "RocaInferior3", env, new Vector3(0f, -8f, 0f), new Vector3(11f, 1.4f, 11f), stoneDark, false);
        Disc(env, "LineaExterior", Vector3.zero, 43.4f, 0.012f, lineCyan);
        Disc(env, "AnilloExterior", Vector3.zero, 42.8f, 0.024f, stone, true);
        Disc(env, "LineaMedia", Vector3.zero, 33.6f, 0.036f, lineGold);
        Disc(env, "AnilloMedio", Vector3.zero, 33f, 0.048f, stoneMid, true);
        Disc(env, "LineaPlaza", Vector3.zero, 16.8f, 0.06f, lineCyan);
        Disc(env, "Plaza", Vector3.zero, 16f, 0.072f, stoneLight, true);

        // Rosa de los vientos: el Nexo es el mapa del que parten todas las eras.
        FloorStrip(env, "RosaNorteSur", Vector3.zero, 0.16f, 15f, 0f, 0.084f, lineGold);
        FloorStrip(env, "RosaEsteOeste", Vector3.zero, 0.16f, 15f, 90f, 0.084f, lineGold);
        FloorStrip(env, "RosaDiagonal", Vector3.zero, 0.1f, 9.5f, 45f, 0.084f, lineCyan);
        FloorStrip(env, "RosaDiagonal", Vector3.zero, 0.1f, 9.5f, 135f, 0.084f, lineCyan);
        for (int i = 0; i < 16; i++)
        {
            float a = i * 22.5f;
            Vector3 p = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 7.2f);
            FloorStrip(env, "MarcaRosa", p, 0.12f, i % 4 == 0 ? 1.2f : 0.6f, a, 0.084f, i % 4 == 0 ? lineGold : lineCyan);
        }
        Disc(env, "LineaMedallon", Vector3.zero, 4.6f, 0.096f, lineCyan);
        Disc(env, "Medallon", Vector3.zero, 4.1f, 0.108f, stoneDark);

        // Corazon del Nexo: cristal que flota sobre el medallon e ilumina la plaza.
        GameObject heart = new GameObject("CorazonDelNexo");
        heart.transform.SetParent(env, false);
        heart.transform.position = new Vector3(0f, 5.4f, 0f);
        CartoSpin heartSpin = heart.AddComponent<CartoSpin>();
        heartSpin.degreesPerSecond = new Vector3(0f, 18f, 0f);
        heartSpin.bobHeight = 0.25f;
        heartSpin.bobSpeed = 0.9f;
        GameObject heartCrystal = Place("Crystal_35", "BUILT_IN", heart.transform.position, 0f, 1f, heart.transform, false, false);
        FitHeight(heartCrystal, 2.6f, 5.4f);
        PointLight(heart.transform, new Vector3(0f, 1.2f, 0f), CartoArt.Cyan, 18f, 2.4f).renderMode = LightRenderMode.ForcePixel;
        Particles(env, "PolvoDelCorazon", new Vector3(0f, 3f, 0f), new Vector3(1.8f, 4.5f, 1.8f),
            CartoArt.Cyan, Color.white, 9f, 2.4f, 0.14f, new Vector3(0f, 0.9f, 0f), 0.1f);

        // =================================================================
        //  Caminos: del punto de llegada a la plaza y de la plaza a cada portal, con el color de su destino
        // =================================================================
        Walkway(env, new Vector3(0f, 0f, -14.2f), 12.6f, 0f, stoneLight, lineCyan);        // sur: llegada
        Walkway(env, new Vector3(-11.5f, 0f, 0f), 7.6f, 90f, stoneLight, lineGold);        // oeste: Era Pirata
        Walkway(env, new Vector3(11.5f, 0f, 0f), 7.6f, 90f, stoneLight, lineMagenta);      // este: Era Samurai

        // Punto de llegada del cartografo
        Vector3 spawn = new Vector3(0f, 0.2f, -13f);
        Disc(env, "LineaLlegada", new Vector3(0f, 0f, -13f), 5.2f, 0.096f, lineCyan);
        Disc(env, "Llegada", new Vector3(0f, 0f, -13f), 4.7f, 0.108f, stoneLight, true);
        HubPillar(env, new Vector3(-2.8f, 0f, -19.6f), 0f, 2f, stone, stoneDark, cyan, "Crystal_1", CartoArt.Cyan, true);
        HubPillar(env, new Vector3(2.8f, 0f, -19.6f), 0f, 2f, stone, stoneDark, cyan, "Crystal_1", CartoArt.Cyan, false);

        // =================================================================
        //  Portales: cada uno en su tarima, con su arco de entrada y restos de su era alrededor
        // =================================================================
        Vector3 piratePortal = new Vector3(-16f, 0f, 0f);
        Vector3 samuraiPortal = new Vector3(16f, 0f, 0f);
        Disc(env, "LineaPortalPirata", piratePortal, 7f, 0.1f, lineGold);
        Disc(env, "TarimaPortalPirata", piratePortal, 6.5f, 0.112f, stoneLight, true);
        Disc(env, "LineaPortalSamurai", samuraiPortal, 7f, 0.1f, lineMagenta);
        Disc(env, "TarimaPortalSamurai", samuraiPortal, 6.5f, 0.112f, stoneLight, true);
        Portal(env, piratePortal, 90f, wood, gold, CartoArt.Gold, CartoGameState.ScenePirate, "Viajar a la Era Pirata");
        Portal(env, samuraiPortal, -90f, red, magenta, CartoArt.Magenta, CartoGameState.SceneSamurai, "Viajar a la Era Samurái");

        // Era Pirata (oeste): arco de muelle sobre el camino, mercancia y un canon
        DockArch(env, new Vector3(-10.4f, 0f, 0f), 90f, 2.5f, wood, gold);
        Place("02_Market_Barrel_Stack_3", pp, new Vector3(-14.2f, 0f, -6.2f), 30f, 1f, env);
        Place("02_Market_Crate_1", pp, new Vector3(-16.4f, 0f, -5.4f), 10f, 1f, env);
        Place("04_CannonShop_Cannon", pp, new Vector3(-14.6f, 0f, 6.4f), 70f, 1f, env);
        Place("04_CannonShop_CannonBall_Stack", pp, new Vector3(-16.6f, 0f, 5.6f), 0f, 1f, env);
        Place("02_Market_Barrel_Single_Closed", pp, new Vector3(-12.8f, 0f, 7.6f), 0f, 1f, env);
        Place("07_Tavern_UmbrelaTable_1", pp, new Vector3(-11.4f, 0f, -9.2f), 0f, 1f, env);

        // Era Samurai (este): torii sobre el camino, faroles y cerezos
        Torii(env, new Vector3(10.4f, 0f, 0f), 2.5f, red, dark, 90f);
        Lantern(env, new Vector3(12.9f, 0f, -3.9f), stone, gold, true);
        Lantern(env, new Vector3(12.9f, 0f, 3.9f), stone, gold, true);
        Sakura(env, new Vector3(14.4f, 0f, -8f), 0.9f, trunk, sakura);
        Sakura(env, new Vector3(14f, 0f, 8.6f), 1f, trunk, sakura);

        // Detras de cada portal flota un pedazo de su era: se ve adonde lleva.
        GameObject shipHolder = new GameObject("FragmentoPirata");
        shipHolder.transform.SetParent(env, false);
        shipHolder.transform.position = new Vector3(-46f, -6f, 3f);
        CartoSpin shipSpin = shipHolder.AddComponent<CartoSpin>();
        shipSpin.degreesPerSecond = Vector3.zero;
        shipSpin.bobHeight = 0.6f;
        shipSpin.bobSpeed = 0.4f;
        GameObject ship = Place("99_Ship_L1", pp, shipHolder.transform.position, 80f, 1.6f, shipHolder.transform, false, false);
        ship.transform.rotation = Quaternion.Euler(0f, 0f, 7f) * ship.transform.rotation;
        PointLight(shipHolder.transform, new Vector3(6f, 9f, 0f), CartoArt.Gold, 30f, 2.2f);

        GameObject isle = new GameObject("FragmentoSamurai");
        isle.transform.SetParent(env, false);
        isle.transform.position = new Vector3(42f, -3f, 2f);
        Prim(PrimitiveType.Cylinder, "Roca", isle.transform, new Vector3(0f, -1.2f, 0f), new Vector3(16f, 1.2f, 16f), stoneDark, false);
        Prim(PrimitiveType.Cylinder, "RocaInferior", isle.transform, new Vector3(0f, -3.5f, 0f), new Vector3(9f, 1.2f, 9f), stoneDark, false);
        Prim(PrimitiveType.Cylinder, "Hierba", isle.transform, new Vector3(0f, 0.04f, 0f), new Vector3(15.2f, 0.06f, 15.2f), grass, false);
        Torii(isle.transform, isle.transform.position + new Vector3(-2f, 0f, 0f), 2.6f, red, dark, 90f);
        Sakura(isle.transform, isle.transform.position + new Vector3(2.5f, 0f, -4f), 1.1f, trunk, sakura);
        Sakura(isle.transform, isle.transform.position + new Vector3(3f, 0f, 4.2f), 0.9f, trunk, sakura);
        Lantern(isle.transform, isle.transform.position + new Vector3(-4.5f, 0f, 3.4f), stone, gold, false);
        PointLight(isle.transform, new Vector3(0f, 6f, 0f), CartoArt.Magenta, 26f, 2.2f);
        CartoSpin isleSpin = isle.AddComponent<CartoSpin>();
        isleSpin.degreesPerSecond = Vector3.zero;
        isleSpin.bobHeight = 0.5f;
        isleSpin.bobSpeed = 0.35f;

        // =================================================================
        //  Altar de mejoras (norte): tarima propia, arco de monolitos de cristal y los dos pedestales
        // =================================================================
        Vector3 daisCenter = new Vector3(0f, 0f, 12.4f);
        const float daisTop = 0.112f;
        Disc(env, "LineaAltar", daisCenter, 11.5f, 0.1f, lineCyan);
        Disc(env, "TarimaAltar", daisCenter, 11f, daisTop, stoneLight, true);

        GameObject altar = new GameObject("AltarDeMejoras");
        altar.transform.SetParent(env, false);
        altar.transform.position = new Vector3(0f, 0f, 11f);
        Prim(PrimitiveType.Cube, "Base", altar.transform, new Vector3(0f, 0.3f, 0f), new Vector3(4.2f, 0.6f, 3f), stoneLight);
        Prim(PrimitiveType.Cube, "Mesa", altar.transform, new Vector3(0f, 0.9f, 0f), new Vector3(2.6f, 0.6f, 1.7f), stone);
        Prim(PrimitiveType.Cube, "Filete", altar.transform, new Vector3(0f, 0.62f, 0f), new Vector3(4.24f, 0.06f, 3.04f), cyan, false);
        GameObject altarCrystalHolder = new GameObject("CristalAltar");
        altarCrystalHolder.transform.SetParent(altar.transform, false);
        altarCrystalHolder.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        CartoSpin aspin = altarCrystalHolder.AddComponent<CartoSpin>();
        aspin.bobHeight = 0.15f;
        GameObject ac = Place("Crystal_35", "BUILT_IN", new Vector3(0f, 1.5f, 11f), 0f, 1f, altarCrystalHolder.transform, false, false);
        FitHeight(ac, 1.5f, 1.5f);
        PointLight(altar.transform, new Vector3(0f, 2.6f, 0f), CartoArt.Cyan, 12f, 3f).renderMode = LightRenderMode.ForcePixel;
        Particles(altar.transform, "Destellos", new Vector3(0f, 2.1f, 0f), new Vector3(1.6f, 1.4f, 1.2f),
            CartoArt.Cyan, CartoArt.Gold, 7f, 1.6f, 0.13f, new Vector3(0f, 0.5f, 0f), 0.15f);
        altar.AddComponent<CartoShop>();

        // Arco de monolitos detras del altar: el mas alto en el centro
        string[] arcCrystals = { "Crystal_40", "Crystal_46", "Crystal_35", "Crystal_50", "Crystal_2" };
        for (int i = 0; i < 5; i++)
        {
            float a = (-60f + i * 30f) * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Sin(a) * 5.2f, 0f, 11f + Mathf.Cos(a) * 5.2f);
            Prim(PrimitiveType.Cube, "Plinto", env, new Vector3(p.x, 0.36f, p.z), new Vector3(1.3f, 0.5f, 1.3f), stone, true, -60f + i * 30f);
            GameObject mono = Place(arcCrystals[i], "BUILT_IN", new Vector3(p.x, 0.6f, p.z), i * 47f, 1f, env, false, false);
            FitHeight(mono, 2.6f + 1.8f * Mathf.Cos(a), 0.6f);
        }
        PointLight(env, new Vector3(-3f, 3.2f, 15.6f), CartoArt.Cyan, 11f, 2f);
        PointLight(env, new Vector3(3f, 3.2f, 15.6f), CartoArt.Cyan, 11f, 2f);

        // Guardiana del altar (decorativa)
        GameObject npc = new GameObject("GuardianaDelAltar");
        npc.transform.SetParent(env, false);
        npc.transform.position = new Vector3(3.6f, daisTop, 10.4f);
        npc.transform.rotation = Quaternion.Euler(0f, 200f, 0f);
        CharacterModel("samurai_woman_v2", "Samurai", npc.transform, 1f, magenta);
        npc.AddComponent<CartoBreath>();
        CapsuleCollider npcCol = npc.AddComponent<CapsuleCollider>();
        npcCol.height = 1.8f;
        npcCol.radius = 0.4f;
        npcCol.center = new Vector3(0f, 0.9f, 0f);

        // Pedestales de los cristales recuperados: el de cada era en el lado de su portal
        HubPedestal(env, new Vector3(-6f, 0f, 10f), 0, "Crystal_1", CartoArt.Cyan, stone, lineGold);
        HubPedestal(env, new Vector3(6f, 0f, 10f), 1, "Crystal_5", CartoArt.Magenta, stone, lineMagenta);

        // =================================================================
        //  Borde: anillo de pilares con cristal (oro al oeste, magenta al este, cian al norte y al sur)
        //  y un pretil bajo en ruinas que marca el final de la plataforma
        // =================================================================
        string[] ringCrystals = { "Crystal_46", "Crystal_50", "Crystal_40", "Crystal_35", "Crystal_2", "Crystal_1", "Crystal_5" };
        for (int i = 0; i < 12; i++)
        {
            float a = 15f + i * 30f;
            Vector3 p = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 19.5f);
            bool east = a > 45f && a < 135f;
            bool west = a > 225f && a < 315f;
            Color c = east ? CartoArt.Magenta : (west ? CartoArt.Gold : CartoArt.Cyan);
            Material band = east ? magenta : (west ? gold : cyan);
            float h = 3.4f + 2.6f * (p.z + 19.5f) / 39f;   // mas altos hacia el altar
            HubPillar(env, p, a, h, stone, stoneDark, band, ringCrystals[i % ringCrystals.Length], c, i % 2 == 0);
        }
        for (int i = 0; i < 40; i++)
        {
            float h = Rand(0.55f, 1.35f);
            float jitter = Rand(-3f, 3f);
            if (rng.NextDouble() < 0.15) continue;   // tramos caidos
            float a = i * 9f;
            Vector3 p = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, h * 0.5f, 22.1f);
            Prim(PrimitiveType.Cube, "Pretil", env, p, new Vector3(3.1f, h, 0.7f), stone, true, a + jitter);
        }
        LimitCircle(env, Vector3.zero, 22.4f, "Borde del Nexo: más allá solo queda el vacío");

        // =================================================================
        //  Grieta temporal sobre el altar (desaparece al reunir todos los cristales)
        // =================================================================
        GameObject riftGo = new GameObject("GrietaTemporal");
        riftGo.transform.SetParent(env, false);
        riftGo.transform.position = new Vector3(0f, 17f, 8f);
        CartoSpin riftSpin = riftGo.AddComponent<CartoSpin>();
        riftSpin.degreesPerSecond = new Vector3(0f, 14f, 0f);
        riftSpin.bobHeight = 0.6f;
        Prim(PrimitiveType.Sphere, "Nucleo", riftGo.transform, Vector3.zero, new Vector3(7f, 7f, 7f), rift, false);
        for (int i = 0; i < 9; i++)
        {
            // Esquirlas que giran alrededor, cada una con su inclinacion
            float a = i * 40f;
            Vector3 p = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, Rand(-1.6f, 1.6f), Rand(5.2f, 6.6f));
            GameObject shard = Prim(PrimitiveType.Cube, "Esquirla", riftGo.transform, p, new Vector3(0.45f, Rand(1.4f, 2.8f), 0.45f),
                i % 3 == 0 ? magenta : rift, false, a);
            shard.transform.localRotation = Quaternion.Euler(Rand(-35f, 35f), a, Rand(-35f, 35f));
        }
        PointLight(riftGo.transform, Vector3.zero, new Color(0.7f, 0.3f, 1f), 40f, 2.5f);
        Particles(riftGo.transform, "Chispas", Vector3.zero, new Vector3(11f, 9f, 11f),
            CartoArt.Magenta, new Color(0.6f, 0.35f, 1f), 16f, 3.6f, 0.4f, new Vector3(0f, -0.35f, 0f), 0.6f);
        CartoCrystalDisplay rd = env.gameObject.AddComponent<CartoCrystalDisplay>();
        rd.era = -1;
        rd.invert = true;
        rd.target = riftGo;

        // =================================================================
        //  Ambiente: motas de tiempo sobre toda la plataforma y restos de eras flotando en el vacio
        // =================================================================
        Particles(env, "MotasDelTiempo", new Vector3(0f, 6f, 0f), new Vector3(52f, 14f, 52f),
            CartoArt.Cyan, CartoArt.Parchment, 26f, 9f, 0.17f, new Vector3(0f, 0.12f, 0f), 0.25f);

        string[] crystalNames = { "Crystal_46", "Crystal_50", "Crystal_40", "Crystal_35", "Crystal_2" };
        string[] debris = { "01_House_5", "01_House_6", "04_CannonShop_Cannon", "02_Market_Barrel_Stack_3", "01_House_5" };
        for (int i = 0; i < 18; i++)
        {
            float a = i * 20f + Rand(-6f, 6f);
            float r = Rand(36f, 58f);
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
                if (i % 2 == 0) PointLight(holder.transform, Vector3.up * 2f, i % 4 == 0 ? CartoArt.Cyan : CartoArt.Magenta, 18f, 2f);
            }
        }

        SetupCommon(CartoGame.Zone.Hub, "Nexo del Tiempo", "Cruza un portal, recupera los cristales y mejora tus armas en el altar",
            spawn, 0f, false, voidColor);

        // Intro de historia (unos 58 s, se salta con Enter): el desastre temporal, las eras rotas,
        // los cristales y la mision, con tarjetas en negro, glitches y camara lenta hacia los portales.
        CartoShot wide = Shot(new Vector3(0f, 36f, -66f), new Vector3(0f, 25f, -47f), new Vector3(0f, 5f, 4f), 8f,
            "Cada era en su lugar. Cada camino, trazado por los cartógrafos desde el Nexo del Tiempo.");
        wide.fov = 62f;
        wide.fovEnd = 54f;

        CartoShot tear = Shot(new Vector3(-14f, 6f, -10f), new Vector3(-9f, 9f, -5f), new Vector3(0f, 17f, 8f), 7.5f,
            "Hasta que algo lo desgarró. Una grieta devoró los mapas... y el tiempo empezó a romperse.");
        tear.glitch = 1.4f;

        CartoShot toPirate = Shot(new Vector3(-3.5f, 2.6f, 0.6f), new Vector3(-10.1f, 2.2f, 0.2f), new Vector3(-16f, 2.3f, 0f), 7.5f,
            "Un cristal del tiempo cayó en la Era Pirata, bajo la bandera del Capitán Marea Negra...");
        toPirate.fov = 56f;
        toPirate.fovEnd = 42f;
        toPirate.worldSpeed = 0.5f;

        CartoShot toSamurai = Shot(new Vector3(3.5f, 2.6f, -0.6f), new Vector3(10.1f, 2.2f, -0.2f), new Vector3(16f, 2.3f, 0f), 7.5f,
            "...y otro en la Era Samurái, en el dojo de Akudaikan, Señor de la Guerra.");
        toSamurai.fov = 56f;
        toSamurai.fovEnd = 42f;
        toSamurai.worldSpeed = 0.5f;

        CartoShot empty = Shot(new Vector3(-6.5f, 2.4f, 3.5f), new Vector3(6.5f, 2.6f, 3.5f), new Vector3(0f, 1.6f, 11f), 6.5f,
            "Sin ellos, el Nexo se apaga. Y cuando se apague, no quedará ningún ayer al que volver.");

        // Cierre: la camara sube despacio desde el suelo hasta la cara del cartografo.
        CartoShot hero = Shot(new Vector3(1.4f, 0.45f, -10.3f), new Vector3(0.7f, 1.75f, -10.8f), new Vector3(0f, 0.7f, -13f), 9f,
            "El último Cartógrafo del Tiempo. Cruza los portales, recupera los cristales y devuelve cada era a su lugar.");
        hero.moveLook = true;
        hero.lookAtEnd = new Vector3(0f, 1.55f, -13f);
        hero.fov = 44f;
        hero.fovEnd = 36f;
        hero.title = "EL CARTÓGRAFO DEL TIEMPO";

        AddCinematic(
            Card(4.5f, "Dicen que el tiempo es un mapa.", 0f),
            wide,
            tear,
            Card(4f, "Las eras se mezclaron.", 0.9f),
            toPirate,
            toSamurai,
            empty,
            Card(3.5f, "Solo queda uno.", 0.7f),
            hero);
        SaveScene(scene, HubPath);
    }

    /// <summary>Disco de suelo: cilindro plano de diametro 'diameter' con la cara de arriba a la altura 'top'.</summary>
    static GameObject Disc(Transform parent, string name, Vector3 center, float diameter, float top, Material mat, bool collider = false)
    {
        const float half = 0.25f;
        return Prim(PrimitiveType.Cylinder, name, parent, new Vector3(center.x, top - half, center.z),
            new Vector3(diameter, half, diameter), mat, collider);
    }

    /// <summary>Franja plana sobre el suelo: 'length' a lo largo de su eje Z (girado 'yaw'), cara de arriba en 'top'.</summary>
    static GameObject FloorStrip(Transform parent, string name, Vector3 center, float width, float length, float yaw, float top,
        Material mat, bool collider = false)
    {
        return Prim(PrimitiveType.Cube, name, parent, new Vector3(center.x, top - 0.01f, center.z),
            new Vector3(width, 0.02f, length), mat, collider, yaw);
    }

    /// <summary>Camino del Nexo: losa clara con un filete de luz a cada lado, del color de su destino.</summary>
    static void Walkway(Transform parent, Vector3 center, float length, float yaw, Material slab, Material edge)
    {
        const float width = 3.2f;
        FloorStrip(parent, "Camino", center, width, length, yaw, 0.084f, slab, true);
        Vector3 side = Quaternion.Euler(0f, yaw, 0f) * Vector3.right * (width * 0.5f + 0.08f);
        FloorStrip(parent, "FileteCamino", center + side, 0.16f, length, yaw, 0.096f, edge);
        FloorStrip(parent, "FileteCamino", center - side, 0.16f, length, yaw, 0.096f, edge);
    }

    /// <summary>Pilar del borde del Nexo: base, fuste con una franja de luz, capitel y un cristal flotando encima.</summary>
    static void HubPillar(Transform parent, Vector3 pos, float yaw, float height, Material stone, Material trim, Material band,
        string crystalName, Color color, bool light)
    {
        GameObject root = new GameObject("Pilar");
        root.transform.SetParent(parent, false);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Prim(PrimitiveType.Cube, "Base", root.transform, new Vector3(0f, 0.25f, 0f), new Vector3(1.5f, 0.5f, 1.5f), trim);
        Prim(PrimitiveType.Cube, "Fuste", root.transform, new Vector3(0f, 0.5f + height * 0.5f, 0f), new Vector3(0.95f, height, 0.95f), stone);
        Prim(PrimitiveType.Cube, "Franja", root.transform, new Vector3(0f, 0.5f + height * 0.72f, 0f), new Vector3(1.02f, 0.14f, 1.02f), band, false);
        Prim(PrimitiveType.Cube, "Capitel", root.transform, new Vector3(0f, 0.65f + height, 0f), new Vector3(1.35f, 0.3f, 1.35f), trim, false);

        GameObject holder = new GameObject("Cristal");
        holder.transform.SetParent(root.transform, false);
        holder.transform.localPosition = new Vector3(0f, 0.95f + height, 0f);
        CartoSpin spin = holder.AddComponent<CartoSpin>();
        spin.degreesPerSecond = new Vector3(0f, 20f, 0f);
        spin.bobHeight = 0.08f;
        Vector3 at = holder.transform.position;
        GameObject c = Place(crystalName, "BUILT_IN", at, 0f, 1f, holder.transform, false, false);
        FitHeight(c, 1.4f, at.y);
        if (light) PointLight(holder.transform, new Vector3(0f, 0.8f, 0f), color, 9f, 1.9f);
    }

    /// <summary>Arco de muelle (entrada al portal pirata): dos postes, travesanos y faroles colgando.</summary>
    static void DockArch(Transform parent, Vector3 pos, float yaw, float halfWidth, Material wood, Material glow)
    {
        GameObject root = new GameObject("ArcoDeMuelle");
        root.transform.SetParent(parent, false);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Prim(PrimitiveType.Cylinder, "Poste", root.transform, new Vector3(-halfWidth, 2.5f, 0f), new Vector3(0.45f, 2.5f, 0.45f), wood);
        Prim(PrimitiveType.Cylinder, "Poste", root.transform, new Vector3(halfWidth, 2.5f, 0f), new Vector3(0.45f, 2.5f, 0.45f), wood);
        Prim(PrimitiveType.Cube, "Travesano", root.transform, new Vector3(0f, 4.85f, 0f), new Vector3(halfWidth * 2f + 1.4f, 0.3f, 0.42f), wood, false);
        Prim(PrimitiveType.Cube, "Travesano", root.transform, new Vector3(0f, 4.25f, 0f), new Vector3(halfWidth * 2f + 0.5f, 0.18f, 0.3f), wood, false);
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * halfWidth * 0.5f;
            Prim(PrimitiveType.Cube, "Cuerda", root.transform, new Vector3(x, 3.95f, 0f), new Vector3(0.06f, 0.45f, 0.06f), wood, false);
            Prim(PrimitiveType.Cube, "Farol", root.transform, new Vector3(x, 3.5f, 0f), new Vector3(0.4f, 0.5f, 0.4f), glow, false);
        }
        PointLight(root.transform, new Vector3(0f, 3.4f, 0f), CartoArt.Gold, 8f, 1.7f);
    }

    static void HubPedestal(Transform env, Vector3 pos, int era, string crystalName, Color color, Material stone, Material line)
    {
        GameObject root = new GameObject("PedestalCristal_" + era);
        root.transform.SetParent(env, false);
        root.transform.position = pos;
        Disc(root.transform, "Marca", Vector3.zero, 3f, 0.062f, line);   // el color de su era, aunque falte el cristal
        Prim(PrimitiveType.Cylinder, "Pedestal", root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(1.5f, 0.5f, 1.5f), stone);
        Prim(PrimitiveType.Cylinder, "Plato", root.transform, new Vector3(0f, 1.03f, 0f), new Vector3(1.8f, 0.04f, 1.8f), stone, false);
        GameObject holder = new GameObject("CristalRecuperado");
        holder.transform.SetParent(root.transform, false);
        holder.transform.localPosition = new Vector3(0f, 1.15f, 0f);
        holder.AddComponent<CartoSpin>().bobHeight = 0.1f;
        GameObject c = Place(crystalName, "BUILT_IN", pos + Vector3.up * 1.15f, 0f, 1f, holder.transform, false, false);
        FitHeight(c, 1.7f, pos.y + 1.15f);
        PointLight(holder.transform, new Vector3(0f, 1f, 0f), color, 10f, 3f);
        Particles(holder.transform, "Destellos", new Vector3(0f, 0.9f, 0f), new Vector3(1f, 1.6f, 1f),
            color, Color.white, 6f, 1.5f, 0.12f, new Vector3(0f, 0.45f, 0f), 0.12f);
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
        LimitRect(env, Vector3.zero, 31.1f, 55.1f, "Límite de la era: no puedes ir más lejos");

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

        // Punto de control antes del jefe (entre el segundo y el tercer grupo de piratas)
        Checkpoint(env, new Vector3(0f, 0f, 17f));

        CrystalGoal(env, 0, "Era Pirata", new Vector3(0f, 0f, 48f), boss.GetComponent<CartoHealth>(), "Crystal_46", new Color(0.3f, 0.9f, 1f));
        Portal(env, new Vector3(0f, 0f, -51f), 0f, wood, cyan, new Color(0.4f, 0.9f, 1f), CartoGameState.SceneHub, "Volver al Nexo");

        SetupCommon(CartoGame.Zone.Pirate, "Era Pirata", "Derrota al Capitán Marea Negra y toma el cristal del tiempo",
            new Vector3(0f, 0.1f, -44f), 0f, true, sky);

        // Presentacion de la era: el puerto desde arriba y el tramo final hacia el jefe.
        AddCinematic(
            Shot(new Vector3(0f, 13f, -50f), new Vector3(0f, 8f, -30f), new Vector3(0f, 1.5f, 8f), 5.5f,
                "Era Pirata. El Capitán Marea Negra ha tomado el puerto y esconde un cristal del tiempo."),
            Shot(new Vector3(6f, 4.6f, 22f), new Vector3(3f, 3.6f, 31f), new Vector3(0f, 2.2f, 42f), 5.5f,
                "Ábrete paso entre su tripulación. En el pueblo encontrarás un sable y una pistola de chispa."));
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
        LimitRect(env, Vector3.zero, 31.1f, 55.1f, "Límite de la era: no puedes ir más lejos");

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

        // Punto de control antes del jefe (entre el segundo torii y el ultimo)
        Checkpoint(env, new Vector3(0f, 0f, 16f));

        CrystalGoal(env, 1, "Era Samurái", new Vector3(0f, 0.2f, 48f), boss.GetComponent<CartoHealth>(), "Crystal_50", new Color(1f, 0.4f, 0.8f));
        Portal(env, new Vector3(0f, 0f, -51f), 0f, red, cyan, new Color(0.4f, 0.9f, 1f), CartoGameState.SceneHub, "Volver al Nexo");

        SetupCommon(CartoGame.Zone.Samurai, "Era Samurái", "Derrota a Akudaikan y toma el cristal del tiempo",
            new Vector3(0f, 0.1f, -44f), 0f, false, dusk);

        // Presentacion de la era: el camino de los torii desde arriba y el dojo del jefe.
        AddCinematic(
            Shot(new Vector3(0f, 12f, -54f), new Vector3(0f, 7.4f, -33f), new Vector3(0f, 3f, 2f), 5.5f,
                "Era Samurái. Akudaikan, Señor de la Guerra, se ha apoderado del dojo y de otro cristal del tiempo."),
            Shot(new Vector3(2.5f, 4f, 21.5f), new Vector3(1.5f, 3.6f, 27.5f), new Vector3(0f, 3f, 42f), 5.5f,
                "Ashigaru, ninjas y samuráis guardan el camino de los torii. Busca la katana y los kunai."));
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
