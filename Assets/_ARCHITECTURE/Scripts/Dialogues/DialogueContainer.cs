using System;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "DialogueSpeaker", menuName = "Dialogues/DialogueSpeaker")]
public class DialogueSpeaker : ScriptableObject
{
    [Localized] public string speakerName;
    public Sprite portrait;
    [Tooltip("Typing voice for this speaker. Empty = the SFXManager's default dialogue voice.")]
    // Not initialised from SFXManager.Instance: it doesn't exist in edit mode, so creating the
    // asset would throw. An empty list already falls back to the default (DialogueNode.speakerVoiceClips).
    public List<AudioClip> voiceClips = new List<AudioClip>();
}

[Serializable]
public class DialogueNode
{
    [HideInInspector] public int nodeId = -1;
    [Tooltip("leave blank if you do not wanna to reinit it")]
    public string speakerId;
    [HideInInspector] public bool initialized = false;
    public bool speakerIsThis=true; //
    public bool overWriteValues = false;
    [TextArea(3, 7), Localized]
    public string text;
    
    [SerializeField] bool initFromScriptableObject=true;
    public DialogueSpeaker speaker;
    public GameObject speakerGameObject;
    [Localized] public string speakerName;
    public Sprite speakerPortrait;
    public bool useSpriteAtlas = true;
    public Sprite[] spriteAtlas;
    public int spriteId;

    public Material material;

    [Tooltip("Tint the dialogue text for this node. Normally filled in from the speaking " +
             "Talkable; when off the window keeps its own default text color.")]
    public bool useCustomTextColor = false;
    [Tooltip("Color the dialogue text is set to for this node.")]
    public Color textColor = Color.white;

    [Tooltip("Move on to the next node by itself, autoSkipDelay seconds after this node's text has " +
             "finished typing. Off: the node follows the DialogueManager's own auto-skip setting.")]
    public bool autoSkip = false;
    [Tooltip("Seconds after the text finishes typing before this node moves on (real time, so it also " +
             "runs over a paused game).")]
    [Min(0f)] public float autoSkipDelay = 2f;

    public UnityEvent onNodeStart;
    public UnityEvent onNodeEnd;
    
    
    
    /// <summary>
    /// Runtime-only voice for this node, set by whatever stages it (e.g. a visual novel node's
    /// voice settings) before the text starts typing. Null = the speaker's own voice; an empty
    /// list = silent.
    /// </summary>
    [NonSerialized] public List<AudioClip> voiceOverride;

    /// <summary>
    /// Runtime-only, set the same way as <see cref="voiceOverride"/>: multiplies the DialogueManager's
    /// voice volume on this node. Null = 1.
    /// </summary>
    [NonSerialized] public float? voiceVolumeScale;

    /// <summary>Runtime-only: replaces the DialogueManager's voice pitch range on this node. Null = keep it.</summary>
    [NonSerialized] public Vector2? voicePitchOverride;

    /// Voice clips are never stored on the node: they are read live from the speaking
    /// Talkable (or DialogueSpeaker asset), so editing a character's clips always takes effect.
    public List<AudioClip> speakerVoiceClips
    {
        get
        {
            if (voiceOverride != null) return voiceOverride;
            if (speakerGameObject && speakerGameObject.TryGetComponent<Talkable>(out var talkable)
                && talkable.voiceClips != null && talkable.voiceClips.Count > 0)
                return talkable.voiceClips;
            if (speaker && speaker.voiceClips != null && speaker.voiceClips.Count > 0)
                return speaker.voiceClips;
            return SFXManager.Instance ? SFXManager.Instance.defaultDialogueVoiceList : null;
        }
    }

    public void Init()
    {
        initialized = false;
        if (speakerIsThis) return;
        
        if (initFromScriptableObject && speaker != null)
        {
            InitFromScriptableObject(speaker);
            return;
        }

        if (speakerGameObject)
        {
            InitFromGameObject(speakerGameObject);
            return;
        }

        if(h.CheckIfAllExist(speakerName, speakerPortrait))
        {
            InitFromDirectValues(speakerName, speakerPortrait);
            return;
        }
        
        speakerIsThis = true;
    }

