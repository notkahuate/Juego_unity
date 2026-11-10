using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Altar de mejoras del Nexo: se gasta oro para mejorar armas y vida.</summary>
public class CartoShop : MonoBehaviour
{
    public static CartoShop Instance;
    public static bool IsOpen;

    public float radius = 3.6f;

    void Awake()
    {
        Instance = this;
        IsOpen = false;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            IsOpen = false;
        }
    }

    public static void Close()
    {
        IsOpen = false;
    }

    void Update()
    {
        CartoPlayer player = CartoPlayer.Instance;
        if (player == null || player.IsDead)
        {
            IsOpen = false;
            return;
        }
        Vector3 to = player.transform.position - transform.position;
        to.y = 0f;
        bool near = to.sqrMagnitude <= radius * radius;
        if (!near)
        {
            IsOpen = false;
            return;
        }

        if (!IsOpen)
        {
            if (CartoHUD.OverlayActive) return;
            CartoHUD.SetPrompt("[E]  Altar de mejoras");
            if (CartoInput.InteractDown) IsOpen = true;
            return;
        }

        if (CartoInput.InteractDown)
        {
            IsOpen = false;
            return;
        }
        if (CartoInput.NumberDown(1)) BuyWeapon(CartoGameState.meleeId);
        if (CartoInput.NumberDown(2)) BuyWeapon(CartoGameState.rangedId);
        if (CartoInput.NumberDown(3)) BuyVitality();
    }

    void BuyWeapon(string id)
    {
        CartoWeaponDef def = CartoWeaponLibrary.Instance != null ? CartoWeaponLibrary.Instance.Get(id) : null;
        if (def == null)
        {
            CartoHUD.Toast("Aún no tienes un arma de ese tipo", 2f);
            return;
        }
        if (CartoGameState.WeaponLevel(id) >= CartoGameState.MaxWeaponLevel)
        {
            CartoHUD.Toast(def.displayName + " ya está al máximo", 2f);
            return;
        }
        int cost = CartoGameState.WeaponUpgradeCost(id);
        if (CartoGameState.gold < cost)
        {
            CartoHUD.Toast("No tienes suficiente oro", 2f);
            return;
        }
        CartoGameState.gold -= cost;
        CartoGameState.UpgradeWeapon(id);
        CartoHUD.Toast(def.displayName + " mejorada a nivel " + (CartoGameState.WeaponLevel(id) + 1), 2.5f);
    }

    void BuyVitality()
    {
        int cost = CartoGameState.VitalityCost;
        if (CartoGameState.gold < cost)
        {
            CartoHUD.Toast("No tienes suficiente oro", 2f);
            return;
        }
        CartoGameState.gold -= cost;
        CartoGameState.vitalityLevel++;
        CartoGameState.maxHealth += 25f;
        CartoGameState.health = CartoGameState.maxHealth;
        CartoPlayer player = CartoPlayer.Instance;
        if (player != null)
        {
            player.health.maxHealth = CartoGameState.maxHealth;
            player.health.current = CartoGameState.maxHealth;
        }
        CartoHUD.Toast("Vida máxima aumentada a " + Mathf.RoundToInt(CartoGameState.maxHealth), 2.5f);
    }

    /// <summary>Texto de cada opcion, para que el HUD lo dibuje.</summary>
    public static string OptionText(int n)
    {
        CartoWeaponLibrary lib = CartoWeaponLibrary.Instance;
        if (n == 1 || n == 2)
        {
            string id = n == 1 ? CartoGameState.meleeId : CartoGameState.rangedId;
            CartoWeaponDef def = lib != null ? lib.Get(id) : null;
            string kind = n == 1 ? "cuerpo a cuerpo" : "a distancia";
            if (def == null) return "[" + n + "]  Arma " + kind + ":  (aún no tienes)";
            int lvl = CartoGameState.WeaponLevel(id);
            if (lvl >= CartoGameState.MaxWeaponLevel)
                return "[" + n + "]  " + def.displayName + "  Nv." + (lvl + 1) + "  (MÁXIMO)";
            float now = def.damage * (1f + 0.25f * lvl);
            float next = def.damage * (1f + 0.25f * (lvl + 1));
            return "[" + n + "]  Mejorar " + def.displayName + "  Nv." + (lvl + 1) + " > Nv." + (lvl + 2) +
                   "   daño " + Mathf.RoundToInt(now) + " > " + Mathf.RoundToInt(next) +
                   "   —   " + CartoGameState.WeaponUpgradeCost(id) + " oro";
        }
        return "[3]  Vitalidad  +25 vida máxima (ahora " + Mathf.RoundToInt(CartoGameState.maxHealth) +
               ")   —   " + CartoGameState.VitalityCost + " oro";
    }
}
