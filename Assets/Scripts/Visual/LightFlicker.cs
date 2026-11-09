using UnityEngine;

/// <summary>
/// Candle / fireplace flicker: drives the light's intensity (and optionally its range) with layered
/// Perlin noise around the base value it starts with. Cheap enough for a handful of lights on Quest.
/// </summary>
[RequireComponent(typeof(Light))]
public class LightFlicker : MonoBehaviour
{
    [Tooltip("Fraction of the base intensity the noise can add or remove.")]
    [Range(0f, 1f)] public float amount = 0.25f;
    [Tooltip("Noise speed. Candles ~3, a fireplace ~1.5.")]
    public float speed = 3f;
    [Tooltip("Also wobble the range by this fraction.")]
    [Range(0f, 0.5f)] public float rangeAmount = 0.05f;

    private Light target;
    private float baseIntensity;
    private float baseRange;
    private float seed;

    private void Awake()
    {
        target = GetComponent<Light>();
        baseIntensity = target.intensity;
        baseRange = target.range;
        seed = Random.value * 100f;
    }

    private void Update()
    {
        float t = Time.time * speed + seed;
        float n = Mathf.PerlinNoise(t, seed) * 0.7f + Mathf.PerlinNoise(t * 2.7f, seed + 7f) * 0.3f; // 0..1
        float k = (n - 0.5f) * 2f; // -1..1
        target.intensity = baseIntensity * (1f + k * amount);
        target.range = baseRange * (1f + k * rangeAmount);
    }
}
