using System;
using System.Collections.Generic;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>How a die is thrown. Shared by all dice; lives on the <see cref="DiceManager"/>.</summary>
[Serializable]
public class DiceRollAnimation
{
    [Min(0.05f)] public float duration = 0.6f;
    [Tooltip("Seconds between one die starting to roll and the next, so they don't move as one.")]
    [Min(0f)] public float stagger = 0.06f;

    [Tooltip("Scale at the top of the throw, relative to rest.")]
    public float liftScale = 1.35f;
    [Tooltip("How far the die rises at the top of the throw (UI units of the canvas). 0: scale only.")]
    public float liftHeight = 0f;
    [Tooltip("Height over the roll (time 0..1): 0 = on the table, 1 = top of the throw.")]
    public AnimationCurve liftCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 4f), new Keyframe(0.4f, 1f, 0f, 0f), new Keyframe(1f, 0f, -3.5f, 0f));

    [Tooltip("How far a rolled die travels, in a random direction (UI units). It always lands inside " +
             "the Dice Parent's rect, clear of the other dice when there's room. Max 0: dice don't travel.")]
    public Vector2 travelDistance = new Vector2(40f, 160f);
    [Tooltip("Travel progress over the roll (time 0..1 -> 0..1).")]
    public AnimationCurve travelCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2.2f), new Keyframe(1f, 1f, 0f, 0f));

    [Tooltip("Full turns per roll.")]
    [Min(0)] public int spins = 2;
    [Tooltip("Spin progress over the roll (time 0..1 -> 0..1).")]
    public AnimationCurve spinCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(1f, 1f, 0f, 0f));
    [Tooltip("The die lands tilted up to this many degrees either way.")]
    [Min(0f)] public float restTilt = 8f;

    [Tooltip("Seconds per sprite while the die tumbles.")]
    [Min(0.01f)] public float frameInterval = 0.06f;

    [Tooltip("Scale punch on landing. 0: none.")]
    [Min(0f)] public float landPunch = 0.12f;
    [Min(0f)] public float landPunchDuration = 0.2f;
}

/// <summary>How a locked die looks.</summary>
[Serializable]
public class DiceLockStyle
{
    [Tooltip("Multiplied with the die's color while locked.")]
    public Color lockedTint = new Color(0.7f, 0.7f, 0.75f, 1f);
    [Tooltip("Where a locked die moves (UI units of the canvas), e.g. pulled down off the play area.")]
    public Vector2 offset = new Vector2(0f, -14f);
    [Min(0f)] public float duration = 0.12f;
}

/// <summary>The outline a die gets while hovered.</summary>
[Serializable]
public class DiceOutlineStyle
{
    public bool enabled = true;
    public Color color = new Color(1f, 0.95f, 0.75f, 1f);
    [Tooltip("Outline thickness (UI units). Unity's UI Outline traces the sprite by offsetting it; keep it small (2-6).")]
    public Vector2 width = new Vector2(4f, 4f);
    [Min(0f)] public float fadeDuration = 0.1f;
    [Tooltip("Only while clicking the die would lock / unlock it (not mid-roll, not before the first roll, not out of rerolls).")]
    public bool onlyWhenLockable = true;
}

/// <summary>
/// Text popups over the table (through the <see cref="TextAlertManager"/>): the score's change, the hand the
/// dice made, and the goals being reached.
/// </summary>
[Serializable]
public class DicePopups
{
    public bool enabled = true;
    [Tooltip("Seconds between popups that come at once (a landing that reaches a goal), so they don't pile up.")]
    [Min(0f)] public float interval = 0.25f;

    [Header("Where (empty: Score Text / Combination Text / Target Text)")]
    public Transform scoreAnchor;
    public Transform combinationAnchor;
    public Transform goalAnchor;

    [Header("Text")]
    [Tooltip("{0} = points gained.")]
    public string gainFormat = "+{0}";
    [Tooltip("{0} = points lost (a positive number).")]
    public string lossFormat = "-{0}";
    public string goalReachedText = "Goal reached!";
    public string riskyGoalReachedText = "You feel hopeful.";
    [Tooltip("At the end of a round that reached the risky goal: the next cards are upgraded.")]
    public string cardUpgradedText = "Card upgraded!";

