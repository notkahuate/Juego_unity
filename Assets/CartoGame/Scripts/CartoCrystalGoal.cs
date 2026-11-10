using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Cristal del tiempo al final de cada era. Se libera al derrotar al guardian.</summary>
public class CartoCrystalGoal : MonoBehaviour
{
    public int era = 0;                 // 0 = pirata, 1 = samurai
    public string eraName = "Era Pirata";
    public CartoHealth guardian;
    public GameObject crystalVisual;
    public GameObject barrier;
    public float radius = 3.2f;

    bool taken;
    float leaveAt = -1f;

    void Start()
    {
        if (CartoGameState.HasCrystal(era))
        {
            taken = true;
            if (crystalVisual != null) crystalVisual.SetActive(false);
            if (barrier != null) barrier.SetActive(false);
        }
    }

    bool Unlocked { get { return guardian == null || guardian.IsDead; } }

    void Update()
    {
        if (leaveAt > 0f)
        {
            if (Time.time >= leaveAt)
            {
                leaveAt = -1f;
                SceneManager.LoadScene(CartoGameState.SceneHub);
            }
            return;
        }
        if (taken) return;

        if (barrier != null && barrier.activeSelf && Unlocked) barrier.SetActive(false);

        CartoPlayer player = CartoPlayer.Instance;
        if (player == null || player.IsDead) return;
        Vector3 to = player.transform.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > radius * radius) return;

        if (!Unlocked)
        {
            CartoHUD.SetPrompt("El cristal está sellado. Derrota al guardián");
            return;
        }
        CartoHUD.SetPrompt("[E]  Tomar el cristal del tiempo");
        if (CartoInput.InteractDown && !CartoHUD.Blocking)
        {
            taken = true;
            CartoGameState.crystals[era] = true;
            if (crystalVisual != null) crystalVisual.SetActive(false);
            CartoHUD.Toast("¡Cristal de la " + eraName + " recuperado!  (" + CartoGameState.CrystalCount + "/2)", 5f);
            leaveAt = Time.time + 2.2f;
        }
    }
}
