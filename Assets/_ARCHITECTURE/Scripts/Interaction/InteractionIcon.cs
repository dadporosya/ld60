using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class InteractionIcon : MonoBehaviour
{
    
    [SerializeField] private bool animate = true;
    
    [HideInInspector] public Vector3 origScale;
    [HideInInspector] public InteractionManager manager;
    [HideInInspector] public bool isInteracting=false;
    private Animator _animator;

    
    [SerializeField] private bool setZeroVectorOnStart = true;

    [HideInInspector] public int stack = 0;
    public bool affectedByStack = false;
    
    void Awake()
    {
        origScale = transform.localScale;
        if (!_animator) _animator = GetComponent<Animator>();
    }

    void Start()
    {
        if (setZeroVectorOnStart && animate) transform.localScale = Vector3.zero;
        if (!manager) manager = FindFirstObjectByType<InteractionManager>();
        manager.interactionIcons.Add(this);
        
        if (!isInteracting && _animator) _animator.enabled = false;
        if (animate) affectedByStack = animate;
    }

    public void StartInteractionAnim(Transform parent)
    {
        if (!animate)
        {
            // h.Out("Start interaction");
            // h.FadeIn(gameObject, 0, this);

            if (stack <= 0) gameObject.SetActive(true);
            stack++;
            return;
        }
        
        if (isInteracting)
        {
            InteractionIcon newIcon = manager.GetFreeIcon();
            Interactable pi = parent.GetComponent<Interactable>();
            pi.interactionIcon = newIcon;
            // pi.StartInteraction();
            newIcon.StartInteractionAnim(parent);
            return;
        }

        isInteracting = true;
        _animator.enabled = true;

        StopAllCoroutines();

        transform.SetParent(parent);
        transform.position = parent.position;
        transform.localScale = Vector3.zero;
        transform.localPosition = Vector3.zero;
        
        h.SmoothScaling(this, transform, origScale * manager.scaleMultiplier, manager.duration);
        h.SmoothTranslating(this, transform, transform.position + manager.translateVector, manager.duration);
    }

    public void EndInteractionAnim(Transform parent)
    {
        if (!animate)
        {
            // h.Out("End interaction");
            //
            // h.FadeOut(gameObject, 0, this);
            if (stack <= 1) gameObject.SetActive(false);
            stack--;
            return;
        }
        
        StopAllCoroutines();

        h.SmoothScaling(this, transform, Vector3.zero, manager.duration);
        h.SmoothTranslating(this, transform, transform.position - manager.translateVector, manager.duration);
        h.InvokeAfterTime(this, manager.duration, () =>
        {
            isInteracting = false;
            _animator.enabled = false;
            if (manager.interactionIcons.Count > 1)
            {
                manager.interactionIcons.Remove(this);
                Destroy(gameObject);
            }
        });
    }
}
