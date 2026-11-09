using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The city around the Lane: a quiet bed of street noise and, now and then, a car passing, a horn, a
/// distant bell, a siren or a piano chord from somewhere around the player; plus an occasional crackle
/// from the nearest standing tower. It loads whatever is in <c>Resources/Ambience/&lt;Folder&gt;</c>, so
/// adding or swapping a file needs no scene change, and it adds itself when the Lane loads.
/// </summary>
public class LaneAmbience : MonoBehaviour
{
    private const string LaneScene = "Lane";

    private sealed class Layer
    {
        public string folder;
        public Vector2 interval;
        public Vector2 distance;
        public float volume;
        public float nextAt;
        public AudioClip[] clips;
        public int last = -1;
    }

    private Layer[] layers;
    private Tower[] towers = System.Array.Empty<Tower>();
    private AudioSource bed;
    private AudioSource[] shieldHums = System.Array.Empty<AudioSource>();
    private float nextZap;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == LaneScene && FindObjectOfType<LaneAmbience>() == null)
        {
            new GameObject(nameof(LaneAmbience)).AddComponent<LaneAmbience>();
        }
    }

    private void Start()
    {
        towers = FindObjectsOfType<Tower>();

        // A hum over each tower; it only sounds while the tower is shielded (an earlier one of its side still stands).
        AudioClip hum = ExperienceCues.Clip("tower_shield_loop");
        shieldHums = new AudioSource[towers.Length];

        if (hum != null)
        {
            for (int i = 0; i < towers.Length; i++)
            {
                AudioSource source = new GameObject("ShieldHum").AddComponent<AudioSource>();
                source.transform.SetParent(towers[i].transform, false);
                source.transform.localPosition = Vector3.up * 3f;
                source.clip = hum;
                source.loop = true;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 4f;
                source.maxDistance = 40f;
                source.volume = 0f;
                source.time = Random.value * hum.length;
                source.Play();
                shieldHums[i] = source;
            }
        }

        layers = new[]
        {
            Make("Cars", new Vector2(14f, 30f), new Vector2(15f, 35f), 0.45f),
            Make("Honks", new Vector2(25f, 55f), new Vector2(20f, 40f), 0.35f),
            Make("Bells", new Vector2(70f, 140f), new Vector2(30f, 50f), 0.4f),
            Make("Sirens", new Vector2(90f, 180f), new Vector2(30f, 50f), 0.3f),
            Make("Piano", new Vector2(45f, 90f), new Vector2(8f, 18f), 0.35f),
        };

        AudioClip city = Resources.Load<AudioClip>("Ambience/Bed/amb_city");

        if (city != null)
        {
            bed = new GameObject("CityBed").AddComponent<AudioSource>();
            bed.transform.SetParent(transform, false);
            bed.clip = city;
            bed.loop = true;
            bed.spatialBlend = 0f;
            bed.volume = 0f;
            bed.Play();
            StartCoroutine(FadeBed());
        }
    }

    private static Layer Make(string folder, Vector2 interval, Vector2 distance, float volume)
    {
        return new Layer
        {
            folder = folder,
            interval = interval,
            distance = distance,
            volume = volume,
            clips = Resources.LoadAll<AudioClip>("Ambience/" + folder),
            nextAt = Time.time + Random.Range(interval.x * 0.3f, interval.y),
        };
    }

    private IEnumerator FadeBed()
    {
        for (float t = 0f; t < 3f; t += Time.deltaTime)
        {
            bed.volume = Mathf.Lerp(0f, 0.3f, t / 3f);
            yield return null;
        }

        bed.volume = 0.3f;
    }

    private void Update()
    {
        Camera cam = Camera.main;

        if (cam == null || layers == null)
        {
            return;
        }

        foreach (Layer layer in layers)
        {
            if (layer.clips.Length == 0 || Time.time < layer.nextAt)
            {
                continue;
            }

            layer.nextAt = Time.time + Random.Range(layer.interval.x, layer.interval.y);

            int pick = Random.Range(0, layer.clips.Length);

            if (layer.clips.Length > 1 && pick == layer.last)
            {
                pick = (pick + 1) % layer.clips.Length;
            }

            layer.last = pick;

            // Anywhere around the player, a bit above street level, so it reads as the city and not a speaker.
            Vector2 around = Random.insideUnitCircle.normalized * Random.Range(layer.distance.x, layer.distance.y);
            Vector3 at = cam.transform.position + new Vector3(around.x, Random.Range(-0.5f, 4f), around.y);
            ExperienceCues.Spawn(layer.clips[pick], at, layer.volume, 1f);
        }

        PlayTowerCrackle(cam.transform.position);
        UpdateShieldHums();
    }

    private void UpdateShieldHums()
    {
        for (int i = 0; i < shieldHums.Length && i < towers.Length; i++)
        {
            if (shieldHums[i] == null)
            {
                continue;
            }

            bool shielded = towers[i] != null && towers[i].IsStanding && towers[i].IsLocked;
            shieldHums[i].volume = Mathf.MoveTowards(shieldHums[i].volume, shielded ? 0.35f : 0f, 0.5f * Time.deltaTime);
        }
    }

    /// <summary>A spark from the closest standing tower, so the objectives feel charged even when quiet.</summary>
    private void PlayTowerCrackle(Vector3 head)
    {
        if (Time.time < nextZap)
        {
            return;
        }

        nextZap = Time.time + Random.Range(7f, 14f);
        AudioClip[] zaps = Resources.LoadAll<AudioClip>("Ambience/TowerZap");
        Tower nearest = null;
        float best = 30f;

        foreach (Tower tower in towers)
        {
            if (tower == null || !tower.IsStanding)
            {
                continue;
            }

            float distance = Vector3.Distance(tower.transform.position, head);

            if (distance < best)
            {
                best = distance;
                nearest = tower;
            }
        }

        if (nearest != null && zaps.Length > 0)
        {
            ExperienceCues.Spawn(zaps[Random.Range(0, zaps.Length)], nearest.transform.position + Vector3.up * 4f, 0.4f, 1f);
        }
    }
}
