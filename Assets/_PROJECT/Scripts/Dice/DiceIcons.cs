using System.Collections.Generic;
using System.Text;

/// <summary>
/// TMP sprite tags for the dice icons in the default sprite asset (DiceFaces): faces 1..6, "x" and "y"
/// (any face - the same letter is the same face), "even" and "odd". Used for the cards' icons and the
/// hint paper's combinations.
/// </summary>
public static class DiceIcons
{
    public const string X = "<sprite name=\"x\">";
    public const string Y = "<sprite name=\"y\">";
    public const string Even = "<sprite name=\"even\">";
    public const string Odd = "<sprite name=\"odd\">";

    /// <summary>Between icons in a row.</summary>
    public const string Gap = " ";

    /// <summary>The die showing <paramref name="face"/> (1..6).</summary>
    public static string Face(int face) => $"<sprite name=\"{face}\">";

    public static string Parity(DiceParity parity) => parity == DiceParity.Even ? Even : Odd;

    /// <summary>
    /// The dice a hand is made of: a pair "x x", two pair "x x y y", a full house "x x y y y", a small straight
    /// "1 2 3 4", a large one "1 2 3 4 5". Empty for <see cref="PokerDiceCombination.Nothing"/>.
    /// </summary>
    public static string Combination(PokerDiceCombination combination)
    {
        switch (combination)
        {
            case PokerDiceCombination.OnePair: return Row(X, X);
            case PokerDiceCombination.TwoPair: return Row(X, X, Y, Y);
            case PokerDiceCombination.ThreeOfAKind: return Row(X, X, X);
            case PokerDiceCombination.SmallStraight: return Faces(1, 2, 3, 4);
            case PokerDiceCombination.LargeStraight: return Faces(1, 2, 3, 4, 5);
            case PokerDiceCombination.FullHouse: return Row(X, X, Y, Y, Y);
            case PokerDiceCombination.FourOfAKind: return Row(X, X, X, X);
            case PokerDiceCombination.FiveOfAKind: return Row(X, X, X, X, X);
            default: return "";
        }
    }

    /// <summary>A pattern's faces, e.g. "1 1 1" or "1 6". Anything but 1..6 is skipped.</summary>
    public static string Pattern(string pattern)
    {
        List<string> icons = new List<string>();
        foreach (char c in pattern ?? "")
        {
            int face = c - '0';
            if (face >= 1 && face <= PokerDiceEvaluator.Faces) icons.Add(Face(face));
        }
        return Row(icons.ToArray());
    }

    /// <summary>
    /// The dice a card's bonus is about: its combination, pattern, face or parity. Empty when it isn't about
    /// particular dice (flat points, score multiplier, rerolls).
    /// </summary>
    public static string ForEffect(CardEffect effect)
    {
        if (effect == null) return "";
        switch (effect.type)
        {
            case CardEffectType.CombinationBonus:
            case CardEffectType.CombinationMultiplier:
                // The biggest hand the card counts for: a straight card (small + large) shows 1 2 3 4 5.
                PokerDiceCombination shown = effect.combination;
                foreach (PokerDiceCombination other in effect.otherCombinations)
                    if (other > shown) shown = other;
                return Combination(shown);

            case CardEffectType.PatternBonus:
            case CardEffectType.PatternMultiplier:
                return Pattern(effect.pattern);

            case CardEffectType.FaceValueBonus:
            case CardEffectType.RerollFace:
            case CardEffectType.DestroyFace:
                return Face(effect.faceValue);

            case CardEffectType.FlipFace:
                return Face(effect.faceValue) + " / " + Face(effect.flipFace);

            case CardEffectType.ParityBonus:
            case CardEffectType.ParityMultiplier:
                return Parity(effect.parity);

            case CardEffectType.ExtraDice:
                return "+" + Repeat(X, effect.amount);

            default:
                return "";
        }
    }

    private static string Faces(params int[] faces)
    {
        string[] icons = new string[faces.Length];
        for (int i = 0; i < faces.Length; i++) icons[i] = Face(faces[i]);
        return Row(icons);
    }

    private static string Repeat(string icon, int count)
    {
        count = System.Math.Clamp(count, 1, 5);
        string[] icons = new string[count];
        for (int i = 0; i < count; i++) icons[i] = icon;
        return Row(icons);
    }

    private static string Row(params string[] icons)
    {
        StringBuilder text = new StringBuilder();
        foreach (string icon in icons)
        {
            if (text.Length > 0) text.Append(Gap);
            text.Append(icon);
        }
        return text.ToString();
    }
}