    [Header("Colors")]
    public Color gainColor = new Color(0.45f, 0.9f, 0.45f, 1f);
    public Color lossColor = new Color(0.95f, 0.4f, 0.4f, 1f);
    public Color combinationColor = Color.white;
    public Color goalColor = new Color(1f, 0.9f, 0.45f, 1f);
    public Color riskyGoalColor = new Color(0.45f, 0.9f, 0.45f, 1f);
}

/// <summary>Points a poker dice hand is worth.</summary>
[Serializable]
public class CombinationScore
{
    public PokerDiceCombination combination;
    public string displayName;
    public int points;

    public CombinationScore(PokerDiceCombination combination, string displayName, int points)
    {
        this.combination = combination;
        this.displayName = displayName;
        this.points = points;
    }
}

/// <summary>A scored set of dice: the best hand they make, with every card bonus applied.</summary>
public class DiceScore
{
    public PokerDiceCombination combination;
    public string combinationName;
    /// <summary>The hand's points (with the dice sum when it counts, and hand multipliers).</summary>
    public int basePoints;
    /// <summary>Everything the cards added on top, multipliers included.</summary>
    public int bonusPoints;
    public int total;
}

/// <summary>How a dice round ended.</summary>
public class DiceRoundResult
{
    public DiceScore score;
    public int target;
    public bool passed;
    /// <summary>The risky goal: <see cref="target"/> x the risky multiplier.</summary>
    public int riskyTarget;
    /// <summary>The score reached the risky goal: the next cards are upgraded.</summary>
    public bool riskyPassed;
    public int[] values;
}

/// <summary>
/// The poker dice table. Owns the dice, the roll animation, lock looks, the scoring table and the
/// table's UI; a <see cref="Dice"/> only animates itself.
///
/// <para>A round (<see cref="StartRound"/>): the first <see cref="Roll"/> throws every die. After it,
/// clicking a die locks / unlocks it, and each further Roll (up to <see cref="rerollsPerRound"/> plus
/// card bonuses) rethrows the unlocked ones. <see cref="Finish"/> - or running out of rerolls - scores
/// the best poker hand the dice make, cards included, against the target.</para>
///
/// <para>Two goals: the target (the minimum to pass) and the risky goal, <see cref="riskyGoalMultiplier"/>
/// times higher - reaching it upgrades the next cards offered (<see cref="DiceRoundResult.riskyPassed"/>).</para>
/// </summary>
public class DiceManager : MonoBehaviour
{
    public static DiceManager Instance;

    [Header("Dice")]
    [Tooltip("How many dice the table has. Spawned from Dice Prefab at scene start; dice already in the " +
             "list below count towards it.")]
    [SerializeField, Min(1)] private int startDiceCount = 5;
    [Tooltip("Spawned to make up Start Dice Count. A prefab asset, or a template die in the scene (hidden, " +
             "never rolled).")]
    [SerializeField] private Dice dicePrefab;
    [Tooltip("The dice area: spawned dice go here, are scattered across it, and rolled dice only ever land " +
             "inside its rect. No layout group on it - the dice move freely. Empty: the template's parent, " +
             "else the table root.")]
    [SerializeField] private RectTransform diceParent;
    [Tooltip("Extra gap kept between dice when they land (UI units).")]
    [SerializeField, Min(0f)] private float diceSpacing = 8f;
    [Tooltip("Dice placed by hand. Spawned dice are added here at runtime.")]
    [SerializeField] private List<Dice> dice = new List<Dice>();
    [Tooltip("Faces 1 to 6, in order.")]
    [SerializeField] private Sprite[] faceSprites = new Sprite[PokerDiceEvaluator.Faces];
    [Tooltip("Frames shown (in random order) while a die tumbles. Empty: random faces.")]
    [SerializeField] private Sprite[] tumbleSprites = new Sprite[0];

    [Header("Animation")]
    public DiceRollAnimation rollAnimation = new DiceRollAnimation();
    public DiceLockStyle lockStyle = new DiceLockStyle();
    [Tooltip("Outline a die gets while the mouse is over it.")]
    public DiceOutlineStyle hoverOutline = new DiceOutlineStyle();

    [Header("Audio")]
    [SerializeField] private AudioClip rollSfx;
    [SerializeField] private AudioClip landSfx;
    [SerializeField] private AudioClip lockSfx;
    [SerializeField] private AudioClip unlockSfx;
    [SerializeField] private AudioClip passedSfx;
    [SerializeField] private AudioClip failedSfx;

