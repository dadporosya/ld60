using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps the music playing across a scene change. Every scene has its own <see cref="BGMManager"/>,
/// which is destroyed with the scene - and its AudioSources with it, cutting the music dead. So the
/// manager's sources live under this persistent object instead, and a manager that is destroyed
/// hands them over (<see cref="Release"/>): they fade out here while the next scene's manager
/// fades its own music in on fresh sources, which is the crossfade.
///
/// Created on demand; never placed in a scene.
/// </summary>
public class BGMSceneCrossfade : MonoBehaviour
{
    private static BGMSceneCrossfade instance;
    private static bool quitting;

    // A scene load can stall a frame for a long time; counting all of it would jump the fade.
    private const float MaxFadeStep = 0.1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        quitting = false;
    }

    /// <summary>Parent for music AudioSources that must outlive their scene. Null while quitting.</summary>
    public static Transform Root
    {
        get
        {
            if (quitting) return null;
            if (!instance)
            {
                GameObject go = new GameObject("BGM Sources (persistent)");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<BGMSceneCrossfade>();
            }

            return instance.transform;
        }
    }

    private void OnApplicationQuit() => quitting = true;

    /// <summary>
    /// Takes over the sources of a manager that is going away: the audible ones fade to silence over
    /// <paramref name="fadeTime"/> seconds (real time) and are destroyed, the rest are destroyed at once.
    /// </summary>
    public static void Release(List<AudioSource> sources, float fadeTime)
    {
        if (sources == null) return;

        // Nothing is created here: this runs from OnDestroy, also when the game is shutting down.
        bool canFade = !quitting && instance && instance.isActiveAndEnabled && fadeTime > 0f;

        foreach (AudioSource source in sources)
        {
            if (!source) continue;

            if (canFade && source.isPlaying && source.volume > 0f)
                instance.StartCoroutine(instance.FadeOutAndDestroy(source, fadeTime));
            else
                Destroy(source.gameObject);
        }
    }

    private IEnumerator FadeOutAndDestroy(AudioSource source, float fadeTime)
    {
        float from = source.volume;

        for (float t = 0f; t < fadeTime && source; t += Mathf.Min(Time.unscaledDeltaTime, MaxFadeStep))
        {
            source.volume = Mathf.Lerp(from, 0f, t / fadeTime);
            yield return null;
        }

        if (source) Destroy(source.gameObject);
    }
}
