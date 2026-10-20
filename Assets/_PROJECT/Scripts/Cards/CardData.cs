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

/// <summary>
/// When a permanent card counts, or what else it does. The scoring ones apply the card's
/// <see cref="ScoreOperation"/>. Values are serialized: keep the numbers, append new ones at the end.
/// </summary>
public enum CardEffectType
{
    [Tooltip("Every round.")]
    Always = 0,
    [Tooltip("When the dice make Combination (or one of Other Combinations).")]
    Combination = 1,
    [Tooltip("Once for each die showing Face Value.")]
    FaceValue = 3,
    [Tooltip("+Amount rerolls every round.")]
    ExtraRerolls = 4,
    [Tooltip("When the dice show Pattern (\"1 1\": two 1s, \"1 6\": a 1 and a 6).")]
    Pattern = 6,
    [Tooltip("Once for each die that is Parity (odd / even).")]
    Parity = 8,
    [Tooltip("When every die is Parity (odd / even).")]
    AllParity = 9,
    [Tooltip("+Amount dice on the table every round, and Reroll Cost rerolls fewer.")]
    ExtraDice = 10,
    [Tooltip("Right click a die showing Face Value: it is rerolled alone, for free, as often as the player likes.")]
    RerollFace = 11,
    [Tooltip("Right click a die showing Face Value: it is removed for the rest of the round, gives +Amount rerolls " +
             "and +Multiplier mult for the round.")]
    DestroyFace = 12,
    [Tooltip("Right click a die showing Face Value to turn it into Flip Face, and back - as often as the player likes.")]
    FlipFace = 13,
    [Tooltip("Straights may skip Amount missing faces: 1 2 4 5 6 is a large straight, 1 2 4 5 a small one.")]
    GapStraight = 14,
    [Tooltip("When more dice are Parity (odd / even) than not.")]
    MostParity = 15,
}

/// <summary>
/// What a scoring card does to the round's score, which is points x mult (points start at the hand's points plus
/// the dice sum, mult at 1). Applied in this order over all cards: added points, multiplied points, added mult,
/// multiplied mult. Values are serialized: append new ones at the end.
/// </summary>
public enum ScoreOperation
{
    [Tooltip("+Amount points.")]
    AddPoints,
    [Tooltip("+Multiplier mult.")]
    AddMult,
    [Tooltip("Mult x Multiplier (the whole score x Multiplier).")]
    MultiplyMult,
    [Tooltip("Points x Multiplier.")]
    MultiplyPoints,
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
    public CardEffectType type = CardEffectType.Always;
    [Tooltip("Scoring cards (Always, Combination, Face Value, Pattern, Parity, All Parity): what they do to the score.")]
    public ScoreOperation operation = ScoreOperation.AddPoints;
    [Tooltip("Points (Add Points), rerolls (Extra Rerolls, Destroy Face), dice (Extra Dice) or gaps (Gap Straight).")]
    public int amount = 3;
    [Tooltip("Mult added (Add Mult, Destroy Face) or what mult / points are multiplied by (Multiply Mult / Points).")]
    [Min(0f)] public float multiplier = 1.5f;
    [Tooltip("Scoring cards: rerolls added to the next dice round (the next call's, after a turn's last round) " +
             "whenever the card counts in a finished round.")]
    [Min(0)] public int nextRoundRerolls;
    public PokerDiceCombination combination = PokerDiceCombination.OnePair;
    [Tooltip("Combination cards: more hands the card counts for - e.g. Large Straight next to Small Straight.")]
    public List<PokerDiceCombination> otherCombinations = new List<PokerDiceCombination>();
    [Range(1, 6)] public int faceValue = 6;
    [Tooltip("Pattern cards: the faces the dice must show, e.g. \"1 1\" (two 1s), \"6 6 6\", \"1 6\" (a 1 and a 6).")]
    public string pattern = "1 1";
    [Tooltip("Pattern cards: count once for each time the dice show the pattern (\"2 2\" twice for 2 2 2 2), not just once.")]
    public bool eachPattern;
    [Tooltip("Parity cards: which dice count.")]
    public DiceParity parity = DiceParity.Odd;
    [Tooltip("Extra Dice: rerolls lost every round.")]
    [Min(0)] public int rerollCost = 1;
    [Tooltip("Flip Face: what Face Value turns into (and back).")]
    [Range(1, 6)] public int flipFace = 5;

