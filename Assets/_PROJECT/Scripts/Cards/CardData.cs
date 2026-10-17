using System;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>Which title a card shows: the good one after a call that went well, the bad one after one that didn't.</summary>
public enum CardMood
{
    Hope,
    Hate,
}

/// <summary>How a card's bonus works.</summary>
public enum CardKind
{
    [Tooltip("Kept: its bonus counts in every dice round, and it is shown with the player's cards.")]
    Permanent,
    [Tooltip("Applied once, the moment it is picked, then burned.")]
    Immediate,
}

/// <summary>A permanent card's bonus to the dice score.</summary>
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

/// <summary>An immediate card's one-off effect.</summary>
public enum ImmediateEffectType
{
    [Tooltip("Plays the song back by Seconds: more time left.")]
    RewindSong,
    [Tooltip("Nothing built in - react through CardManager.onImmediateCardApplied.")]
    Custom,
}

/// <summary>What a permanent card does to the dice.</summary>
[Serializable]
public class CardEffect
{
    public CardEffectType type = CardEffectType.FlatPoints;
    public int amount = 3;
    [Min(0f)] public float multiplier = 1.5f;
    public PokerDiceCombination combination = PokerDiceCombination.OnePair;
    [Range(1, 6)] public int faceValue = 6;

    public bool Matches(PokerDiceCombination hand) => hand == combination;

    /// <summary>Multiplier cards: their number is the multiplier, not an amount of points.</summary>
    public bool UsesMultiplier => type == CardEffectType.CombinationMultiplier || type == CardEffectType.ScoreMultiplier;

    /// <summary>Whether an upgrade changes this bonus - everything that adds points (not rerolls).</summary>
    public bool CanUpgrade => type != CardEffectType.ExtraRerolls;

    /// <summary>Points with <paramref name="scale"/> applied (1.5 = 50% more), rounded half up.</summary>
    public int AmountFor(float scale) => Mathf.FloorToInt(amount * scale + 0.5f);

    /// <summary>The multiplier with <paramref name="scale"/> applied to what it adds: x1.5 scaled 1.5 is x1.75.</summary>
    public float MultiplierFor(float scale) => 1f + (multiplier - 1f) * scale;

    /// <summary>The bonus's number as shown on the card - the amount, or the multiplier for multiplier cards.</summary>
    public string Value(float scale = 1f) => UsesMultiplier ? MultiplierFor(scale).ToString("0.##") : AmountFor(scale).ToString();

    /// <summary>Plain description of the bonus, for cards without their own text (or for {bonus}).</summary>
    public string Describe() => Describe(Value());

    /// <summary>The plain description with <paramref name="value"/> (the amount or multiplier, maybe highlighted) in it.</summary>
    public string Describe(string value)
    {
        switch (type)
        {
            case CardEffectType.FlatPoints: return $"+{value} points";
            case CardEffectType.CombinationBonus: return $"+{value} points for {Nicify(combination)}";
            case CardEffectType.CombinationMultiplier: return $"{Nicify(combination)} scores x{value}";
            case CardEffectType.FaceValueBonus: return $"+{value} points for each {faceValue}";
            case CardEffectType.ExtraRerolls: return amount == 1 ? $"+{value} reroll" : $"+{value} rerolls";
            case CardEffectType.ScoreMultiplier: return $"Score x{value}";
            default: return "";
        }
    }

    public static string Nicify(PokerDiceCombination combination) =>
        Regex.Replace(combination.ToString(), "(\\B[A-Z])", " $1");
}

/// <summary>What an immediate card does once.</summary>
[Serializable]
public class ImmediateEffect
{
    public ImmediateEffectType type = ImmediateEffectType.RewindSong;
    [Tooltip("RewindSong: seconds the song is played back.")]
    [Min(0f)] public float seconds = 10f;

    public string Value() => type == ImmediateEffectType.RewindSong ? seconds.ToString("0.#") : "";

    public string Describe() => Describe(Value());

    public string Describe(string value)
    {
        switch (type)
        {
            case ImmediateEffectType.RewindSong: return $"Play the song back {value} seconds";
            default: return "";
        }
    }
}

