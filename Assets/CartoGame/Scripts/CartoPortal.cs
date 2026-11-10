using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Portal entre el Nexo y las eras.</summary>
public class CartoPortal : MonoBehaviour
{
    public string targetScene = "Carto_Hub";
    public string label = "Volver al Nexo";
    public float radius = 2.6f;

    void Update()
    {
        CartoPlayer player = CartoPlayer.Instance;
        if (player == null || player.IsDead || CartoHUD.Blocking) return;
        Vector3 to = player.transform.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > radius * radius) return;

        CartoHUD.SetPrompt("[E]  " + label);
        if (CartoInput.InteractDown)
        {
            CartoGameState.health = player.health.current;
            SceneManager.LoadScene(targetScene);
        }
    }
}
