using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Arma en el suelo. Al acercarse, el cartografo la recoge y la equipa.</summary>
public class CartoWeaponPickup : MonoBehaviour
{
    public string weaponId = "sable";
    public float radius = 1.8f;

    void Start()
    {
        if (CartoGameState.Owns(weaponId)) Destroy(gameObject);
    }

    void Update()
    {
        CartoPlayer player = CartoPlayer.Instance;
        if (player == null || player.IsDead) return;
        Vector3 to = player.transform.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > radius * radius) return;

        CartoGameState.AddWeapon(weaponId);
        CartoWeaponDef def = CartoWeaponLibrary.Instance != null ? CartoWeaponLibrary.Instance.Get(weaponId) : null;
        if (def != null)
        {
            if (def.ranged) CartoGameState.rangedId = weaponId; else CartoGameState.meleeId = weaponId;
            string how = def.ranged ? "Clic derecho para disparar" : "Clic izquierdo para atacar";
            CartoHUD.Toast("¡Nueva arma: " + def.displayName + "!  " + how, 4f);
        }
        player.RefreshWeapons();
        Destroy(gameObject);
    }
}
