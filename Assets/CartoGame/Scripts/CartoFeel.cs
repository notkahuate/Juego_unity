using UnityEngine;

/// <summary>
/// Unico gestor de la sensacion de impacto: congelado breve del juego (hit-stop) y temblor de camara.
/// Todo se mide en tiempo real (unscaled), asi sigue funcionando con el juego congelado.
/// Se crea solo la primera vez que hace falta; no hay que ponerlo en la escena.
/// </summary>
public class CartoFeel : MonoBehaviour
{
    const float MaxStop = 0.12f;

    static CartoFeel instance;
    static bool stopping;
    static float stopUntil;
    static float shakeAmp;
    static float shakeStart;
    static float shakeDur = 0.1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void DomainReset()
    {
        instance = null;
        stopping = false;
        stopUntil = 0f;
        shakeAmp = 0f;
        shakeStart = 0f;
        shakeDur = 0.1f;
    }

    public static void Ensure()
    {
        if (instance != null) return;
        GameObject go = new GameObject("_CartoFeel");
        instance = go.AddComponent<CartoFeel>();
    }

    /// <summary>Congela el juego unos milisegundos (50-80 ms dan peso a un golpe).</summary>
    public static void HitStop(float seconds)
    {
        if (seconds <= 0f || CartoHUD.PausesTime) return;
        if (!stopping && Time.timeScale <= 0f) return;   // otro sistema tiene el tiempo parado
        Ensure();
        stopping = true;
        stopUntil = Mathf.Max(stopUntil, Time.unscaledTime + Mathf.Min(seconds, MaxStop));
        Time.timeScale = 0f;
    }

    /// <summary>Temblor de camara: 'amplitude' en metros, se apaga solo en 'seconds'.</summary>
    public static void Shake(float amplitude, float seconds)
    {
        float now = Time.unscaledTime;
        if (amplitude < CurrentAmp(now)) return;   // no pisa un temblor mas fuerte
        shakeAmp = amplitude;
        shakeStart = now;
        shakeDur = Mathf.Max(0.05f, seconds);
    }

    static float CurrentAmp(float now)
    {
        float k = 1f - (now - shakeStart) / shakeDur;
        return k > 0f ? shakeAmp * k * k : 0f;
    }

    /// <summary>Desplazamiento del temblor en el espacio de la camara (lo aplica CartoCamera).</summary>
    public static Vector3 ShakeOffset
    {
        get
        {
            float now = Time.unscaledTime;
            float a = CurrentAmp(now);
            if (a <= 0f) return Vector3.zero;
            float t = now * 38f;
            return new Vector3((Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 2f * a, (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f * a, 0f);
        }
    }

    void Update()
    {
        if (!stopping) return;
        if (CartoHUD.PausesTime)
        {
            stopping = false;   // la pausa manda sobre el tiempo
            return;
        }
        if (Time.unscaledTime >= stopUntil)
        {
            stopping = false;
            Time.timeScale = 1f;
        }
    }

    void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        if (stopping)
        {
            stopping = false;
            if (!CartoHUD.PausesTime) Time.timeScale = 1f;
        }
    }
}
