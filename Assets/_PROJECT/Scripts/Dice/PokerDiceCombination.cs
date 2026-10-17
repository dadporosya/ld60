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
}

/// <summary>Which poker dice hands a set of dice values contains.</summary>
public static class PokerDiceEvaluator
{
    public const int Faces = 6;

    /// <summary>
    /// Every hand <paramref name="values"/> (1..6) contains - a full house also counts as a pair and
    /// three of a kind, so a card boosting a lower hand can still make it the best choice.
    /// <see cref="PokerDiceCombination.Nothing"/> is always included.
    /// </summary>
    public static List<PokerDiceCombination> Contained(IReadOnlyList<int> values)
    {
        int[] counts = new int[Faces + 1];
        foreach (int v in values)
            if (v >= 1 && v <= Faces) counts[v]++;

        int max = 0, pairs = 0;
        bool hasThree = false, hasTwo = false;
        for (int f = 1; f <= Faces; f++)
        {
            max = System.Math.Max(max, counts[f]);
            if (counts[f] >= 2) pairs++;
            if (counts[f] == 3) hasThree = true;
            if (counts[f] == 2) hasTwo = true;
        }

        int run = LongestRun(counts);

        List<PokerDiceCombination> result = new List<PokerDiceCombination> { PokerDiceCombination.Nothing };
        if (max >= 2) result.Add(PokerDiceCombination.OnePair);
        if (pairs >= 2 || max >= 4) result.Add(PokerDiceCombination.TwoPair);
        if (max >= 3) result.Add(PokerDiceCombination.ThreeOfAKind);
        if (run >= 4) result.Add(PokerDiceCombination.SmallStraight);
        if (run >= 5) result.Add(PokerDiceCombination.LargeStraight);
        if ((hasThree && hasTwo) || max >= 5) result.Add(PokerDiceCombination.FullHouse);
        if (max >= 4) result.Add(PokerDiceCombination.FourOfAKind);
        if (max >= 5) result.Add(PokerDiceCombination.FiveOfAKind);
        return result;
    }

    private static int LongestRun(int[] counts)
    {
        int best = 0, current = 0;
        for (int f = 1; f <= Faces; f++)
        {
            current = counts[f] > 0 ? current + 1 : 0;
            if (current > best) best = current;
        }
        return best;
    }
}
