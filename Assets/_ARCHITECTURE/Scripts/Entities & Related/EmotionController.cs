using System;
using UnityEngine;

public class EmotionController : MonoBehaviour
{
    private Unit unit;
    public GameObject emotionDisplayer;
    private SpriteRenderer sr;
    private SpriteChangeAnimation spriteChangeAnimation;

    public float healthThreshold = 0.5f;
    public Sprite angerEmotion;
    public Sprite lowHealthEmotion;
    
    private void Awake()
    {
        Sprite[] emotions = Resources.LoadAll<Sprite>("Sprites/Emotions");
        if (!lowHealthEmotion) lowHealthEmotion = emotions[1];
        if (!angerEmotion) angerEmotion = emotions[0];
        
        if (!emotionDisplayer) return;
        sr = emotionDisplayer.GetComponent<SpriteRenderer>();
        
        unit = GetComponent<Unit>();
        if (!unit) return;
        unit.onTakeDamage.AddListener(OnTakeDamage);
        
        spriteChangeAnimation =  emotionDisplayer.GetComponent<SpriteChangeAnimation>();
    }

    public void OnTakeDamage()
    {
        if (unit.stats.values["health"] < unit.stats.values["maxHealth"] * healthThreshold)
        {
            sr.sprite = lowHealthEmotion;
            spriteChangeAnimation.initialSprite = lowHealthEmotion;
        }
    }
}