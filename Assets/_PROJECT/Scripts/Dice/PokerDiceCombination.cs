using System.Collections.Generic;

/// <summary>Poker dice hands. Values are serialized: append new ones at the end.</summary>
public enum PokerDiceCombination
{
    Nothing,
    OnePair,
    TwoPair,
    ThreeOfAKind,
    SmallStraight,
    LargeStraight,
    FullHouse,
    FourOfAKind,
    FiveOfAKind,
    /// <summary>Two different faces three times each, e.g. 2 2 2 5 5 5 (needs 6 dice). Six of a kind counts too.</summary>
    TwoThreeOfAKind,
    /// <summary>
    /// A 1 or a 3 among the dice (<see cref="PokerDiceEvaluator.SafeFaces"/>). Worth no points, but a reroll whose
    /// rolled dice show one doesn't bust - the round's points are safe. Ranks just above <see cref="Nothing"/>.
    /// </summary>
    SafeFace,
}

/// <summary>Which poker dice hands a set of dice values contains.</summary>
public static class PokerDiceEvaluator
{
    public const int Faces = 6;

    /// <summary>The faces that make <see cref="PokerDiceCombination.SafeFace"/>: any one of them saves a reroll from busting.</summary>
    public static readonly int[] SafeFaces = { 1, 3 };

    /// <summary>
    /// Every hand <paramref name="values"/> (1..6) contains - a full house also counts as a pair and
    /// three of a kind, so a card boosting a lower hand can still make it the best choice.
    /// <see cref="PokerDiceCombination.Nothing"/> is always included. A straight may skip up to
    /// <paramref name="straightGaps"/> missing faces (1: 1 2 4 5 6 is a large straight, 1 2 4 5 a small one).
    /// </summary>
    public static List<PokerDiceCombination> Contained(IReadOnlyList<int> values, int straightGaps = 0)
    {
        int[] counts = new int[Faces + 1];
        foreach (int v in values)
            if (v >= 1 && v <= Faces) counts[v]++;

        int max = 0, pairs = 0, triples = 0;
        bool hasThree = false, hasTwo = false;
        for (int f = 1; f <= Faces; f++)
        {
            max = System.Math.Max(max, counts[f]);
            if (counts[f] >= 2) pairs++;
            if (counts[f] >= 3) triples++;
            if (counts[f] == 3) hasThree = true;
            if (counts[f] == 2) hasTwo = true;
        }

        int run = LongestRun(counts, straightGaps);

        List<PokerDiceCombination> result = new List<PokerDiceCombination> { PokerDiceCombination.Nothing };
        if (max >= 2) result.Add(PokerDiceCombination.OnePair);
        if (pairs >= 2 || max >= 4) result.Add(PokerDiceCombination.TwoPair);
        if (max >= 3) result.Add(PokerDiceCombination.ThreeOfAKind);
        if (run >= 4) result.Add(PokerDiceCombination.SmallStraight);
        if (run >= 5) result.Add(PokerDiceCombination.LargeStraight);
        if ((hasThree && hasTwo) || max >= 5) result.Add(PokerDiceCombination.FullHouse);
        if (max >= 4) result.Add(PokerDiceCombination.FourOfAKind);
        if (max >= 5) result.Add(PokerDiceCombination.FiveOfAKind);
        if (triples >= 2 || max >= 6) result.Add(PokerDiceCombination.TwoThreeOfAKind);
        foreach (int face in SafeFaces)
            if (counts[face] >= 1)
            {
                result.Add(PokerDiceCombination.SafeFace);
                break;
            }
        return result;
    }

    /// <summary>
    /// How hands order when they score the same: higher wins. The enum order, but <see cref="PokerDiceCombination.SafeFace"/>
    /// (appended last) sits just above <see cref="PokerDiceCombination.Nothing"/>.
    /// </summary>
    public static int Rank(PokerDiceCombination hand) => hand switch
    {
        PokerDiceCombination.Nothing => 0,
        PokerDiceCombination.SafeFace => 1,
        _ => (int)hand + 1,
    };

    /// <summary>The most faces shown in a row, skipping at most <paramref name="gaps"/> missing faces inside it.</summary>
    private static int LongestRun(int[] counts, int gaps)
    {
        int best = 0;
        for (int start = 1; start <= Faces; start++)
        {
            if (counts[start] == 0) continue;
            int shown = 0, missing = 0;
            for (int f = start; f <= Faces; f++)
            {
                if (counts[f] > 0) shown++;
                else if (++missing > gaps) break;
                if (shown > best) best = shown;
            }
        }
        return best;
    }
}