    public bool Matches(PokerDiceCombination hand) => hand == combination || otherCombinations.Contains(hand);

    /// <summary>Cards that change the score, through their <see cref="operation"/>.</summary>
    public bool IsScoring => type is CardEffectType.Always or CardEffectType.Combination or CardEffectType.FaceValue
        or CardEffectType.Pattern or CardEffectType.Parity or CardEffectType.AllParity or CardEffectType.MostParity;

    /// <summary>Mult / multiplier cards: their number is <see cref="multiplier"/>, not an amount.</summary>
    public bool UsesMultiplier => IsScoring && operation != ScoreOperation.AddPoints;

    /// <summary>Cards used by right clicking a die (<see cref="ActsOn"/> says which).</summary>
    public bool IsDieAction => type is CardEffectType.RerollFace or CardEffectType.DestroyFace or CardEffectType.FlipFace;

    /// <summary>Whether an upgrade changes this bonus - the scoring cards (not rerolls, dice or die actions).</summary>
    public bool CanUpgrade => IsScoring;

    /// <summary>
    /// How many times the card counts for <paramref name="values"/> making <paramref name="hand"/>: once or not at
    /// all, or once per matching die for Face Value and Parity (once per copy of the pattern for Each Pattern cards).
    /// 0 for cards that don't score.
    /// </summary>
    public int TimesIn(IReadOnlyList<int> values, PokerDiceCombination hand)
    {
        switch (type)
        {
            case CardEffectType.Always: return 1;
            case CardEffectType.Combination: return Matches(hand) ? 1 : 0;
            case CardEffectType.Pattern: return eachPattern ? PatternCount(values) : PatternIn(values) ? 1 : 0;
            case CardEffectType.FaceValue:
            case CardEffectType.Parity:
                int count = 0;
                foreach (int v in values)
                    if (type == CardEffectType.FaceValue ? v == faceValue : IsOfParity(v)) count++;
                return count;
            case CardEffectType.AllParity:
                if (values.Count == 0) return 0;
                foreach (int v in values)
                    if (!IsOfParity(v)) return 0;
                return 1;
            case CardEffectType.MostParity:
                int matching = 0;
                foreach (int v in values)
                    if (IsOfParity(v)) matching++;
                return matching * 2 > values.Count ? 1 : 0;
            default: return 0;
        }
    }

    /// <summary>Die actions: whether this card can be used on a die showing <paramref name="face"/>.</summary>
    public bool ActsOn(int face) =>
        IsDieAction && (face == faceValue || (type == CardEffectType.FlipFace && face == flipFace));

    /// <summary>Flip Face: the face <paramref name="face"/> turns into.</summary>
    public int FlipOf(int face) => face == faceValue ? flipFace : faceValue;

    public bool IsOfParity(int face) => parity == DiceParity.Odd ? face % 2 == 1 : face % 2 == 0;

    /// <summary>Pattern cards: whether <paramref name="values"/> show every face of <see cref="pattern"/> (as many times as it has it).</summary>
    public bool PatternIn(IReadOnlyList<int> values) => PatternCount(values) > 0;

    /// <summary>Pattern cards: how many times <paramref name="values"/> show <see cref="pattern"/>, no die used twice.</summary>
    public int PatternCount(IReadOnlyList<int> values)
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
        if (!any) return 0;