    [Header("Round")]
    [Tooltip("Rerolls after the first throw (cards can add more).")]
    [Min(0)] public int rerollsPerRound = 2;
    [Tooltip("Throw all dice as soon as the round starts, instead of waiting for the Roll button.")]
    [SerializeField] private bool autoFirstRoll = false;
    [Tooltip("Score the dice as soon as the last reroll lands.")]
    [SerializeField] private bool autoFinishWhenOutOfRerolls = true;
    [Tooltip("Seconds the result stays on the table before the round hands back.")]
    [SerializeField, Min(0f)] private float finishDelay = 1.5f;
    [Tooltip("Hide the table root when the round ends.")]
    [SerializeField] private bool hideTableOnFinish = true;
    [Tooltip("The risky goal is the target times this. Reaching it upgrades the next cards offered.")]
    [SerializeField, Min(1f)] private float riskyGoalMultiplier = 2f;

    [Header("Scoring")]
    [Tooltip("Add the sum of all dice to the hand's points.")]
    [SerializeField] private bool addDiceSum = false;
    [SerializeField] private List<CombinationScore> combinations = new List<CombinationScore>
    {
        new CombinationScore(PokerDiceCombination.Nothing, "Nothing", 0),
        new CombinationScore(PokerDiceCombination.OnePair, "Pair", 5),
        new CombinationScore(PokerDiceCombination.TwoPair, "Two Pair", 10),
        new CombinationScore(PokerDiceCombination.ThreeOfAKind, "Three of a Kind", 15),
        new CombinationScore(PokerDiceCombination.SmallStraight, "Small Straight", 20),
        new CombinationScore(PokerDiceCombination.LargeStraight, "Large Straight", 30),
        new CombinationScore(PokerDiceCombination.FullHouse, "Full House", 25),
        new CombinationScore(PokerDiceCombination.FourOfAKind, "Four of a Kind", 40),
        new CombinationScore(PokerDiceCombination.FiveOfAKind, "Five of a Kind", 60),
    };

    [Header("UI")]
    [Tooltip("The table (background, dice, buttons). Shown for a round.")]
    [SerializeField] private GameObject tableRoot;
    [SerializeField] private Button rollButton;
    [SerializeField] private TextMeshProUGUI rollButtonLabel;
    [SerializeField] private string firstRollLabel = "Roll";
    [Tooltip("{0} = rerolls left.")]
    [SerializeField] private string rerollLabel = "Reroll ({0})";
    [SerializeField] private Button finishButton;
    [Tooltip("{0} = rerolls left.")]
    [SerializeField] private TextMeshProUGUI rerollsText;
    [SerializeField] private string rerollsFormat = "Rerolls: {0}";
    [SerializeField] private TextMeshProUGUI targetText;
    [Tooltip("{0} = target score, {1} = risky goal.")]
    [SerializeField] private string targetFormat = "Need {0}";
    [Tooltip("Optional: shows the risky goal.")]
    [SerializeField] private TextMeshProUGUI riskyTargetText;
    [Tooltip("{0} = risky goal, {1} = target score.")]
    [SerializeField] private string riskyTargetFormat = "Risky {0}";
    [SerializeField] private TextMeshProUGUI scoreText;
    [Tooltip("{0} = the score. Copied into the CountingText added to Score Text when it has none; a CountingText " +
             "already there keeps its own format.")]
    [SerializeField] private string scoreFormat = "{0}";
    [Tooltip("Counts the score up / down and shakes when it changes. Defaults to the CountingText on Score Text (added if missing).")]
    [SerializeField] private CountingText scoreCounter;
    [SerializeField] private TextMeshProUGUI combinationText;
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private string passedText = "They still care.";
    [SerializeField] private string failedText = "...";

    [Header("Popups")]
    public DicePopups popups = new DicePopups();

    [Header("Events")]
    public UnityEvent onRoundStarted = new UnityEvent();
    public UnityEvent onRolled = new UnityEvent();
    /// <summary>The round's total score.</summary>
    public UnityEvent<int> onRoundFinished = new UnityEvent<int>();

    public bool RoundActive { get; private set; }
    public bool HasRolled { get; private set; }
    public int RerollsLeft { get; private set; }
    public int Target { get; private set; }
    /// <summary>The risky goal: <see cref="Target"/> x <see cref="riskyGoalMultiplier"/>.</summary>
    public int RiskyTarget => Mathf.CeilToInt(Target * riskyGoalMultiplier);
    public bool IsRolling => landingPending > 0;
    public IReadOnlyList<Dice> AllDice => dice;

