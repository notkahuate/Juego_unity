using UnityEngine;

/// <summary>
/// Punto de control de una era: una franja que cruza el nivel a lo ancho, justo antes del jefe.
/// Se activa al cruzarla; si el cartografo cae despues, reaparece aqui
/// (CartoGame.RespawnAtCheckpoint) en vez de volver al Nexo.
/// </summary>
public class CartoCheckpoint : MonoBehaviour
{
    public float halfWidth = 32f;          // mitad del ancho de la franja (eje X local)
    public float halfDepth = 2.5f;         // mitad del fondo de la franja (eje Z local)
    public float yaw = 0f;                 // hacia donde mira el cartografo al reaparecer
    public GameObject activeVisual;        // se enciende al activarlo
    public GameObject inactiveVisual;      // se apaga al activarlo

    bool activated;

    public Vector3 RespawnPosition { get { return transform.position + Vector3.up * 0.1f; } }

    void Start()
    {
        SetVisual(false);
    }

    void Update()
    {
        if (activated) return;
        CartoPlayer player = CartoPlayer.Instance;
        if (player == null || player.IsDead) return;
        Vector3 local = transform.InverseTransformPoint(player.transform.position);
        if (Mathf.Abs(local.x) > halfWidth || Mathf.Abs(local.z) > halfDepth) return;

        activated = true;
        SetVisual(true);
        if (CartoGame.Instance != null) CartoGame.Instance.checkpoint = this;
        CartoHUD.Toast("Punto de control activado  —  si caes, reaparecerás aquí", 3.5f);
    }

    void SetVisual(bool on)
    {
        if (activeVisual != null) activeVisual.SetActive(on);
        if (inactiveVisual != null) inactiveVisual.SetActive(!on);
    }
}
