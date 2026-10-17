using System;
using System.Collections.Generic;
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

/// <summary>A permanent card's bonus to the dice score. Values are serialized: append new ones at the end.</summary>
public enum CardEffectType
{
    [Tooltip("+Amount points to every round's score.")]
    FlatPoints,
    [Tooltip("+Amount points when the dice make Combination (or one of Other Combinations).")]
    CombinationBonus,
    [Tooltip("The Combination's (or one of Other Combinations') bonus points are multiplied by Multiplier. The dice sum isn't.")]
    CombinationMultiplier,
    [Tooltip("+Amount points for each die showing Face Value.")]
    FaceValueBonus,
    [Tooltip("+Amount rerolls every round.")]
    ExtraRerolls,
    [Tooltip("The whole score is multiplied by Multiplier.")]
    ScoreMultiplier,
    [Tooltip("+Amount points when the dice show Pattern (\"1 1\": two 1s, \"1 6\": a 1 and a 6).")]
    PatternBonus,
    [Tooltip("When the dice show Pattern, the combination's bonus points are multiplied by Multiplier. The dice sum isn't.")]
    PatternMultiplier,
    [Tooltip("+Amount points for each die that is Parity (odd / even).")]
    ParityBonus,
    [Tooltip("The whole score is multiplied by Multiplier when every die is Parity (odd / even).")]
    ParityMultiplier,
    [Tooltip("+Amount dice on the table every round, and Reroll Cost rerolls fewer.")]
    ExtraDice,
    [Tooltip("Right click a die showing Face Value: it is rerolled alone, for free, as often as the player likes.")]
    RerollFace,
    [Tooltip("Right click a die showing Face Value: it is removed for the rest of the round and gives +Amount rerolls.")]
    DestroyFace,
    [Tooltip("Right click a die showing Face Value to turn it into Flip Face, and back - as often as the player likes.")]
    FlipFace,
}

/// <summary>Odd or even faces, for the parity cards.</summary>
public enum DiceParity
{
    Odd,
    Even,
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
    [Tooltip("Combination cards: more hands the card counts for - e.g. Large Straight next to Small Straight.")]
    public List<PokerDiceCombination> otherCombinations = new List<PokerDiceCombination>();
    [Range(1, 6)] public int faceValue = 6;
    [Tooltip("Pattern cards: the faces the dice must show, e.g. \"1 1\" (two 1s), \"6 6 6\", \"1 6\" (a 1 and a 6).")]
    public string pattern = "1 1";
    [Tooltip("Parity cards: which dice count.")]
    public DiceParity parity = DiceParity.Odd;
    [Tooltip("Extra Dice: rerolls lost every round.")]
    [Min(0)] public int rerollCost = 1;
    [Tooltip("Flip Face: what Face Value turns into (and back).")]
    [Range(1, 6)] public int flipFace = 5;

    public bool Matches(PokerDiceCombination hand) => hand == combination || otherCombinations.Contains(hand);

    /// <summary>Multiplier cards: their number is the multiplier, not an amount of points.</summary>
    public bool UsesMultiplier => type is CardEffectType.CombinationMultiplier or CardEffectType.ScoreMultiplier
        or CardEffectType.PatternMultiplier or CardEffectType.ParityMultiplier;

    /// <summary>Cards used by right clicking a die (<see cref="ActsOn"/> says which).</summary>
    public bool IsDieAction => type is CardEffectType.RerollFace or CardEffectType.DestroyFace or CardEffectType.FlipFace;

    /// <summary>Whether an upgrade changes this bonus - everything that adds points (not rerolls, dice or die actions).</summary>
    public bool CanUpgrade => !IsDieAction && type is not (CardEffectType.ExtraRerolls or CardEffectType.ExtraDice);

    /// <summary>Die actions: whether this card can be used on a die showing <paramref name="face"/>.</summary>
    public bool ActsOn(int face) =>
        IsDieAction && (face == faceValue || (type == CardEffectType.FlipFace && face == flipFace));

    /// <summary>Flip Face: the face <paramref name="face"/> turns into.</summary>
    public int FlipOf(int face) => face == faceValue ? flipFace : faceValue;

    public bool IsOfParity(int face) => parity == DiceParity.Odd ? face % 2 == 1 : face % 2 == 0;

