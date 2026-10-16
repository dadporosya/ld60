using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class SFXManager : AudioManagerBase
{
    public static SFXManager Instance; // Instance

    public List<AudioClip> defaultDialogueVoiceList;
    
    private void Awake()
    {
        // h.Out(Instance);
        h.CreateStaticInstance(this, ref Instance);
        // h.Out(Instance);
        
        InitSource(ref defaultAudioSource, AudioMixerManager.GetSFXGroup());
    }
}