    private bool finishing;
    private int landingPending;
    private IReadOnlyList<CardInstance> cards = Array.Empty<CardInstance>();
    private Action<DiceRoundResult> roundCallback;
    private DiceRoundResult pendingResult;
    private Tween finishTween;
    private readonly List<Tween> popupTweens = new List<Tween>();
    private float nextPopupTime;

    // The score on screen: the last one the dice made, kept while they are rolled again.
    private DiceScore shownScore;

    /// <summary>
    /// The score's counter: <see cref="scoreCounter"/>, else the CountingText on <see cref="scoreText"/> -
    /// added when it has none, so the score always counts instead of jumping.
    /// </summary>
    private CountingText ScoreCounter
    {
        get
        {
            if (scoreCounter || !scoreText) return scoreCounter;

            scoreCounter = scoreText.GetComponent<CountingText>();
            if (!scoreCounter)
            {
                scoreCounter = scoreText.gameObject.AddComponent<CountingText>();
                // The counter only has the total to show: {1} / {2} would have nothing to fill them.
                if (!scoreFormat.Contains("{1}") && !scoreFormat.Contains("{2}")) scoreCounter.Format = scoreFormat;
            }
            return scoreCounter;
        }
    }

    private bool CanLock => RoundActive && HasRolled && !IsRolling && !finishing && RerollsLeft > 0;

    /// <summary>True when clicking a die would lock or unlock it right now.</summary>
    public bool CanLockNow => CanLock;

    private bool initialized;

    private void Awake() => Initialize();

    /// <summary>
    /// Sets up the dice and buttons. Runs from Awake, or from <see cref="StartRound"/> when this sits on
    /// a table panel that starts hidden (an inactive object gets no Awake until it is shown).
    /// </summary>
    private void Initialize()
    {
        if (initialized) return;
        initialized = true;

        h.CreateStaticInstance(this, ref Instance, setDontDestroy: false);

        List<Dice> spawned = SpawnDice();

        foreach (Dice die in dice)
        {
            if (!die) continue;
            die.Init(this);
            die.SetFaces(faceSprites);
        }

        ScatterSpawned(spawned);

        if (Area && Area.GetComponent<LayoutGroup>())
            h.Out($"DiceManager: the dice area '{Area.name}' has a layout group - it will snap the dice back. " +
                  "Remove it: the dice place and move themselves.");

        if (rollButton) rollButton.onClick.AddListener(Roll);
        if (finishButton) finishButton.onClick.AddListener(Finish);
        if (tableRoot) tableRoot.SetActive(false);
        if (resultText) resultText.gameObject.SetActive(false);
    }

    /// <summary>
    /// Where dice live: <see cref="diceParent"/>, else the template die's parent, else the table root.
    /// Rolled dice land inside its rect.
    /// </summary>
    private RectTransform Area
    {
        get
        {
            if (diceParent) return diceParent;
            if (dicePrefab && dicePrefab.gameObject.scene.IsValid() && dicePrefab.transform.parent is RectTransform templateParent)
                return templateParent;
            if (tableRoot && tableRoot.transform is RectTransform table) return table;
            return transform as RectTransform;
        }
    }

    /// <summary>Spawns dice from <see cref="dicePrefab"/> until the table has <see cref="startDiceCount"/>. Returns the new ones.</summary>
    private List<Dice> SpawnDice()
    {
        List<Dice> spawned = new List<Dice>();
        bool template = dicePrefab && dicePrefab.gameObject.scene.IsValid();
        dice.RemoveAll(die => !die || die == dicePrefab);
        if (template) dicePrefab.gameObject.SetActive(false);

        int missing = startDiceCount - dice.Count;
        if (missing <= 0) return spawned;

        if (!dicePrefab)
        {
            h.Out($"DiceManager: {missing} dice short of Start Dice Count ({startDiceCount}) and no Dice Prefab to spawn them.");
            return spawned;
        }

        Transform parent = Area ? Area : transform;
        for (int i = 0; i < missing; i++)
        {
            Dice die = Instantiate(dicePrefab, parent);
            die.name = $"{dicePrefab.name} {dice.Count + 1}";
            die.gameObject.SetActive(true);
            dice.Add(die);
            spawned.Add(die);
        }

        return spawned;
    }

    // ---------------------------------------------------------------------
    // Placement inside the dice area
    // ---------------------------------------------------------------------

