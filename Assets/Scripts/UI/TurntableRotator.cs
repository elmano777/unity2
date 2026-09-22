using UnityEngine;

/// <summary>Slowly spins this transform around its local Y axis (hero preview turntable).</summary>
public class TurntableRotator : MonoBehaviour
{
    [SerializeField] private float degreesPerSecond = 20f;

    public float DegreesPerSecond
    {
        get => degreesPerSecond;
        set => degreesPerSecond = value;
    }

    private void Update()
    {
        transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self);
    }
}
