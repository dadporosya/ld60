using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnimationControllerBase : MonoBehaviour
{
    public AnimationPreferences.Type type;
    [SerializeField] private bool filterByTypes = true;
    public List<AnimationPreferences.Type> targetTypes = new List<AnimationPreferences.Type>();
    public List<AnimationBase> animations = new List<AnimationBase>();
    
    [SerializeField] private bool searchForAnimationInChildren = true;
    [SerializeField] private bool searchForAnimationInAllChildren = true;
    [SerializeField] private bool loop;
    [SerializeField] private bool playOnStart;

    public virtual void Awake()
    {
        if (!targetTypes.Contains(type))  targetTypes.Add(type);
        targetTypes.RemoveAll(t => t == AnimationPreferences.Type.None);
        if (targetTypes.Count == 0) filterByTypes = false;
        
        
        
        void AddAnimations(Transform t)
        {
            foreach (AnimationBase anim in t.GetComponents<AnimationBase>())
            {
                if (filterByTypes && !targetTypes.Contains(anim.type)) continue;
                anim.loop = loop;
                if (!animations.Contains(anim)) animations.Add(anim);
            }
        }

        AddAnimations(transform);

        if (searchForAnimationInChildren)
        {
            void AddAnimationsFromChildren(Transform t)
            {
                foreach (Transform child in t)
                {
                    AddAnimations(child);
                    if (searchForAnimationInAllChildren) AddAnimationsFromChildren(child);
                }
            }

            AddAnimationsFromChildren(transform);
        }
    }

    public virtual void Start()
    {
        if (playOnStart) StartCoroutine(PlayAnimations());
    }

    // One PlayAnimations() call in flight. Callers routinely do
    // StartCoroutine(controller.PlayAnimations()) from a DIFFERENT MonoBehaviour, so the controller
    // cannot StopCoroutine that outer routine. Instead the session carries a Cancelled flag the
    // outer routine polls, which lets it finish itself when StopAnimations() is called.
    //
    // Without this, a controller of looping animations never satisfied its old
    // `WaitUntil(() => remaining == 0)` — remaining never reached zero, StopAnimations() could not
    // reach the waiter, and every play/stop cycle stranded another coroutine. On units that start
    // and stop walking constantly this reached ~7,700 stuck coroutines polled every frame.
    private class PlaySession
    {
        public int Remaining;
        public bool Cancelled;
        public readonly List<Coroutine> Tracked = new List<Coroutine>();
        public bool Finished => Cancelled || Remaining <= 0;
    }

    private readonly List<PlaySession> activeSessions = new List<PlaySession>();

    public IEnumerator PlayAnimations(Action onAnimationEnd = null)
    {
        if (animations.Count == 0) { onAnimationEnd?.Invoke(); yield break; }

        PlaySession session = new PlaySession { Remaining = animations.Count };
        activeSessions.Add(session);

        foreach (AnimationBase animation in animations)
        {
            if (!animation) { session.Remaining--; continue; }
            session.Tracked.Add(StartCoroutine(TrackCoroutine(animation.Play(), session)));
        }

        while (!session.Finished) yield return null;

        activeSessions.Remove(session);

        // Cancelled means StopAnimations() interrupted us; only a natural finish is an "end".
        if (!session.Cancelled) onAnimationEnd?.Invoke();
    }

    private IEnumerator TrackCoroutine(IEnumerator routine, PlaySession session)
    {
        yield return routine;
        session.Remaining--;
    }

    public void StopAnimations()
    {
        CancelSessions();

        foreach (AnimationBase animation in animations)
            if (animation) animation.Stop();
    }

    // Releases every in-flight PlayAnimations(), including ones owned by other MonoBehaviours.
    private void CancelSessions()
    {
        for (int i = 0; i < activeSessions.Count; i++)
        {
            PlaySession session = activeSessions[i];
            session.Cancelled = true;
            foreach (Coroutine tracked in session.Tracked)
                if (tracked != null) StopCoroutine(tracked);
            session.Tracked.Clear();
        }

        activeSessions.Clear();
    }

    // Disabling this object kills the tracked coroutines but not a caller-owned PlayAnimations,
    // so release those too. Deliberately does NOT call animation.Stop() — that starts a return
    // coroutine, which throws on an inactive GameObject.
    protected virtual void OnDisable()
    {
        CancelSessions();
    }
}