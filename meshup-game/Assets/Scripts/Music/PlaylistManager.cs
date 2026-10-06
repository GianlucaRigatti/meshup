using UnityEngine;
using System.Collections;
using Meshup.Game;

public class PlaylistManager : MonoBehaviour
{
    private const float UnderwaterAmbienceVolume = 0.025f;

    public AudioSource audioSource;
    public AudioClip[] playlist; 
    private int currentTrackIndex = 0;
    private AudioSource underwaterAmbienceSource;

    void Start()
    {
        StartUnderwaterAmbience();

        if (playlist.Length > 0 && audioSource != null)
        {
            StartCoroutine(PlayPlaylist());
        }
    }

    private void StartUnderwaterAmbience()
    {
        var assets = MeshupRuntimeAssets.LoadDefault();
        var ambience = assets != null ? assets.UnderwaterAmbience : null;
        if (ambience == null)
        {
            Debug.LogWarning("[MeshUp] Underwater ambience clip is missing.",
                this);
            return;
        }

        underwaterAmbienceSource = gameObject.AddComponent<AudioSource>();
        underwaterAmbienceSource.clip = ambience;
        underwaterAmbienceSource.playOnAwake = false;
        underwaterAmbienceSource.loop = true;
        underwaterAmbienceSource.spatialBlend = 0f;
        underwaterAmbienceSource.volume = UnderwaterAmbienceVolume;
        underwaterAmbienceSource.priority = 192;
        underwaterAmbienceSource.Play();
    }

    IEnumerator PlayPlaylist()
    {
        while (true)
        {
            audioSource.clip = playlist[currentTrackIndex];
            audioSource.Play();

            yield return new WaitForSeconds(audioSource.clip.length);

            currentTrackIndex = (currentTrackIndex + 1) % playlist.Length;
        }
    }
}
