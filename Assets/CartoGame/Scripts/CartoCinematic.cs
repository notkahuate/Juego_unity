using System;
using UnityEngine;

/// <summary>
/// Un plano de cinematica: la camara viaja de 'from' a 'to' mirando a 'lookAt' mientras se escribe 'text'.
/// Lo demas es opcional y sirve para dar tension: tarjetas en negro, rotulo, zoom lento, camara lenta
/// y "glitch temporal".
/// </summary>
[Serializable]
public class CartoShot
{
    public Vector3 from;
    public Vector3 to;
    public Vector3 lookAt;
    public float seconds = 5.5f;
    [TextArea] public string text = "";

    public bool card;               // tarjeta: pantalla en negro con el texto en el centro
    public string title = "";       // rotulo grande que aparece en la segunda mitad del plano
    public bool moveLook;           // el punto de mira viaja de 'lookAt' a 'lookAtEnd'
    public Vector3 lookAtEnd;
    public float fov;               // campo de vision al empezar el plano (0 = el de la camara)
    public float fovEnd;            // y al terminarlo (0 = igual que 'fov'): zoom lento
    public float glitch;            // segundos de glitch temporal al empezar el plano (0 = ninguno)
    public float worldSpeed = 1f;   // velocidad del mundo durante el plano (0.5 = camara lenta)
}

/// <summary>
/// Cinematica de historia de una escena: una lista de planos con texto narrativo.
/// El constructor la guarda en el objeto "_CartoGame". CartoGame la lanza al empezar la partida
/// (Nexo) y la primera vez que se entra en cada era; CartoHUD dibuja las barras de cine, el texto
/// letra por letra y el glitch, y la salta con Enter.
/// No usa Timeline ni paquetes extra: solo mueve la camara principal.
/// </summary>
public class CartoCinematic : MonoBehaviour
{
    /// <summary>La cinematica que se esta reproduciendo (null si no hay ninguna).</summary>
    public static CartoCinematic Active;

    public CartoShot[] shots = new CartoShot[0];

    const float Fade = 0.6f;         // fundido a negro al entrar y salir de cada plano
    const float TypeDelay = 0.5f;    // el texto empieza cuando la imagen ya se ve
    const float TypeSpeed = 32f;     // letras por segundo

    int index;
    float t;            // segundos dentro del plano actual
    float elapsed;      // segundos desde que empezo la cinematica
    Action onDone;
    Camera cam;
    float baseFov = 60f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void DomainReset()
    {
        Active = null;
    }

    public bool HasShots { get { return shots != null && shots.Length > 0; } }

    CartoShot Current { get { return shots[Mathf.Clamp(index, 0, shots.Length - 1)]; } }

    public string Caption { get { return HasShots && Current.text != null ? Current.text : ""; } }

    /// <summary>El texto del plano tal como va escrito hasta ahora (aparece letra por letra).</summary>
    public string CaptionTyped
    {
        get
        {
            string s = Caption;
            int n = Mathf.Clamp(Mathf.FloorToInt((t - TypeDelay) * TypeSpeed), 0, s.Length);
            return n >= s.Length ? s : s.Substring(0, n);
        }
    }

    public bool Typing { get { return (t - TypeDelay) * TypeSpeed < Caption.Length; } }

    /// <summary>El plano actual es una tarjeta: negro con el texto en el centro.</summary>
    public bool IsCard { get { return HasShots && Current.card; } }

    public string Title { get { return HasShots && Current.title != null ? Current.title : ""; } }

    /// <summary>Opacidad del rotulo: entra en la segunda mitad del plano y se va con el fundido final.</summary>
    public float TitleAlpha
    {
        get
        {
            if (Title.Length == 0) return 0f;
            float start = Current.seconds * 0.5f;
            return Mathf.Clamp01((t - start) / 1.2f) * Mathf.Clamp01((Current.seconds - t) / Fade);
        }
    }

    /// <summary>Opacidad del texto: entra al empezar el plano y se apaga justo antes de acabar.</summary>
    public float TextAlpha
    {
        get { return HasShots ? Mathf.Clamp01(Mathf.Min(t / 0.3f, (Current.seconds - t) / 0.5f)) : 0f; }
    }

    /// <summary>Cuanto negro hay en pantalla (1 = todo negro): fundido del plano actual, o tarjeta.</summary>
    public float Blackness
    {
        get
        {
            if (!HasShots || Current.card) return 1f;
            float fromStart = t / Fade;
            float toEnd = (Current.seconds - t) / Fade;
            return 1f - Mathf.Clamp01(Mathf.Min(fromStart, toEnd));
        }
    }

    /// <summary>Barras de cine: 0 al empezar, 1 cuando ya han entrado del todo.</summary>
    public float Letterbox
    {
        get
        {
            float k = Mathf.Clamp01(elapsed / 1.1f);
            return k * k * (3f - 2f * k);
        }
    }