    /// <summary>Spreads freshly spawned dice over the area, clear of each other and of the dice already there.</summary>
    private void ScatterSpawned(List<Dice> spawned)
    {
        if (spawned.Count == 0 || !Area) return;

        List<Vector3> taken = new List<Vector3>();
        foreach (Dice die in dice)
            if (die && !spawned.Contains(die)) taken.Add(Occupied(die, die.TablePosition));

        foreach (Dice die in spawned)
        {
            Vector3 spot = PickSpot(die, taken, anywhere: true);
            die.PlaceAt(spot);
            taken.Add(Occupied(die, spot));
        }
    }

    /// <summary>
    /// A landing spot for <paramref name="die"/> (local, in its parent): <see cref="DiceRollAnimation.travelDistance"/>
    /// away in a random direction - or, with <paramref name="anywhere"/>, anywhere in the area - kept inside the
    /// area's rect and, when there's room, clear of <paramref name="taken"/> (area-space x, y and radius).
    /// </summary>
    private Vector3 PickSpot(Dice die, List<Vector3> taken, bool anywhere)
    {
        RectTransform area = Area;
        if (!area) return die.TablePosition;

        Vector2 range = rollAnimation.travelDistance.x < rollAnimation.travelDistance.y
            ? rollAnimation.travelDistance : new Vector2(rollAnimation.travelDistance.y, rollAnimation.travelDistance.x);
        if (!anywhere && range.y <= 0f) return die.TablePosition;

        float radius = die.Radius;
        Rect bounds = area.rect;
        float xMin = bounds.xMin + radius, xMax = bounds.xMax - radius;
        float yMin = bounds.yMin + radius, yMax = bounds.yMax - radius;
        // An area smaller than the die: its centre is the only place left.
        if (xMin > xMax) xMin = xMax = bounds.center.x;
        if (yMin > yMax) yMin = yMax = bounds.center.y;

        Vector2 from = ToArea(die, die.TablePosition);
        Vector2 best = new Vector2(Mathf.Clamp(from.x, xMin, xMax), Mathf.Clamp(from.y, yMin, yMax));
        float bestClearance = float.NegativeInfinity;

        const int Tries = 20;
        for (int i = 0; i < Tries; i++)
        {
            Vector2 spot = anywhere
                ? new Vector2(UnityEngine.Random.Range(xMin, xMax), UnityEngine.Random.Range(yMin, yMax))
                : from + UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(Mathf.Max(0f, range.x), range.y);
            spot.x = Mathf.Clamp(spot.x, xMin, xMax);
            spot.y = Mathf.Clamp(spot.y, yMin, yMax);

            float clearance = Clearance(spot, radius, taken);
            if (clearance >= 0f) return FromArea(die, spot);
            if (clearance > bestClearance)
            {
                bestClearance = clearance;
                best = spot;
            }
        }

        // Too crowded for a clean spot: the least overlapping one.
        return FromArea(die, best);
    }

    /// <summary>Smallest gap between a die at <paramref name="spot"/> and the taken spots (negative: overlapping).</summary>
    private float Clearance(Vector2 spot, float radius, List<Vector3> taken)
    {
        float clearance = float.PositiveInfinity;
        foreach (Vector3 other in taken)
        {
            float gap = Vector2.Distance(spot, other) - radius - other.z - diceSpacing;
            if (gap < clearance) clearance = gap;
        }
        return clearance;
    }

    /// <summary>A die at <paramref name="localPosition"/> as an area-space (x, y, radius) obstacle.</summary>
    private Vector3 Occupied(Dice die, Vector3 localPosition)
    {
        Vector2 p = ToArea(die, localPosition);
        return new Vector3(p.x, p.y, die.Radius);
    }

    private Vector2 ToArea(Dice die, Vector3 localPosition)
    {
        Transform parent = die.transform.parent;
        if (!parent || parent == Area) return localPosition;
        return Area.InverseTransformPoint(parent.TransformPoint(localPosition));
    }

    private Vector3 FromArea(Dice die, Vector2 areaPosition)
    {
        Transform parent = die.transform.parent;
        Vector3 z = new Vector3(0f, 0f, die.TablePosition.z);
        if (!parent || parent == Area) return (Vector3)areaPosition + z;
        Vector3 local = parent.InverseTransformPoint(Area.TransformPoint(areaPosition));
        local.z = die.TablePosition.z;
        return local;
    }

    // ---------------------------------------------------------------------
    // Round
    // ---------------------------------------------------------------------