        int[] have = new int[PokerDiceEvaluator.Faces + 1];
        foreach (int v in values)
            if (v >= 1 && v <= PokerDiceEvaluator.Faces) have[v]++;
        int times = int.MaxValue;
        for (int f = 1; f <= PokerDiceEvaluator.Faces; f++)
            if (need[f] > 0) times = Mathf.Min(times, have[f] / need[f]);
        return times;
    }

    /// <summary>Points with <paramref name="scale"/> applied (1.5 = 50% more), rounded half up.</summary>
    public int AmountFor(float scale) => Mathf.FloorToInt(amount * scale + 0.5f);

    /// <summary>
    /// The multiplier with <paramref name="scale"/> applied to what it adds: +0.5 mult scaled 1.5 is +0.75,
    /// x1.5 scaled 1.5 is x1.75.
    /// </summary>
    public float MultiplierFor(float scale) =>
        AddsMult ? multiplier * scale : 1f + (multiplier - 1f) * scale;

    private bool AddsMult => type == CardEffectType.DestroyFace || operation == ScoreOperation.AddMult;

    /// <summary>The bonus's number as shown on the card - the amount, or the multiplier for mult / multiplier cards.</summary>
    public string Value(float scale = 1f) => UsesMultiplier ? FormatMult(MultiplierFor(scale)) : AmountFor(scale).ToString();

    /// <summary>The multiplier as shown on the card (the {mult} of a description).</summary>
    public string MultValue(float scale = 1f) => FormatMult(MultiplierFor(scale));

    public static string FormatMult(float mult) => mult.ToString("0.##");

    /// <summary>Plain description of the bonus, for cards without their own text (or for {bonus}).</summary>
    public string Describe() => Describe(Value());

    /// <summary>The plain description with <paramref name="value"/> (the amount or multiplier, maybe highlighted) in it.</summary>
    public string Describe(string value)
    {
        string text = IsScoring ? DescribeScoring(Operation(value)) : DescribeOther(value);
        if (IsScoring && nextRoundRerolls > 0)
            text += nextRoundRerolls == 1 ? Loc.F(", +{0} reroll next round", nextRoundRerolls)
                : Loc.F(", +{0} rerolls next round", nextRoundRerolls);
        return text;
    }

    private string DescribeScoring(string operationText)
    {
        switch (type)
        {
            case CardEffectType.Always: return operationText;
            case CardEffectType.Combination: return $"{Combinations}: {operationText}";
            case CardEffectType.FaceValue: return Loc.F("{0} for each {1}", operationText, faceValue);
            case CardEffectType.Pattern:
                return eachPattern ? Loc.F("{0} for each {1}", operationText, pattern) : $"{pattern}: {operationText}";
            case CardEffectType.Parity:
                return parity == DiceParity.Odd ? Loc.F("{0} for each odd die", operationText)
                    : Loc.F("{0} for each even die", operationText);
            case CardEffectType.AllParity:
                return parity == DiceParity.Odd ? Loc.F("{0} if every die is odd", operationText)
                    : Loc.F("{0} if every die is even", operationText);
            case CardEffectType.MostParity:
                return parity == DiceParity.Odd ? Loc.F("{0} if more dice are odd than even", operationText)
                    : Loc.F("{0} if more dice are even than odd", operationText);
            default: return "";
        }
    }

    private string DescribeOther(string value)
    {
        switch (type)
        {
            case CardEffectType.ExtraRerolls: return amount == 1 ? Loc.F("+{0} reroll", value) : Loc.F("+{0} rerolls", value);
            case CardEffectType.ExtraDice:
                return (amount == 1 ? Loc.F("+{0} die", value) : Loc.F("+{0} dice", value)) + ", " +
                       (rerollCost == 1 ? Loc.F("-{0} reroll", rerollCost) : Loc.F("-{0} rerolls", rerollCost));
            case CardEffectType.RerollFace: return Loc.F("Right click a {0} to reroll it, any number of times", faceValue);
            case CardEffectType.DestroyFace:
                string text = amount == 1 ? Loc.F("Right click a {0} to destroy it: +{1} reroll", faceValue, value)
                    : Loc.F("Right click a {0} to destroy it: +{1} rerolls", faceValue, value);
                return multiplier > 0f ? text + ", " + Loc.F("+{0} mult", MultValue()) : text;
            case CardEffectType.FlipFace:
                return Loc.F("Right click a {0} to flip it to {1} and back, any number of times", faceValue, flipFace);
            case CardEffectType.GapStraight:
                return amount == 1 ? Loc.F("Straights may have {0} gap", value) : Loc.F("Straights may have {0} gaps", value);
            default: return "";
        }
    }

    private string Operation(string value)
    {
        switch (operation)
        {
            case ScoreOperation.AddPoints: return Loc.F("+{0} points", value);
            case ScoreOperation.AddMult: return Loc.F("+{0} mult", value);
            case ScoreOperation.MultiplyMult: return Loc.F("x{0} mult", value);
            case ScoreOperation.MultiplyPoints: return Loc.F("x{0} points", value);
            default: return "";
        }
    }

    private string Combinations
    {
        get
        {
            string text = Loc.T(Nicify(combination));
            foreach (PokerDiceCombination other in otherCombinations)
                if (other != combination) text += " / " + Loc.T(Nicify(other));
            return text;
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
            case ImmediateEffectType.RewindSong: return Loc.F("Play the song back {0} seconds", value);
            default: return "";
        }
    }
}

