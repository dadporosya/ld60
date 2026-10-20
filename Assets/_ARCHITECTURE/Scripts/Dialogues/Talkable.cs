using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Events;

public class Talkable : MonoBehaviour
{
    
    [Localized] public string speakerName;

    public string Name
    {
        get => speakerName;
        set => speakerName = value;
    }

    public Sprite portrait;
    public Sprite[] portraitAtlas;
    public Sprite Portrait
    {
        get
        {
            if (portrait) return portrait;
            // Not every talkable has a visual (e.g. off-screen / "empty" speakers),
            // so a missing SpriteRenderer just means "no portrait".
            return TryGetComponent<SpriteRenderer>(out var sr) ? sr.sprite : null;
        }
        set
        {
            portrait = value;
            if (TryGetComponent<SpriteRenderer>(out var sr)) sr.sprite = portrait;
        }
    }

    public Material material;

    [Header("Dialogue Text Color")]
    [Tooltip("Tint the dialogue window's text while this speaker is talking. " +
             "When off, the window keeps its own default text color.")]
    public bool useCustomTextColor = false;
    [Tooltip("Color the dialogue text is set to while this speaker is talking.")]
    public Color textColor = Color.white;

    public List<AudioClip> voiceClips;
    
    [HideInInspector] public DialogueManager dialogueManager;
    
    [Header("Simple Monologues")]
    public List<ListOfStrWrapper> simpleMonologues = new List<ListOfStrWrapper>();
    
    [Header("Serializable Dialogues")]
    public List<SerializableDialogueContainer> rawDialogues = new List<SerializableDialogueContainer>();
    
    [Header("Dialogues' Scriptable Objects")]
    public List<DialogueContainer> dialogues = new List<DialogueContainer>();

    [Header("Dialogue Lifecycle Events")]
    [Tooltip("Fired right before the dialogue window's open animation plays.")]
    [SerializeField] public UnityEvent beforeDialogueStart = new UnityEvent();
    [Tooltip("Fired once the open animation finishes and the dialogue actually starts.")]
    [SerializeField] public UnityEvent onDialogueStart = new UnityEvent();
    [Tooltip("Fired when the dialogue ends.")]
    [SerializeField] public UnityEvent onDialogueEnd = new UnityEvent();

    public virtual void Awake()
    {
        if (TryGetComponent(out Unit unit))
        {
            speakerName = unit.displayedName;
        }
        if (speakerName == "")
            speakerName = gameObject.name;
        if (portrait == null)
            portrait = GetComponentInChildren<SpriteRenderer>()?.sprite;
        if (!portrait)  portrait = GetComponentInChildren<SpriteRenderer>()?.sprite;
        
        if (!dialogueManager) dialogueManager = FindFirstObjectByType<DialogueManager>();
        
        if ((voiceClips==null || voiceClips.Count==0)
            && SFXManager.Instance != null) voiceClips =  SFXManager.Instance.defaultDialogueVoiceList;
        // h.Out(gameObject.name, voiceClips);
        
        h.ForEach(rawDialogues, (d) =>
        {
            dialogues.Add(d.Convert());
        });
        
        h.ForEach(simpleMonologues, (m) =>
        {
            DialogueContainer d = ScriptableObject.CreateInstance<DialogueContainer>();
            
            h.ForEach(m.values, (s) =>
            {
                DialogueNode node = new DialogueNode();
                node.speakerIsThis = true;
                node.text = s;
                node.InitFromGameObject(gameObject);
                d.nodes.Add(node);
            });
            dialogues.Add(d);
        });
        
        // Initialize all dialogue nodes that haven't been initialized yet
        h.ForEach(dialogues, InitDialogue); // hz? 
    }
    
    

    /// <summary>
    /// Init dialogues nodes based on the talkable object.
    /// Attention! does not create an instance! be aware
    /// </summary>
    public void InitDialogue(DialogueContainer dialogue)
    {
        if (!dialogue) return;

        dialogue.AssignNodeIds();

        // h.Out(dialogue);
        h.ForEach(dialogue.nodes, (node) =>
        {
            node.Init();
            if (!node.initialized || node.speakerIsThis)
            {
                node.InitFromGameObject(gameObject);
            }
        });
    }

    public void Talk(int i = -1)
    {
        DialogueContainer dialogue;
        if (i >= 0 && i < dialogues.Count)  dialogue =  dialogues[i];
        else dialogue = h.RandChoice(dialogues);
        
        h.Out(dialogues.Count, dialogue);

        // Run the whole intro sequence before the manager opens its window.
        StartCoroutine(TalkRoutine(dialogue));
    }

    /// <summary>
    /// Plays the talkable's before-dialogue sequence to completion, THEN asks the manager
    /// to open the window. This keeps the talkable's intro animation and the window's open
    /// animation sequential (talkable first) instead of running them at the same time.
    /// </summary>
    protected virtual IEnumerator TalkRoutine(DialogueContainer dialogue)
    {
        yield return BeforeDialogueStartRoutine();
        dialogueManager.StartDialogue(dialogue, this);
    }

    /// <summary>
    /// The before-dialogue sequence. Base implementation just fires the before-dialogue
    /// events; subclasses override to also play an intro animation before yielding back.
    /// </summary>
    protected virtual IEnumerator BeforeDialogueStartRoutine()
    {
        BeforeDialogueStart();
        yield break;
    }

    /// <summary>
    /// Fires the before-dialogue events. Called before the window's open animation plays.
    /// </summary>
    public virtual void BeforeDialogueStart()
    {
        beforeDialogueStart?.Invoke();
    }

    /// <summary>
    /// Called once the open animation finishes and the dialogue actually starts.
    /// </summary>
    public virtual void OnDialogueStart()
    {
        onDialogueStart?.Invoke();
    }

    /// <summary>
    /// Called when the dialogue ends.
    /// </summary>
    public virtual void OnDialogueEnd()
    {
        onDialogueEnd?.Invoke();
    }

    public virtual void OnNodeStart(DialogueNode node=null)
    {

    }

    public virtual void OnNodeEnd(DialogueNode node=null)
    {

    }

}