    /// <summary>
    /// Opens the table for a round against <paramref name="target"/> with <paramref name="ownedCards"/>'
    /// bonuses. <paramref name="onFinished"/> gets the result once it has been shown.
    /// </summary>
    public void StartRound(int target, IReadOnlyList<CardInstance> ownedCards, Action<DiceRoundResult> onFinished)
    {
        Initialize();

        if (RoundActive)
        {
            h.Out("DiceManager: a round is already running.");
            return;
        }

        RoundActive = true;
        HasRolled = false;
        finishing = false;
        landingPending = 0;
        Target = target;
        cards = ownedCards ?? Array.Empty<CardInstance>();
        roundCallback = onFinished;
        RerollsLeft = rerollsPerRound + ExtraRerolls(cards);

        foreach (Dice die in dice)
            if (die) die.ResetDice();

        StopPopups();
        if (tableRoot) tableRoot.SetActive(true);
        if (resultText) resultText.gameObject.SetActive(false);
        UpdateUI();
        onRoundStarted?.Invoke();

        if (autoFirstRoll) Roll();
    }

    /// <summary>First call: throws every die. Later calls: rethrows the unlocked ones, using a reroll.</summary>
    public void Roll()
    {
        if (!RoundActive || finishing || IsRolling) return;

        List<Dice> toRoll = new List<Dice>();
        foreach (Dice die in dice)
            if (die && (!HasRolled || !die.IsLocked)) toRoll.Add(die);

        if (HasRolled)
        {
            if (RerollsLeft <= 0 || toRoll.Count == 0) return;
            RerollsLeft--;
        }

        if (toRoll.Count == 0) return;
        HasRolled = true;
        landingPending = toRoll.Count;

        // Dice staying put (locked) block their spots; each thrown die then claims where it will land.
        List<Vector3> taken = new List<Vector3>();
        foreach (Dice die in dice)
            if (die && !toRoll.Contains(die)) taken.Add(Occupied(die, die.TablePosition));

        for (int i = 0; i < toRoll.Count; i++)
        {
            Dice die = toRoll[i];
            Vector3 landAt = PickSpot(die, taken, anywhere: false);
            taken.Add(Occupied(die, landAt));

            int result = UnityEngine.Random.Range(1, PokerDiceEvaluator.Faces + 1);
            die.Roll(result, rollAnimation, faceSprites, tumbleSprites, i * rollAnimation.stagger, landAt, OnDieLanded);
        }

        PlaySfx(rollSfx);
        UpdateUI();
    }

    /// <summary>Locks or unlocks <paramref name="die"/>, when locking is allowed right now.</summary>
    public void ToggleLock(Dice die)
    {
        if (!die || !CanLock) return;

        die.SetLocked(!die.IsLocked, lockStyle);
        PlaySfx(die.IsLocked ? lockSfx : unlockSfx);
        UpdateUI();
    }

    /// <summary>Scores the dice and ends the round (after <see cref="finishDelay"/>).</summary>
    public void Finish()
    {
        if (!RoundActive || !HasRolled || IsRolling || finishing) return;
        finishing = true;

        int[] values = Values();
        DiceScore score = Evaluate(values, cards);
        pendingResult = new DiceRoundResult
        {
            score = score,
            target = Target,
            passed = score.total >= Target,
            riskyTarget = RiskyTarget,
            riskyPassed = RiskyTarget > 0 && score.total >= RiskyTarget,
            values = values,
        };

        if (resultText)
        {
            resultText.text = pendingResult.passed ? passedText : failedText;
            resultText.gameObject.SetActive(true);
        }

        PlaySfx(pendingResult.passed ? passedSfx : failedSfx);
        UpdateUI();
        if (pendingResult.riskyPassed) Popup(popups.cardUpgradedText, GoalAnchor, popups.riskyGoalColor);

        if (finishDelay > 0f) finishTween = Tween.Delay(this, finishDelay, manager => manager.CompleteRound());
        else CompleteRound();
    }

    /// <summary>Drops the round without a result (the song ended mid-round).</summary>
    public void CancelRound()
    {
        if (!RoundActive) return;

        finishTween.Stop();
        foreach (Dice die in dice)
            if (die) die.StopRoll();

        RoundActive = false;
        finishing = false;
        landingPending = 0;
        roundCallback = null;
        pendingResult = null;
        StopPopups();
        if (tableRoot) tableRoot.SetActive(false);
    }

