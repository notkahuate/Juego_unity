using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>En el Nexo: muestra un objeto segun los cristales recuperados.</summary>
public class CartoCrystalDisplay : MonoBehaviour
{
    public int era = 0;            // -1 = todos los cristales
    public bool invert = false;    // true = visible mientras FALTE el cristal
    public GameObject target;

    void Start()
    {
        bool has = era < 0 ? CartoGameState.AllCrystals : CartoGameState.HasCrystal(era);
        if (target != null) target.SetActive(invert ? !has : has);
    }
}
