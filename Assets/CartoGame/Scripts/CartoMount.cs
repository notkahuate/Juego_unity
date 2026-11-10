using UnityEngine;

/// <summary>Utilidades para colocar un arma en la mano derecha de un personaje humanoide.</summary>
public static class CartoMount
{
    /// <summary>
    /// Crea (o reutiliza) un punto de agarre en la mano derecha: +Y hacia el lado del pulgar
    /// (direccion de la hoja) y +Z hacia los dedos (direccion del canon).
    /// </summary>
    public static Transform GetGrip(Animator anim, Transform root)
    {
        Transform hand = null, middle = null, thumb = null;
        if (anim != null && anim.isHuman && anim.avatar != null)
        {
            hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            middle = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            thumb = anim.GetBoneTransform(HumanBodyBones.RightThumbProximal);
        }
        if (hand == null)
        {
            hand = FindContains(root, "hand.r");
            if (hand == null) hand = FindContains(root, "wrist_r");
            if (hand == null) hand = FindContains(root, "righthand");
            if (hand == null) hand = FindContains(root, "hand_r");
        }
        if (hand == null) return null;

        Transform existing = hand.Find("CartoGrip");
        if (existing != null) return existing;

        if (middle == null) middle = FindContains(hand, "middle");
        if (thumb == null) thumb = FindContains(hand, "thumb");

        float charScale = Mathf.Max(0.01f, root.lossyScale.y);
        Vector3 f;
        if (middle != null && (middle.position - hand.position).sqrMagnitude > 0.000001f)
            f = (middle.position - hand.position).normalized;
        else if (hand.parent != null && (hand.position - hand.parent.position).sqrMagnitude > 0.000001f)
            f = (hand.position - hand.parent.position).normalized;
        else
            f = root.right;

        Vector3 t = thumb != null ? (thumb.position - hand.position) : root.forward;
        t = Vector3.ProjectOnPlane(t, f);
        if (t.sqrMagnitude < 0.000001f) t = Vector3.ProjectOnPlane(root.forward, f);
        if (t.sqrMagnitude < 0.000001f) t = Vector3.ProjectOnPlane(Vector3.up, f);
        t.Normalize();

        var grip = new GameObject("CartoGrip").transform;
        grip.SetParent(hand, false);
        float palm = middle != null ? Vector3.Distance(middle.position, hand.position) * 0.8f : 0.08f * charScale;
        grip.position = hand.position + f * palm;
        grip.rotation = Quaternion.LookRotation(f, t);
        Vector3 ls = hand.lossyScale;
        grip.localScale = new Vector3(Safe(charScale, ls.x), Safe(charScale, ls.y), Safe(charScale, ls.z));
        return grip;
    }

    static float Safe(float a, float b)
    {
        return Mathf.Abs(b) < 0.00001f ? 1f : a / b;
    }

    public static GameObject Attach(Transform grip, GameObject prefab, Vector3 euler, Vector3 offset, float scale)
    {
        if (grip == null || prefab == null) return null;
        GameObject go = UnityEngine.Object.Instantiate(prefab, grip);
        go.name = prefab.name;
        go.transform.localPosition = offset;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = prefab.transform.localScale * scale;
        Collider[] cols = go.GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++) UnityEngine.Object.Destroy(cols[i]);
        return go;
    }

    public static Transform FindContains(Transform root, string lowerName)
    {
        if (root == null) return null;
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == root) continue;
            if (all[i].name.ToLowerInvariant().Contains(lowerName)) return all[i];
        }
        return null;
    }
}

/// <summary>Materiales y primitivas creados en tiempo de ejecucion.</summary>
public static class CartoUtil
{
    public static Material MakeMaterial(Color color, float emission)
    {
        Shader sh = Shader.Find("Standard");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        var m = new Material(sh);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (emission > 0f && m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * emission);
        }
        return m;
    }

    public static GameObject Primitive(PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        Collider c = go.GetComponent<Collider>();
        if (c != null) UnityEngine.Object.Destroy(c);
        go.transform.position = pos;
        go.transform.localScale = scale;
        Renderer r = go.GetComponent<Renderer>();
        if (r != null && mat != null) r.sharedMaterial = mat;
        return go;
    }
}
