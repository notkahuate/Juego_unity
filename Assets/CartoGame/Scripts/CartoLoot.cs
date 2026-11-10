using UnityEngine;

/// <summary>Moneda de oro o corazon que vuela hacia el jugador.</summary>
public class CartoLoot : MonoBehaviour
{
    public enum Kind { Gold, Heart }

    public Kind kind = Kind.Gold;
    public int value = 5;

    static Material goldMat;
    static Material heartMat;

    Vector3 velocity;
    float born;

    public static void Spawn(Vector3 pos, Kind kind, int value)
    {
        if (goldMat == null) goldMat = CartoUtil.MakeMaterial(new Color(1f, 0.8f, 0.15f), 0.6f);
        if (heartMat == null) heartMat = CartoUtil.MakeMaterial(new Color(0.95f, 0.15f, 0.25f), 0.8f);

        GameObject go;
        if (kind == Kind.Gold)
            go = CartoUtil.Primitive(PrimitiveType.Cylinder, pos, new Vector3(0.38f, 0.04f, 0.38f), goldMat);
        else
            go = CartoUtil.Primitive(PrimitiveType.Sphere, pos, Vector3.one * 0.42f, heartMat);
        go.name = kind == Kind.Gold ? "Moneda" : "Corazon";
        go.transform.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);

        var loot = go.AddComponent<CartoLoot>();
        loot.kind = kind;
        loot.value = value;
        Vector2 r = Random.insideUnitCircle * 3.2f;
        loot.velocity = new Vector3(r.x, Random.Range(4f, 6.5f), r.y);
        loot.born = Time.time;
    }

    void Update()
    {
        CartoPlayer player = CartoPlayer.Instance;
        float age = Time.time - born;
        transform.Rotate(0f, 240f * Time.deltaTime, 0f, Space.World);

        if (age < 0.6f || player == null)
        {
            // Salto inicial
            velocity.y -= 18f * Time.deltaTime;
            Vector3 p = transform.position + velocity * Time.deltaTime;
            if (p.y < 0.35f && velocity.y < 0f)
            {
                p.y = 0.35f;
                velocity = Vector3.zero;
            }
            transform.position = p;
        }
        else
        {
            Vector3 target = player.transform.position + Vector3.up * 1f;
            Vector3 to = target - transform.position;
            float d = to.magnitude;
            if (d < 0.8f)
            {
                Collect(player);
                return;
            }
            float speed = Mathf.Min(22f, 5f + (age - 0.6f) * 16f);
            transform.position += to / d * Mathf.Min(d, speed * Time.deltaTime);
        }

        if (age > 30f) Destroy(gameObject);
    }

    void Collect(CartoPlayer player)
    {
        if (kind == Kind.Gold)
        {
            CartoGameState.gold += value;
        }
        else
        {
            player.health.Heal(value);
            CartoHUD.AddFloater(player.transform.position + Vector3.up * 2f, "+" + value + " vida", new Color(0.4f, 1f, 0.5f));
        }
        Destroy(gameObject);
    }
}