    private void OnDieLanded(Dice die)
    {
        PlaySfx(landSfx);
        landingPending = Mathf.Max(0, landingPending - 1);
        if (landingPending > 0) return;

        DiceScore before = shownScore;
        UpdateUI();
        ShowRollPopups(before, shownScore);
        onRolled?.Invoke();

        if (autoFinishWhenOutOfRerolls && RerollsLeft <= 0) Finish();
    }

    private void CompleteRound()
    {
        DiceRoundResult result = pendingResult;
        Action<DiceRoundResult> callback = roundCallback;

        pendingResult = null;
        roundCallback = null;
        RoundActive = false;
        finishing = false;
        if (hideTableOnFinish && tableRoot) tableRoot.SetActive(false);

        onRoundFinished?.Invoke(result.score.total);
        callback?.Invoke(result);
    }

    // ---------------------------------------------------------------------
    // Scoring
    // ---------------------------------------------------------------------

    public int[] Values()
    {
        List<int> values = new List<int>();
        foreach (Dice die in dice)
            if (die) values.Add(die.Value);
        return values.ToArray();
    }

    /// <summary>Points for a combination in the scoring table (0 when it isn't listed).</summary>
    public CombinationScore ScoreFor(PokerDiceCombination combination)
    {
        foreach (CombinationScore entry in combinations)
            if (entry != null && entry.combination == combination) return entry;
        return null;
    }

    /// <summary>
    /// The best score <paramref name="values"/> can make: every hand they contain is scored with the
    /// cards' bonuses and the highest total wins. Order: hand points (+ dice sum) x hand multipliers,
    /// + flat / hand / face bonuses, x score multipliers.
    /// </summary>
    public DiceScore Evaluate(IReadOnlyList<int> values, IReadOnlyList<CardInstance> ownedCards)
    {
        int diceSum = 0;
        foreach (int v in values) diceSum += v;

        DiceScore best = null;
        foreach (PokerDiceCombination hand in PokerDiceEvaluator.Contained(values))
        {
            CombinationScore entry = ScoreFor(hand);
            if (entry == null) continue;

            float handPoints = entry.points + (addDiceSum ? diceSum : 0);
            float bonus = 0f;
            float scoreMultiplier = 1f;

            if (ownedCards != null)
                foreach (CardInstance card in ownedCards)
                {
                    CardEffect effect = card?.Effect;
                    if (effect == null) continue;

                    // The card's own amount / multiplier: an upgraded card's are higher.
                    switch (effect.type)
                    {
                        case CardEffectType.FlatPoints:
                            bonus += card.Amount;
                            break;
                        case CardEffectType.CombinationBonus:
                            if (effect.Matches(hand)) bonus += card.Amount;
                            break;
                        case CardEffectType.CombinationMultiplier:
                            if (effect.Matches(hand)) handPoints *= card.Multiplier;
                            break;
                        case CardEffectType.FaceValueBonus:
                            foreach (int v in values)
                                if (v == effect.faceValue) bonus += card.Amount;
                            break;
                        case CardEffectType.ScoreMultiplier:
                            scoreMultiplier *= card.Multiplier;
                            break;
                    }
                }

            int basePoints = Mathf.RoundToInt(handPoints);
            int total = Mathf.RoundToInt((handPoints + bonus) * scoreMultiplier);

            if (best != null && (total < best.total || (total == best.total && hand < best.combination))) continue;

            best = new DiceScore
            {
                combination = hand,
                combinationName = string.IsNullOrEmpty(entry.displayName) ? CardEffect.Nicify(hand) : entry.displayName,
                basePoints = basePoints,
                bonusPoints = total - basePoints,
                total = total,
            };
        }

        return best ?? new DiceScore { combination = PokerDiceCombination.Nothing, combinationName = "Nothing" };
    }

    private static int ExtraRerolls(IReadOnlyList<CardInstance> ownedCards)
    {
        int extra = 0;
        foreach (CardInstance card in ownedCards)
        {
            CardEffect effect = card?.Effect;
            if (effect != null && effect.type == CardEffectType.ExtraRerolls) extra += card.Amount;
        }
        return Mathf.Max(0, extra);
    }

    // ---------------------------------------------------------------------
    // UI / audio
    // ---------------------------------------------------------------------