    public void InitFromScriptableObject(DialogueSpeaker data)
    {
        initialized = true;
        if (overWriteValues || speaker == null) speaker = data;
        if (overWriteValues || string.IsNullOrEmpty(speakerName)) speakerName = speaker.speakerName;
        if (overWriteValues || speakerPortrait == null) speakerPortrait = speaker.portrait;
    }
    public void InitFromGameObject(GameObject go)
    {
        // h.Out("init from go");
        speakerGameObject = go;
        initialized = true;
        if (go.TryGetComponent<Talkable>(out var talkable))
        {
            if (overWriteValues || speakerName == "") speakerName = talkable.Name;
            
            if (overWriteValues && talkable.portraitAtlas != null && talkable.portraitAtlas.Length > 0)
            {
                speakerPortrait = talkable.portraitAtlas[spriteId];
            } else if (!overWriteValues && spriteAtlas != null && spriteAtlas.Length > 0 && spriteId < spriteAtlas.Length)
            {
                speakerPortrait = spriteAtlas[spriteId];
            } else if (overWriteValues || speakerPortrait == null) speakerPortrait = talkable.Portrait;
            
            if (talkable.material) material = talkable.material;

            // The speaker's text color wins over the node's own; a speaker that doesn't
            // define one leaves the node untouched (so an authored per-node color survives).
            if (talkable.useCustomTextColor)
            {
                useCustomTextColor = true;
                textColor = talkable.textColor;
            }
        }
        else
        {
            if (overWriteValues || string.IsNullOrEmpty(speakerName)) speakerName = go.name;
            if (!overWriteValues && spriteAtlas != null && spriteAtlas.Length > 0 && spriteId < spriteAtlas.Length)
            {
                speakerPortrait = spriteAtlas[spriteId];
            } else if (overWriteValues || speakerPortrait == null) speakerPortrait = go.GetComponent<SpriteRenderer>().sprite;
        }

        InitSpeaker();
    }

    public void InitFromDirectValues(
        string nameIn,
        Sprite spriteIn
        )
    {
        if (overWriteValues || string.IsNullOrEmpty(speakerName)) speakerName = nameIn;
        if (overWriteValues || speakerPortrait == null) speakerPortrait = spriteIn;
        InitSpeaker();
    }

    private void InitSpeaker()
    {
        speaker = ScriptableObject.CreateInstance<DialogueSpeaker>();
        speaker.speakerName = speakerName;
        speaker.portrait = speakerPortrait;
    }
    
    void OnDisable() // ???
    {
        initialized = false;
        if (!initFromScriptableObject && !speakerIsThis) speaker = null;
    }
}


[CreateAssetMenu(fileName = "DialogueContainer", menuName = "Dialogues/DialogueContainer")]
public class DialogueContainer : ScriptableObject
{
    public List<string> speakerIds = new List<string>();
    public List<GameObject> speakerPrefabs = new List<GameObject>();
    public Dictionary<string, GameObject> speakers = new Dictionary<string, GameObject>();
    public List<DialogueNode> nodes = new List<DialogueNode>();

    [Tooltip("Typing voice for every node of this dialogue, instead of the speaker's. Empty = each " +
             "node's speaker voice, falling back to the DialogueManager's default. A visual novel " +
             "node set to a Custom or Silent voice keeps its own.")]
    public List<AudioClip> defaultVoiceClips = new List<AudioClip>();

    public bool HasDefaultVoice => defaultVoiceClips != null && defaultVoiceClips.Count > 0;

    // virtual so overrides (e.g. DialogueContainerWithModels) can extend init/teardown.
    protected virtual void OnEnable()
    {
        speakers.Clear();

        AssignNodeIds();

        int idCount = speakerIds.Count;
        int pfbCount = speakerPrefabs.Count;
        for (int i = 0; i < idCount; i++)
        {
            string id = speakerIds[i];
            if (string.IsNullOrEmpty(id)) continue;
            GameObject pfb = pfbCount > i ? speakerPrefabs[i] : null;
            speakers[id] = pfb;
        }
    }

    protected virtual void OnDisable()
    {
        speakers.Clear();
    }

    /// <summary>
    /// Assigns each node its index in the nodes list as nodeId.
    /// </summary>
    public void AssignNodeIds()
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] != null) nodes[i].nodeId = i;
        }
    }
}


[Serializable]
public class SerializableDialogueContainer
{
    public List<DialogueNode> values = new List<DialogueNode>();

    public DialogueContainer Convert()
    {
        DialogueContainer d = ScriptableObject.CreateInstance<DialogueContainer>();
        d.nodes = values;
        return d;
    }
}