    /// <summary>Pattern cards: whether <paramref name="values"/> show every face of <see cref="pattern"/> (as many times as it has it).</summary>
    public bool PatternIn(IReadOnlyList<int> values)
    {
        int[] need = new int[PokerDiceEvaluator.Faces + 1];
        bool any = false;
        foreach (char c in pattern ?? "")
        {
            int face = c - '0';
            if (face < 1 || face > PokerDiceEvaluator.Faces) continue;
            need[face]++;
            any = true;
        }
        if (!any) return false;

        int[] have = new int[PokerDiceEvaluator.Faces + 1];
        foreach (int v in values)
            if (v >= 1 && v <= PokerDiceEvaluator.Faces) have[v]++;
        for (int f = 1; f <= PokerDiceEvaluator.Faces; f++)
            if (have[f] < need[f]) return false;
        return true;
    }

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
            case CardEffectType.CombinationBonus: return $"+{value} points for {Combinations}";
            case CardEffectType.CombinationMultiplier: return $"{Combinations} scores x{value}";
            case CardEffectType.FaceValueBonus: return $"+{value} points for each {faceValue}";
            case CardEffectType.ExtraRerolls: return amount == 1 ? $"+{value} reroll" : $"+{value} rerolls";
            case CardEffectType.ScoreMultiplier: return $"Score x{value}";
            case CardEffectType.PatternBonus: return $"{pattern} grants +{value} points";
            case CardEffectType.PatternMultiplier: return $"{pattern} multiplies the combination's points by {value}";
            case CardEffectType.ParityBonus: return $"+{value} points for each {Parity} die";
            case CardEffectType.ParityMultiplier: return $"Score x{value} if every die is {Parity}";
            case CardEffectType.ExtraDice:
                return $"+{value} {(amount == 1 ? "die" : "dice")}, -{rerollCost} {(rerollCost == 1 ? "reroll" : "rerolls")}";
            case CardEffectType.RerollFace: return $"Right click a {faceValue} to reroll it, any number of times";
            case CardEffectType.DestroyFace:
                return $"Right click a {faceValue} to destroy it: +{value} {(amount == 1 ? "reroll" : "rerolls")}";
            case CardEffectType.FlipFace: return $"Right click a {faceValue} to flip it to {flipFace} and back, any number of times";
            default: return "";
        }
    }

    private string Combinations
    {
        get
        {
            string text = Nicify(combination);
            foreach (PokerDiceCombination other in otherCombinations)
                if (other != combination) text += " / " + Nicify(other);
            return text;
        }
    }

    private string Parity => parity == DiceParity.Odd ? "odd" : "even";

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
    [Tooltip("Optional: what the icon in the middle of the card shows (TMP rich text, <sprite name=\"1\"> etc. allowed). " +
             "Empty: the dice the bonus is about - its combination, pattern, face or parity.")]
    [TextArea] public string icon;
    [Tooltip("Optional: this card's own prefab instead of the CardManager's Card Prefab.")]
    public CardView viewPrefab;

    [Header("Deck")]
    [Tooltip("Copies of this card in the deck: the more, the likelier it is offered. A permanent card can be owned " +
             "this many times at most - unless the CardManager allows owned permanent cards to be offered again.")]
    [Min(1)] public int count = 1;

    [Header("Bonus")]
    public CardKind kind = CardKind.Permanent;
    [Tooltip("Permanent cards: the bonus to every dice round.")]
    public CardEffect effect = new CardEffect();
    [Tooltip("Immediate cards: what happens the moment it is picked.")]
    public ImmediateEffect immediate = new ImmediateEffect();

    public bool IsPermanent => kind == CardKind.Permanent;

    public string Title(CardMood mood) => mood == CardMood.Hope ? goodTitle : badTitle;

    public string BonusDescription => IsPermanent ? effect.Describe() : immediate.Describe();

    /// <summary>The card's icon: <see cref="icon"/>, or the dice icons of its bonus (<see cref="DiceIcons.ForEffect"/>).</summary>
    public string Icon => !string.IsNullOrWhiteSpace(icon) ? icon : IsPermanent ? DiceIcons.ForEffect(effect) : "";

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
    public string Icon => data ? data.Icon : "";

    /// <summary>The description with an upgraded card's number tinted <paramref name="highlight"/>.</summary>
    public string DescriptionWith(Color highlight) =>
        data ? data.Description(PointsScale, IsUpgraded ? highlight : (Color?)null) : "";
}