    private void UpdateUI()
    {
        bool anyUnlocked = false;
        foreach (Dice die in dice)
            if (die && !die.IsLocked) anyUnlocked = true;

        bool idle = RoundActive && !finishing && !IsRolling;

        if (rollButton) rollButton.interactable = idle && (!HasRolled || (RerollsLeft > 0 && anyUnlocked));
        if (rollButtonLabel) rollButtonLabel.text = HasRolled ? string.Format(rerollLabel, RerollsLeft) : firstRollLabel;
        if (finishButton) finishButton.interactable = idle && HasRolled;
        if (rerollsText) rerollsText.text = string.Format(rerollsFormat, RerollsLeft);
        if (targetText) targetText.text = string.Format(targetFormat, Target, RiskyTarget);
        if (riskyTargetText) riskyTargetText.text = string.Format(riskyTargetFormat, RiskyTarget, Target);

        // The score is what the dice on the table make right now - never added up over rolls. While dice
        // are in the air the last one stays up; once they land it counts from that to the new score
        // (10 -> 20, or down). A new round starts empty and counts up from 0.
        if (!RoundActive || !HasRolled) shownScore = null;
        else if (!IsRolling) shownScore = Evaluate(Values(), cards);

        CountingText counter = ScoreCounter;
        if (counter)
        {
            if (shownScore == null) counter.Clear();
            else counter.SetValue(shownScore.total);
        }

        if (combinationText) combinationText.text = shownScore != null ? shownScore.combinationName : "";

        // Whether a die may be locked just changed (a roll started or landed): hovered dice follow.
        foreach (Dice die in dice)
            if (die) die.RefreshOutline();
    }

    // ---------------------------------------------------------------------
    // Popups
    // ---------------------------------------------------------------------

    private Transform ScoreAnchor => popups.scoreAnchor ? popups.scoreAnchor : scoreText ? scoreText.transform : null;
    private Transform CombinationAnchor => popups.combinationAnchor ? popups.combinationAnchor : combinationText ? combinationText.transform : null;
    private Transform GoalAnchor => popups.goalAnchor ? popups.goalAnchor : targetText ? targetText.transform : null;

    /// <summary>
    /// After a landing: how much the score changed (+10 / -5), the hand the dice make, and a goal reached
    /// (the score climbing past the target or the risky goal).
    /// </summary>
    private void ShowRollPopups(DiceScore before, DiceScore after)
    {
        if (!popups.enabled || after == null) return;

        int oldTotal = before?.total ?? 0;
        int change = after.total - oldTotal;
        if (change != 0)
            Popup(string.Format(change > 0 ? popups.gainFormat : popups.lossFormat, Mathf.Abs(change)), ScoreAnchor,
                change > 0 ? popups.gainColor : popups.lossColor);

        if (after.combination != PokerDiceCombination.Nothing)
            Popup(after.combinationName, CombinationAnchor, popups.combinationColor);

        if (Target > 0 && oldTotal < Target && after.total >= Target)
            Popup(popups.goalReachedText, GoalAnchor, popups.goalColor);
        if (RiskyTarget > 0 && oldTotal < RiskyTarget && after.total >= RiskyTarget)
            Popup(popups.riskyGoalReachedText, GoalAnchor, popups.riskyGoalColor);
    }

    /// <summary>Pops <paramref name="text"/> up at <paramref name="anchor"/>, <see cref="DicePopups.interval"/> after the previous popup.</summary>
    private void Popup(string text, Transform anchor, Color color)
    {
        if (!popups.enabled || string.IsNullOrEmpty(text)) return;
        if (!TextAlertManager.Instance)
        {
            h.Out($"DiceManager: no TextAlertManager for the popup '{text}'.");
            return;
        }
        if (!anchor) anchor = tableRoot ? tableRoot.transform : transform;

        float delay = Mathf.Max(0f, nextPopupTime - Time.time);
        nextPopupTime = Time.time + delay + popups.interval;

        if (delay <= 0f)
        {
            TextAlertManager.Instance.CreateTextAlert(text, anchor, color);
            return;
        }

        popupTweens.RemoveAll(tween => !tween.isAlive);
        popupTweens.Add(Tween.Delay(anchor, delay, a =>
        {
            if (TextAlertManager.Instance) TextAlertManager.Instance.CreateTextAlert(text, a, color);
        }));
    }

    private void StopPopups()
    {
        foreach (Tween tween in popupTweens) tween.Stop();
        popupTweens.Clear();
        nextPopupTime = 0f;
    }

    private static void PlaySfx(AudioClip clip)
    {
        if (clip && SFXManager.Instance) SFXManager.Instance.PlayClip(clip);
    }
}
