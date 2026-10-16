using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIImageSpriteChangeAnimation : AnimationBase
{
    [Header("UIImageSpriteChangeAnimation Settings")]
    [Tooltip("If null, the initial sprite will be used.")]
    public List<Sprite> frames = new List<Sprite>();
    public List<float> gapsBetweenFrames = new List<float>();
    public float defaultGapsBetweenFrames;
    public bool shuffleOrder;

    public Sprite initialSprite;

    public override void Awake()
    {
        if (ShouldCaptureInitialState())
            initialSprite = GetComponent<Image>()?.sprite;
    }

    public override void ReturnToInitialState()
    {
        var img = GetComponent<Image>();
        if (img) img.sprite = initialSprite;
    }

    public override IEnumerator AnimationCoroutine()
    {
        var img = GetComponent<Image>();
        if (!img)
        {
            yield return base.AnimationCoroutine();
            yield break;
        }

        // initialSprite = img.sprite;

        List<int> order = new List<int>(frames.Count);
        for (int i = 0; i < frames.Count; i++) order.Add(i);
        if (shuffleOrder)
        {
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
        }

        foreach (int i in order)
        {
            img.sprite = frames[i] != null ? frames[i] : initialSprite;
            float gap = (i < gapsBetweenFrames.Count) ? gapsBetweenFrames[i] : defaultGapsBetweenFrames;
            yield return new WaitForSeconds(gap);
        }

        yield return base.AnimationCoroutine();
    }

}
