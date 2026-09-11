using UnityEngine;
using System.Collections;

public class PlaylistManager : MonoBehaviour
{
    public AudioSource audioSource;
    public AudioClip[] playlist; 
    private int currentTrackIndex = 0;

    void Start()
    {
        if (playlist.Length > 0 && audioSource != null)
        {
            StartCoroutine(PlayPlaylist());
        }
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