/// <summary>
/// A bonus card. <see cref="CardKind.Permanent"/> cards add to every dice round; <see cref="CardKind.Immediate"/>
/// ones act once when picked and burn. The card shows its good or bad title depending on how the call went;
/// the bonus is the same either way. The description may use {0} for the bonus's number (points, mult, rerolls or
/// seconds - upgraded and highlighted when the card is), {mult} for the effect's multiplier (a Destroy Face card's
/// mult), {next} for its next-round rerolls and {bonus} for its plain description; an empty description shows
/// just the plain one.
/// </summary>
[CreateAssetMenu(fileName = "Card", menuName = "Funeral Song/Card")]
public class CardData : ScriptableObject
{
    [Header("Look")]
    [Localized] public string goodTitle = "Hope";
    [Localized] public string badTitle = "Hate";
    [TextArea, Localized] public string description;
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
    /// card's 50% more), tinted <paramref name="highlight"/> when given; {bonus} the plain description. The dice
    /// the card is about show as icons (<see cref="DiceIcons.InText"/>) - worked out before the number goes in,
    /// so the number never becomes a die.
    /// </summary>
    public string Description(float scale = 1f, Color? highlight = null)
    {
        string value = IsPermanent ? effect.Value(scale) : immediate.Value();
        if (highlight.HasValue) value = $"<color=#{ColorUtility.ToHtmlStringRGBA(highlight.Value)}>{value}</color>";

        string bonus = IsPermanent ? effect.Describe("{0}") : immediate.Describe("{0}");
        // Translated before the dice icons and numbers go in: the table holds the description as written.
        string text = string.IsNullOrWhiteSpace(description) ? bonus : Loc.T(description).Replace("{bonus}", bonus);
        if (!IsPermanent) return text.Replace("{0}", value);

        text = DiceIcons.InText(text, effect);
        return text.Replace("{0}", value)
            .Replace("{mult}", effect.MultValue(scale))
            .Replace("{next}", effect.nextRoundRerolls.ToString());
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
    [Tooltip("The title shown instead of the card's own (one from the level's list for how the round went). Empty: the card's own.")]
    public string title;

    public CardInstance(CardData data, CardMood mood, float upgradeBonus = 0f, string title = null)
    {
        this.data = data;
        this.mood = mood;
        this.upgradeBonus = Mathf.Max(0f, upgradeBonus);
        this.title = title;
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

    public string Title => !string.IsNullOrEmpty(title) ? title : data ? data.Title(mood) : "";
    public string Description => data ? data.Description(PointsScale) : "";
    public string Icon => data ? data.Icon : "";

    /// <summary>The description with an upgraded card's number tinted <paramref name="highlight"/>.</summary>
    public string DescriptionWith(Color highlight) =>
        data ? data.Description(PointsScale, IsUpgraded ? highlight : (Color?)null) : "";
}
