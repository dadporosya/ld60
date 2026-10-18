using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

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

    /// <summary>The sprite named <paramref name="spriteName"/> ("1".."6", "x", "odd"...) tinted <paramref name="color"/>.</summary>
    public static string Tinted(string spriteName, Color color) =>
        $"<color=#{ColorUtility.ToHtmlStringRGBA(color)}><sprite name=\"{spriteName}\" tint=1></color>";

    /// <summary>
    /// The dice a hand is made of: a pair "x x", two pair "x x y y", a full house "x x y y y", a small straight
    /// "1 2 3 4", a large one "1 2 3 4 5". Empty for <see cref="PokerDiceCombination.Nothing"/>.
    /// <paramref name="gap"/> goes between the icons (null: <see cref="Gap"/>).
    /// </summary>
    public static string Combination(PokerDiceCombination combination, string gap = null)
    {
        gap ??= Gap;
        switch (combination)
        {
            case PokerDiceCombination.OnePair: return Join(gap, X, X);
            case PokerDiceCombination.TwoPair: return Join(gap, X, X, Y, Y);
            case PokerDiceCombination.ThreeOfAKind: return Join(gap, X, X, X);
            case PokerDiceCombination.SmallStraight: return Join(gap, FaceIcons(1, 2, 3, 4));
            case PokerDiceCombination.LargeStraight: return Join(gap, FaceIcons(1, 2, 3, 4, 5));
            case PokerDiceCombination.FullHouse: return Join(gap, X, X, Y, Y, Y);
            case PokerDiceCombination.FourOfAKind: return Join(gap, X, X, X, X);
            case PokerDiceCombination.FiveOfAKind: return Join(gap, X, X, X, X, X);
            case PokerDiceCombination.TwoThreeOfAKind: return Join(gap, X, X, X, Y, Y, Y);
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
    /// particular dice (Always cards, rerolls).
    /// </summary>
    public static string ForEffect(CardEffect effect)
    {
        if (effect == null) return "";
        switch (effect.type)
        {
            case CardEffectType.Combination:
                // The biggest hand the card counts for: a straight card (small + large) shows 1 2 3 4 5.
                PokerDiceCombination shown = effect.combination;
                foreach (PokerDiceCombination other in effect.otherCombinations)
                    if (other > shown) shown = other;
                return Combination(shown);

            case CardEffectType.Pattern:
                return Pattern(effect.pattern);

            case CardEffectType.FaceValue:
            case CardEffectType.RerollFace:
            case CardEffectType.DestroyFace:
                return Face(effect.faceValue);

            case CardEffectType.FlipFace:
                return Face(effect.faceValue) + " / " + Face(effect.flipFace);

            case CardEffectType.Parity:
            case CardEffectType.AllParity:
                return Parity(effect.parity);

            case CardEffectType.GapStraight:
                return Row(FaceIcons(1, 2, 4, 5, 6));

            case CardEffectType.ExtraDice:
                return "+" + Repeat(X, effect.amount);

            default:
                return "";
        }
    }

    // Skipped as a whole: rich text tags and {placeholders}. Then a lone face digit (not part of a number, an
    // amount like +1 / -1, or a decimal), or the word odd / even.
    private static readonly Regex DiceWords = new Regex(
        @"<[^>]*>|\{[^}]*\}|(?<![\w+\-.])(?<face>[1-6])(?![\w%]|\.\d)|\b(?<parity>odd|even)\b",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// <paramref name="text"/> with the dice the card is about turned into icons: the faces of its face value,
    /// flip face or pattern ("Each 1 grants" - the 1 becomes a die), and "odd" / "even" on parity cards.
    /// Other numbers ("Lose 1 reroll" on a card about no face), combination names, tags and {placeholders}
    /// stay as they are.
    /// </summary>
    public static string InText(string text, CardEffect effect)
    {
        if (string.IsNullOrEmpty(text) || effect == null) return text;

        bool[] faces = new bool[PokerDiceEvaluator.Faces + 1];
        switch (effect.type)
        {
            case CardEffectType.FaceValue:
            case CardEffectType.RerollFace:
            case CardEffectType.DestroyFace:
                faces[effect.faceValue] = true;
                break;
            case CardEffectType.FlipFace:
                faces[effect.faceValue] = faces[effect.flipFace] = true;
                break;
            case CardEffectType.Pattern:
                foreach (char c in effect.pattern ?? "")
                    if (c >= '1' && c - '0' <= PokerDiceEvaluator.Faces) faces[c - '0'] = true;
                break;
        }
        bool parity = effect.type is CardEffectType.Parity or CardEffectType.AllParity;

        return DiceWords.Replace(text, match =>
        {
            Group face = match.Groups["face"];
            if (face.Success && faces[face.Value[0] - '0']) return Face(face.Value[0] - '0');

            Group word = match.Groups["parity"];
            if (word.Success && parity)
                return Parity(string.Equals(word.Value, "even", System.StringComparison.OrdinalIgnoreCase) ? DiceParity.Even : DiceParity.Odd);

            return match.Value;
        });
    }

    private static string[] FaceIcons(params int[] faces)
    {
        string[] icons = new string[faces.Length];
        for (int i = 0; i < faces.Length; i++) icons[i] = Face(faces[i]);
        return icons;
    }

    private static string Repeat(string icon, int count)
    {
        count = System.Math.Clamp(count, 1, 5);
        string[] icons = new string[count];
        for (int i = 0; i < count; i++) icons[i] = icon;
        return Row(icons);
    }

    private static string Row(params string[] icons) => Join(Gap, icons);

    private static string Join(string gap, params string[] icons)
    {
        StringBuilder text = new StringBuilder();
        foreach (string icon in icons)
        {
            if (text.Length > 0) text.Append(gap);
            text.Append(icon);
        }
        return text.ToString();
    }
}