/// <summary>
/// A bonus card. <see cref="CardKind.Permanent"/> cards add to every dice round; <see cref="CardKind.Immediate"/>
/// ones act once when picked and burn. The card shows its good or bad title depending on how the call went;
/// the bonus is the same either way. The description may use {0} for the bonus's number (points, multiplier or
/// seconds - upgraded and highlighted when the card is) and {bonus} for its plain description; an empty
/// description shows just the plain one.
/// </summary>
[CreateAssetMenu(fileName = "Card", menuName = "Funeral Song/Card")]
public class CardData : ScriptableObject
{
    [Header("Look")]
    public string goodTitle = "Hope";
    public string badTitle = "Hate";
    [TextArea] public string description;
    [Tooltip("The picture in the middle of the card.")]
    public Sprite art;
    [Tooltip("Optional: this card's own prefab instead of the CardManager's Card Prefab.")]
    public CardView viewPrefab;

    [Header("Bonus")]
    public CardKind kind = CardKind.Permanent;
    [Tooltip("Permanent cards: the bonus to every dice round.")]
    public CardEffect effect = new CardEffect();
    [Tooltip("Immediate cards: what happens the moment it is picked.")]
    public ImmediateEffect immediate = new ImmediateEffect();

    public bool IsPermanent => kind == CardKind.Permanent;

    public string Title(CardMood mood) => mood == CardMood.Hope ? goodTitle : badTitle;

    public string BonusDescription => IsPermanent ? effect.Describe() : immediate.Describe();

    /// <summary>
    /// The card's text: {0} becomes the bonus's number with <paramref name="scale"/> applied (1.5 = an upgraded
    /// card's 50% more), tinted <paramref name="highlight"/> when given; {bonus} the plain description.
    /// </summary>
    public string Description(float scale = 1f, Color? highlight = null)
    {
        string value = IsPermanent ? effect.Value(scale) : immediate.Value();
        if (highlight.HasValue) value = $"<color=#{ColorUtility.ToHtmlStringRGBA(highlight.Value)}>{value}</color>";

        string bonus = IsPermanent ? effect.Describe(value) : immediate.Describe(value);
        string text = string.IsNullOrWhiteSpace(description) ? bonus : description.Replace("{bonus}", bonus);
        return text.Replace("{0}", value);
    }
}

/// <summary>
/// A card offered to or owned by the player: which card, which of its titles it shows, and whether it is
/// upgraded (offered after the player reached a round's risky goal: its points are worth more).
/// </summary>
[Serializable]
public class CardInstance
{
    public CardData data;
    public CardMood mood;
    [Tooltip("Extra share of points an upgraded card grants: 0.5 = 50% more. 0: not upgraded.")]
    public float upgradeBonus;

    public CardInstance(CardData data, CardMood mood, float upgradeBonus = 0f)
    {
        this.data = data;
        this.mood = mood;
        this.upgradeBonus = Mathf.Max(0f, upgradeBonus);
    }

    public bool IsPermanent => data && data.IsPermanent;
    /// <summary>The dice bonus - only for permanent cards; immediate ones never touch the score.</summary>
    public CardEffect Effect => IsPermanent ? data.effect : null;

    /// <summary>Upgraded, and the upgrade changes its bonus (permanent cards that add points).</summary>
    public bool IsUpgraded => upgradeBonus > 0f && IsPermanent && data.effect.CanUpgrade;

    /// <summary>What the bonus's number is scaled by: 1 + the upgrade bonus when upgraded, else 1.</summary>
    public float PointsScale => IsUpgraded ? 1f + upgradeBonus : 1f;

    /// <summary>The effect's amount, upgrade included.</summary>
    public int Amount => Effect != null ? Effect.AmountFor(PointsScale) : 0;

    /// <summary>The effect's multiplier, upgrade included.</summary>
    public float Multiplier => Effect != null ? Effect.MultiplierFor(PointsScale) : 1f;

    public string Title => data ? data.Title(mood) : "";
    public string Description => data ? data.Description(PointsScale) : "";
    public Sprite Art => data ? data.art : null;

    /// <summary>The description with an upgraded card's number tinted <paramref name="highlight"/>.</summary>
    public string DescriptionWith(Color highlight) =>
        data ? data.Description(PointsScale, IsUpgraded ? highlight : (Color?)null) : "";
}
