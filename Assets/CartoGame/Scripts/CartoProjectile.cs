using UnityEngine;

/// <summary>Bala de pistola o kunai lanzado.</summary>
public class CartoProjectile : MonoBehaviour
{
    public Vector3 direction = Vector3.forward;
    public float speed = 30f;
    public float damage = 10f;
    public int team = 0;
    public float radius = 0.18f;
    public float life = 3f;

    static Material bulletMat;

    public static CartoProjectile Spawn(Vector3 pos, Vector3 dir, float speed, float damage, int team,
        GameObject modelPrefab, Vector3 modelEuler, Color color)
    {
        var root = new GameObject("Proyectil");
        root.transform.position = pos;
        root.transform.rotation = Quaternion.LookRotation(dir);
        var p = root.AddComponent<CartoProjectile>();
        p.direction = dir.normalized;
        p.speed = speed;
        p.damage = damage;
        p.team = team;

        if (modelPrefab != null)
        {
            GameObject m = Instantiate(modelPrefab, root.transform);
            m.transform.localPosition = Vector3.zero;
            m.transform.localRotation = Quaternion.Euler(modelEuler);
            m.transform.localScale = modelPrefab.transform.localScale * 1.6f;
            Collider[] cols = m.GetComponentsInChildren<Collider>();
            for (int i = 0; i < cols.Length; i++) Destroy(cols[i]);
        }
        else
        {
            if (bulletMat == null) bulletMat = CartoUtil.MakeMaterial(Color.white, 2f);
            GameObject s = CartoUtil.Primitive(PrimitiveType.Sphere, pos, Vector3.one * 0.22f, bulletMat);
            s.transform.SetParent(root.transform, true);
            Renderer r = s.GetComponent<Renderer>();
            if (r != null)
            {
                r.material.color = color;
                if (r.material.HasProperty("_EmissionColor")) r.material.SetColor("_EmissionColor", color * 2f);
            }
        }
        return p;
    }

    void Update()
    {
        float step = speed * Time.deltaTime;
        RaycastHit[] hits = Physics.SphereCastAll(transform.position, radius, direction, step, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        CartoHealth bestHealth = null;
        bool hitSomething = false;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i].collider;
            if (c == null) continue;
            CartoHealth h = c.GetComponentInParent<CartoHealth>();
            if (h != null && (h.team == team || h.IsDead)) continue;   // atraviesa a los aliados
            if (hits[i].distance < best)
            {
                best = hits[i].distance;
                bestHealth = h;
                hitSomething = true;
            }
        }
        if (hitSomething)
        {
            if (bestHealth != null) bestHealth.TakeDamage(damage, transform.position - direction);
            Destroy(gameObject);
            return;
        }
        transform.position += direction * step;
        life -= Time.deltaTime;
        if (life <= 0f) Destroy(gameObject);
    }
}
