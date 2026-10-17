using System;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>Which side of a card the player got: Hope after a call that went well, Hate after one that didn't.</summary>
public enum CardMood
{
    Hope,
    Hate,
}

public enum CardEffectType
{
    [Tooltip("+Amount points to every round's score.")]
    FlatPoints,
    [Tooltip("+Amount points when the dice make Combination.")]
    CombinationBonus,
    [Tooltip("The Combination's points are multiplied by Multiplier.")]
    CombinationMultiplier,
    [Tooltip("+Amount points for each die showing Face Value.")]
    FaceValueBonus,
    [Tooltip("+Amount rerolls every round.")]
    ExtraRerolls,
    [Tooltip("The whole score is multiplied by Multiplier.")]
    ScoreMultiplier,
}

/// <summary>What a card does to the dice. Hope and Hate versions of a card share it.</summary>
[Serializable]
public class CardEffect
{
    public CardEffectType type = CardEffectType.FlatPoints;
    public int amount = 3;
    [Min(0f)] public float multiplier = 1.5f;
    public PokerDiceCombination combination = PokerDiceCombination.OnePair;
    [Range(1, 6)] public int faceValue = 6;

    public bool Matches(PokerDiceCombination hand) => hand == combination;

    /// <summary>Plain description of the bonus, for cards without their own text (or for {bonus}).</summary>
    public string Describe()
    {
        switch (type)
        {
            case CardEffectType.FlatPoints: return $"+{amount} points";
            case CardEffectType.CombinationBonus: return $"+{amount} points for {Nicify(combination)}";
            case CardEffectType.CombinationMultiplier: return $"{Nicify(combination)} scores x{multiplier:0.##}";
            case CardEffectType.FaceValueBonus: return $"+{amount} points for each {faceValue}";
            case CardEffectType.ExtraRerolls: return amount == 1 ? "+1 reroll" : $"+{amount} rerolls";
            case CardEffectType.ScoreMultiplier: return $"Score x{multiplier:0.##}";
            default: return "";
        }
    }

    public static string Nicify(PokerDiceCombination combination) =>
        Regex.Replace(combination.ToString(), "(\\B[A-Z])", " $1");
}

/// <summary>
/// A bonus card. The bonus is the same whichever way the call went; only the name, text and art
/// change - Hope after a good call, Hate after a bad one. Descriptions may use {bonus} for the
/// bonus's plain description; an empty description shows just that.
/// </summary>
[CreateAssetMenu(fileName = "Card", menuName = "Funeral Song/Card")]
public class CardData : ScriptableObject
{
    [Header("Hope (after a good call)")]
    public string hopeTitle = "Hope";
    [TextArea] public string hopeDescription;
    public Sprite hopeArt;

    [Header("Hate (after a bad call)")]
    public string hateTitle = "Hate";
    [TextArea] public string hateDescription;
    public Sprite hateArt;

    [Header("Bonus (same for both)")]
    public CardEffect effect = new CardEffect();

    public string Title(CardMood mood) => mood == CardMood.Hope ? hopeTitle : hateTitle;

    public Sprite Art(CardMood mood) => mood == CardMood.Hope ? hopeArt : hateArt;

    public string Description(CardMood mood)
    {
        string text = mood == CardMood.Hope ? hopeDescription : hateDescription;
        string bonus = effect.Describe();
        return string.IsNullOrWhiteSpace(text) ? bonus : text.Replace("{bonus}", bonus);
    }
}

/// <summary>A card the player owns: which card, and which side of it they got.</summary>
[Serializable]
public class CardInstance
{
    public CardData data;
    public CardMood mood;

    public CardInstance(CardData data, CardMood mood)
    {
        this.data = data;
        this.mood = mood;
    }

    public CardEffect Effect => data ? data.effect : null;
    public string Title => data ? data.Title(mood) : "";
    public string Description => data ? data.Description(mood) : "";
    public Sprite Art => data ? data.Art(mood) : null;
}
