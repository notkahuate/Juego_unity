using UnityEngine;

/// <summary>
/// Limite del area jugable de una escena. El constructor lo crea junto a los muros invisibles
/// (objeto "Limites"): avisa en pantalla cuando el cartografo se acerca al borde y, si por lo que
/// sea llegara a salirse, lo devuelve dentro.
/// Los muros los crea CartoGameBuilder (LimitCircle en el Nexo, LimitRect en las eras).
/// </summary>
public class CartoBounds : MonoBehaviour
{
    public bool circular = true;
    public Vector3 center = Vector3.zero;
    public float radius = 22f;                           // limite circular (Nexo)
    public Vector2 halfSize = new Vector2(31f, 55f);     // limite rectangular: mitad del ancho (X) y del largo (Z)
    public float warnDistance = 2.5f;                    // a esta distancia del borde sale el aviso
    public string message = "Límite del mapa";

    bool warned;

    void LateUpdate()
    {
        CartoPlayer player = CartoPlayer.Instance;
        if (player == null || player.IsDead) return;

        Vector3 p = player.transform.position - center;
        float flat = new Vector2(p.x, p.z).magnitude;
        // Cuanto falta para el borde (negativo = fuera).
        float margin = circular ? radius - flat : Mathf.Min(halfSize.x - Mathf.Abs(p.x), halfSize.y - Mathf.Abs(p.z));

        if (margin < -1f)
        {
            // Red de seguridad: los muros no deberian dejar llegar aqui.
            Vector3 inside = p;
            if (circular)
            {
                float k = (radius - 0.5f) / Mathf.Max(0.01f, flat);
                inside.x *= k;
                inside.z *= k;
            }
            else
            {
                inside.x = Mathf.Clamp(p.x, -halfSize.x + 0.5f, halfSize.x - 0.5f);
                inside.z = Mathf.Clamp(p.z, -halfSize.y + 0.5f, halfSize.y - 0.5f);
            }
            player.Teleport(center + inside);
            return;
        }

        // Aviso breve al entrar en la franja del borde; no se repite hasta haberse alejado.
        if (margin < warnDistance)
        {
            if (!warned && !CartoHUD.Blocking)
            {
                warned = true;
                CartoHUD.EdgeWarning(message);
            }
        }
        else if (margin > warnDistance + 1.5f)
        {
            warned = false;
        }
    }
}