    /// <summary>
    /// Fuerza del glitch temporal (0-1): una rafaga al empezar el plano y un eco mas corto pasada la mitad.
    /// </summary>
    public float Glitch
    {
        get
        {
            if (!HasShots) return 0f;
            float g = Current.glitch;
            if (g <= 0f) return 0f;
            float burst = 1f - t / g;
            float mid = Current.seconds * 0.62f;
            float echo = t > mid ? 0.7f * (1f - (t - mid) / (g * 0.5f)) : 0f;
            return Mathf.Clamp01(Mathf.Max(burst, echo));
        }
    }

    /// <summary>Numero "al azar" (0-1) fijo para cada entero: el glitch cambia a saltos, no cada fotograma.</summary>
    public static float Hash(int n)
    {
        unchecked
        {
            uint x = (uint)n * 747796405u + 2891336453u;
            x = ((x >> 15) ^ x) * 277803737u;
            x ^= x >> 13;
            return (x & 0xFFFFFF) / 16777216f;
        }
    }

    /// <summary>Empieza la cinematica. 'done' se llama al terminar o al saltarla.</summary>
    public void Play(Action done)
    {
        if (!HasShots)
        {
            if (done != null) done();
            return;
        }
        index = 0;
        t = 0f;
        elapsed = 0f;
        onDone = done;
        cam = Camera.main;
        if (cam != null) baseFov = cam.fieldOfView;
        Active = this;
        CartoHUD.ShowOverlay(CartoHUD.Overlay.Cinematic);
        ApplyCamera();
    }

    public void Skip()
    {
        Finish();
    }

    void Finish()
    {
        if (Active != this) return;
        Active = null;
        Restore();
        Action done = onDone;
        onDone = null;
        CartoHUD.FadeFromBlack();
        if (done != null) done();
    }

    /// <summary>Deja la camara y el tiempo como estaban antes de la cinematica.</summary>
    void Restore()
    {
        Time.timeScale = 1f;
        if (cam != null) cam.fieldOfView = baseFov;
    }

    void LateUpdate()
    {
        if (Active != this) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);   // un tiron de carga no se come un plano
        t += dt;
        elapsed += dt;
        if (t >= Current.seconds)
        {
            index++;
            t = 0f;
            if (index >= shots.Length)
            {
                Finish();
                return;
            }
        }
        ApplyWorldSpeed();
        ApplyCamera();
    }

    /// <summary>Camara lenta del plano; durante el glitch el tiempo del mundo se atasca y da saltos.</summary>
    void ApplyWorldSpeed()
    {
        float speed = Current.worldSpeed > 0f ? Current.worldSpeed : 1f;
        if (Glitch > 0.05f)
        {
            float n = Hash(Mathf.FloorToInt(Time.unscaledTime * 14f) + 911);
            speed = n < 0.45f ? 0.05f : (n > 0.8f ? 2.2f : speed);
        }
        Time.timeScale = speed;
    }

    void ApplyCamera()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        CartoShot s = Current;

        // Glitch: el recorrido da saltos hacia atras y la camara tiembla, se inclina y cambia de zoom.
        float g = Glitch;
        float n1 = 0.5f, n2 = 0.5f, n3 = 0.5f;
        float tc = t;
        if (g > 0f)
        {
            int step = Mathf.FloorToInt(Time.unscaledTime * 16f);
            n1 = Hash(step);
            n2 = Hash(step + 71);
            n3 = Hash(step + 233);
            if (n1 > 0.6f) tc = Mathf.Max(0f, t - 0.5f * g * n2);
        }

        float k = Mathf.Clamp01(tc / Mathf.Max(0.1f, s.seconds));
        Vector3 pos = Vector3.Lerp(s.from, s.to, k);
        Vector3 look = s.moveLook ? Vector3.Lerp(s.lookAt, s.lookAtEnd, k) : s.lookAt;
        Vector3 dir = look - pos;
        Quaternion rot = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : cam.transform.rotation;
        float fov = baseFov;
        if (s.fov > 0f) fov = Mathf.Lerp(s.fov, s.fovEnd > 0f ? s.fovEnd : s.fov, k);

        if (g > 0f)
        {
            pos += rot * new Vector3((n2 - 0.5f) * 0.5f * g, (n3 - 0.5f) * 0.3f * g, 0f);
            rot *= Quaternion.Euler(0f, 0f, (n1 - 0.5f) * 9f * g);
            fov += (n3 - 0.5f) * 12f * g;
        }

        cam.transform.SetPositionAndRotation(pos, rot);
        cam.fieldOfView = Mathf.Clamp(fov, 10f, 100f);
    }

    void OnDisable()
    {
        if (Active != this) return;
        Active = null;
        Restore();
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
    }
}
