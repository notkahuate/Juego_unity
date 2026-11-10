using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Gira y flota. Para cristales, armas en el suelo y portales.</summary>
public class CartoSpin : MonoBehaviour
{
    public Vector3 degreesPerSecond = new Vector3(0f, 60f, 0f);
    public float bobHeight = 0f;
    public float bobSpeed = 1.5f;

    Vector3 basePos;
    float phase;

    void Start()
    {
        basePos = transform.localPosition;
        phase = Random.Range(0f, 6.28f);
    }

    void Update()
    {
        transform.Rotate(degreesPerSecond * Time.deltaTime, Space.World);
        if (bobHeight > 0f)
            transform.localPosition = basePos + Vector3.up * (Mathf.Sin(Time.time * bobSpeed + phase) * bobHeight);
    }
}
