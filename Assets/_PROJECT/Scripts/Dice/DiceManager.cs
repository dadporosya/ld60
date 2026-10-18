using System;
using System.Collections.Generic;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
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

    [Header("Where (empty: the middle of the dice parent)")]
    public Transform scoreAnchor;
    public Transform combinationAnchor;
    public Transform goalAnchor;
    [Tooltip("Added to the anchor, in canvas units, so popups that come together don't stack on one spot.")]
    public Vector2 scoreOffset = new Vector2(0f, 70f);
    public Vector2 combinationOffset = Vector2.zero;
    public Vector2 goalOffset = new Vector2(0f, -70f);

    [Header("Text")]
    [Tooltip("{0} = points gained.")]
    public string gainFormat = "+{0}";
    [Tooltip("{0} = points lost (a positive number).")]
    public string lossFormat = "-{0}";
    public string goalReachedText = "Goal reached!";
    public string riskyGoalReachedText = "You feel hopeful.";
    [Tooltip("At the end of a round that reached the risky goal: the next cards are upgraded.")]
    public string cardUpgradedText = "Card upgraded!";
    [Tooltip("A die destroyed for rerolls (a card's right click). {0} = rerolls gained.")]
    public string rerollsGainedFormat = "+{0} reroll";

    [Header("Risky Goal Popup")]
    [Tooltip("Size of the risky goal popup, relative to the other popups.")]
    [Min(0f)] public float riskyGoalScale = 1.15f;
    [Tooltip("Seconds the risky goal popup stays up before fading, on top of the usual hold.")]
    [Min(0f)] public float riskyGoalExtraHold = 1f;

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
    [Tooltip("Off: the hand doesn't count - dice making it score as the next best hand they make, and the hint paper leaves it out.")]
    public bool enabled = true;
    public PokerDiceCombination combination;
    public string displayName;
    public int points;

    // Unity builds list entries through this one: without it the field initializers (enabled = true) are skipped.
    public CombinationScore() { }

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

/// <summary>How one round (one dice game) of a turn ended.</summary>
public class DiceRoundResult
{
    public DiceScore score;
    public int[] values;
}

/// <summary>How a dice turn ended: every round's points added up, against the target.</summary>
public class DiceTurnResult
{
    public List<DiceRoundResult> rounds = new List<DiceRoundResult>();
    /// <summary>The points of all rounds added up.</summary>
    public int total;
    public int target;
    public bool passed;
    /// <summary>The risky goal: <see cref="target"/> x the risky multiplier.</summary>
    public int riskyTarget;
    /// <summary>The total reached the risky goal: the next cards are upgraded.</summary>
    public bool riskyPassed;
}

/// <summary>
/// The poker dice table. Owns the dice, the roll animation, lock looks, the scoring table and the
/// table's UI; a <see cref="Dice"/> only animates itself.
///
/// <para>A turn (<see cref="StartTurn"/>) is <see cref="roundsPerTurn"/> rounds. A round is one dice game:
/// the first <see cref="Roll"/> throws every die. After it, clicking a die locks / unlocks it, and each
/// further Roll (up to <see cref="rerollsPerRound"/> plus card bonuses) rethrows the unlocked ones.
/// <see cref="Finish"/> - or running out of rerolls - scores the best poker hand the dice make, cards
/// included, and banks it. After the last round the banked points are checked against the target.</para>
///
/// <para>Cards can add dice to the table (<see cref="CardEffectType.ExtraDice"/>) and let the player right click
/// dice showing certain faces (<see cref="CardEffect.IsDieAction"/>): reroll it alone for free, destroy it for the
/// rest of the round for rerolls, or flip it to another face. Destroyed dice don't score.</para>
///
/// <para>Two goals: the target (the minimum to pass) and the risky goal, <see cref="riskyGoalMultiplier"/>
/// times higher - reaching it upgrades the next cards offered (<see cref="DiceTurnResult.riskyPassed"/>).</para>
/// </summary>
public class DiceManager : MonoBehaviour
{
    public static DiceManager Instance;

    [Header("Dice")]
    [Tooltip("How many dice the table has. Spawned from Dice Prefab at scene start; dice already in the " +
             "list below count towards it. Extra Dice cards spawn more for the turns they're owned in.")]
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
    [Tooltip("A die flipped to another face (a card's right click).")]
    [SerializeField] private AudioClip flipSfx;
    [Tooltip("A die destroyed (a card's right click).")]
    [SerializeField] private AudioClip destroySfx;
    [SerializeField] private AudioClip passedSfx;
    [SerializeField] private AudioClip failedSfx;
    [Tooltip("Played as a good text alert pops up: points gained, a combination, a goal reached, a card upgraded, " +
             "rerolls gained.")]
    [SerializeField] private AudioClip goodAlertSfx;
    [Tooltip("Played as a bad text alert pops up: points lost.")]
    [SerializeField] private AudioClip badAlertSfx;

    [Header("Turn")]
    [Tooltip("Dice games per turn. Each round gets its own rerolls; the points of every round add up, and " +
             "the total is checked against the target after the last one.")]
    [Min(1)] public int roundsPerTurn = 1;

    [Header("Round")]
    [Tooltip("Rerolls after the first throw (cards can add more).")]
    [Min(0)] public int rerollsPerRound = 2;
    [Tooltip("Throw all dice as soon as the round starts, instead of waiting for the Roll button.")]
    [SerializeField] private bool autoFirstRoll = false;
    [Tooltip("Seconds the table waits after opening (and after the tutorial) before the first throw - the Roll " +
             "button stays off meanwhile.")]
    [SerializeField, Min(0f)] private float firstRollDelay = 1f;
    [Tooltip("Score the dice as soon as the last reroll lands - unless a card's right click could still change them.")]
    [SerializeField] private bool autoFinishWhenOutOfRerolls = true;
    [Tooltip("Seconds the result stays on the table before the turn hands back.")]
    [SerializeField, Min(0f)] private float finishDelay = 1.5f;
    [Tooltip("Seconds a finished round's dice stay on the table before the next round of the turn starts.")]
    [SerializeField, Min(0f)] private float nextRoundDelay = 1f;
    [Tooltip("Hide the table root when the turn ends.")]
    [SerializeField] private bool hideTableOnFinish = true;
    [Tooltip("The risky goal is the target times this. Reaching it upgrades the next cards offered.")]
    [SerializeField, Min(1f)] private float riskyGoalMultiplier = 1.5f;

    [Header("Tutorial")]
    [Tooltip("Played over the table before the first round of every run (each launch / StartGame). " +
             "The first throw waits for it. Empty: no tutorial.")]
    [SerializeField] private DialogueContainer tutorialDialogue;
    [Tooltip("Optional speaker for the tutorial's nodes that don't name one.")]
    [SerializeField] private Talkable tutorialSpeaker;
    [Tooltip("Binds its scene objects into a DialogueContainerWithModels tutorial (models enabled per node). " +
             "Defaults to the first one found.")]
    [SerializeField] private TalkableWModelsController tutorialObjects;
    [Tooltip("Keep the skip button (the DialogueManager's hold-to-skip indicator, with its fill image) on screen " +
             "from the tutorial's first node until it ends.")]
    [SerializeField] private bool showSkipDuringTutorial = true;
    [Tooltip("Debug: play the tutorial every round, not only the first time.")]
    [SerializeField] private bool alwaysPlayTutorial = false;

    [Header("Scoring")]
    [Tooltip("Add the sum of all dice to the hand's points: every die scores its face value, the hand adds its bonus.")]
    [SerializeField] private bool addDiceSum = true;
    [Tooltip("A round whose dice make no hand (Nothing) scores 0 - no dice sum, no card bonuses.")]
    [SerializeField] private bool noPointsWithoutCombination = false;
    [Tooltip("Each hand's bonus. Defaults from Balance/dice_balance.py (POINTS), which also works out " +
             "LevelManager's targets from it.")]
    [SerializeField] private List<CombinationScore> combinations = new List<CombinationScore>
    {
        new CombinationScore(PokerDiceCombination.Nothing, "Nothing", 0),
        new CombinationScore(PokerDiceCombination.OnePair, "Pair", 10),
        new CombinationScore(PokerDiceCombination.TwoPair, "Two Pair", 20),
        new CombinationScore(PokerDiceCombination.ThreeOfAKind, "Three of a Kind", 30),
        new CombinationScore(PokerDiceCombination.SmallStraight, "Small Straight", 50),
        new CombinationScore(PokerDiceCombination.LargeStraight, "Large Straight", 100),
        new CombinationScore(PokerDiceCombination.FullHouse, "Full House", 67),
        new CombinationScore(PokerDiceCombination.FourOfAKind, "Four of a Kind", 100),
        new CombinationScore(PokerDiceCombination.FiveOfAKind, "Five of a Kind", 200),
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
    [Tooltip("What the Finish button always says - written into its own text. It ends the round, and can be " +
             "pressed any time the dice are down (not mid-roll, not before the first throw).")]
    [SerializeField] private string finishLabel = "Finish";
    [Tooltip("{0} = rerolls left.")]
    [SerializeField] private TextMeshProUGUI rerollsText;
    [SerializeField] private string rerollsFormat = "Rerolls: {0}";
    [SerializeField] private TextMeshProUGUI targetText;
    [Tooltip("{0} = target score, {1} = risky goal.")]
    [SerializeField] private string targetFormat = "Need {0}";
    [Tooltip("Target Text once the score reaches the target: now it shows the risky goal. {0} = risky goal, " +
             "{1} = target score. Back to Target Format if a reroll drops the score under the target.")]
    [SerializeField] private string reachedTargetFormat = "Risky {0}";
    [Tooltip("Scale punch on Target Text when it switches to the risky goal. 0: none.")]
    [SerializeField, Min(0f)] private float targetSwitchPunch = 0.2f;
    [Tooltip("Optional: shows the risky goal.")]
    [SerializeField] private TextMeshProUGUI riskyTargetText;
    [Tooltip("{0} = risky goal, {1} = target score.")]
    [SerializeField] private string riskyTargetFormat = "Risky {0}";
    [Tooltip("Optional: which round of the turn is on. Hidden while a turn has a single round.")]
    [SerializeField] private TextMeshProUGUI roundText;
    [Tooltip("{0} = current round, {1} = rounds per turn.")]
    [SerializeField] private string roundFormat = "Round {0}/{1}";
    [Tooltip("The turn's total so far (rounds banked + the dice on the table). With more than one round per " +
             "turn: the points of the finished rounds only - the running round's go to Round Score Text, and " +
             "are added here as the round finishes.")]
    [SerializeField] private TextMeshProUGUI scoreText;
    [Tooltip("{0} = the score. Copied into the CountingText added to Score Text when it has none; a CountingText " +
             "already there keeps its own format.")]
    [SerializeField] private string scoreFormat = "{0}";
    [Tooltip("Counts the score up / down and shakes when it changes. Defaults to the CountingText on Score Text (added if missing).")]
    [SerializeField] private CountingText scoreCounter;
    [Tooltip("Optional: the running round's points (what the dice on the table make). Only shown while a turn " +
             "has more than one round. Counts like Score Text (a CountingText is added if missing).")]
    [SerializeField] private TextMeshProUGUI roundScoreText;
    [Tooltip("{0} = the running round's points. Copied into the CountingText added to Round Score Text when it has none.")]
    [SerializeField] private string roundScoreFormat = "{0}";
    [Tooltip("Score Text's colour, with more than one round per turn, while the points banked over the rounds " +
             "are under the target. Round Score Text keeps its own colour.")]
    [SerializeField, FormerlySerializedAs("roundBelowTargetColor")]
    private Color scoreBelowTargetColor = new Color(1f, 0.35f, 0.35f, 1f);
    [SerializeField] private TextMeshProUGUI combinationText;
    [Tooltip("Combination Text's colour while the dice make no proper combination - only Nothing, or hands " +
             "turned off in the scoring table.")]
    [SerializeField] private Color noCombinationColor = new Color(1f, 0.35f, 0.35f, 1f);
    [Tooltip("Shakes the combination text's letters harder the stronger the hand (its points against the best hand " +
             "in the scoring table) by driving its Shake Strength. Defaults to the TextAnimation on Combination Text " +
             "(added if missing); Shake is added to its animations when it has none.")]
    [SerializeField] private TextAnimation combinationAnimation;
    [Tooltip("TextAnimation Shake Strength for the weakest (x) and the best (y) hand. No hand: no shake.")]
    [SerializeField] private Vector2 combinationShakeStrength = new Vector2(0.5f, 6f);
    [Tooltip("Seconds the shake takes to reach a new hand's strength.")]
    [SerializeField, Min(0f)] private float combinationShakeBlend = 0.15f;
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private string passedText = "They still care.";
    [SerializeField] private string failedText = "...";

    [Header("Popups")]
    public DicePopups popups = new DicePopups();

    [Header("Debug")]
    [Tooltip("Debug only: during a turn the keys below end it on the spot - lost, won, or won with the risky goal.")]
    [SerializeField] private bool debugKeys = true;
    [SerializeField] private Key debugLoseKey = Key.W;
    [SerializeField] private Key debugWinKey = Key.E;
    [SerializeField] private Key debugRiskyWinKey = Key.R;

    [Header("Events")]
    public UnityEvent onTurnStarted = new UnityEvent();
    /// <summary>Each round of a turn, the first included.</summary>
    public UnityEvent onRoundStarted = new UnityEvent();
    public UnityEvent onRolled = new UnityEvent();
    /// <summary>The round's score (that round alone).</summary>
    public UnityEvent<int> onRoundFinished = new UnityEvent<int>();
    /// <summary>The turn's total score (every round added up).</summary>
    public UnityEvent<int> onTurnFinished = new UnityEvent<int>();

    public bool TurnActive { get; private set; }
    /// <summary>The round being played, 1-based (0 before the turn's first round).</summary>
    public int CurrentRound { get; private set; }
    /// <summary>Rounds in the running turn (<see cref="roundsPerTurn"/> as it was when the turn started).</summary>
    public int RoundsInTurn { get; private set; } = 1;
    public bool IsLastRound => CurrentRound >= RoundsInTurn;
    /// <summary>Points banked by the turn's finished rounds.</summary>
    public int BankedScore { get; private set; }
    public bool HasRolled { get; private set; }
    public int RerollsLeft { get; private set; }
    public int Target { get; private set; }
    /// <summary>The risky goal: <see cref="Target"/> x <see cref="riskyGoalMultiplier"/>.</summary>
    public int RiskyTarget => Mathf.CeilToInt(Target * riskyGoalMultiplier);
    /// <summary>The scoring table: each hand's bonus points.</summary>
    public IReadOnlyList<CombinationScore> Combinations => combinations;
    public bool IsRolling => landingPending > 0;
    public IReadOnlyList<Dice> AllDice => dice;
    /// <summary>Dice in the running round: the turn's dice count, without the ones destroyed this round.</summary>
    public IReadOnlyList<Dice> DiceInPlay
    {
        get
        {
            List<Dice> inPlay = new List<Dice>();
            foreach (Dice die in dice)
                if (InPlay(die)) inPlay.Add(die);
            return inPlay;
        }
    }

    private static bool InPlay(Dice die) => die && die.gameObject.activeSelf && !die.IsRemoved;

    private bool finishing;
    private int landingPending;
    // Dice on the table this turn (the first ones of the list); the rest are hidden.
    private int diceInTurn;

    // The round has opened but the first throw isn't allowed yet (tutorial, first roll delay).
    private bool introRunning;
    private Tween firstRollTween;
    // Bumped per turn and on cancel: a tutorial ending late only carries on the turn it belongs to.
    private int turnId;
    private DialogueContainer runningTutorial;
    private DialogueManager tutorialManager;
    private UnityAction tutorialEnded;
    private UnityAction tutorialFirstNode;
    private bool tutorialPinnedSkip;
    // Set once the tutorial has been seen to the end in this run; cleared by ResetTutorial (StartGame).
    private bool tutorialPlayedThisRun;
    private IReadOnlyList<CardInstance> cards = Array.Empty<CardInstance>();
    private Action<DiceTurnResult> turnCallback;
    // The turn's rounds so far; the last one is added (not yet banked) as it finishes.
    private readonly List<DiceRoundResult> roundResults = new List<DiceRoundResult>();
    private DiceTurnResult pendingResult;
    private Tween finishTween;
    private readonly List<Tween> popupTweens = new List<Tween>();
    private float nextPopupTime;

    // The target text shows the risky goal (the score reached the target).
    private bool targetShowsRisky;
    private Tween targetPunchTween;
    private Vector3? targetTextScale;

    // The Finish button's own text, which always shows finishLabel.
    private TMP_Text finishButtonLabel;

    // The score on screen: the last one the dice made, kept while they are rolled again.
    private DiceScore shownScore;

    /// <summary>
    /// The score's counter: <see cref="scoreCounter"/>, else the CountingText on <see cref="scoreText"/> -
    /// added when it has none, so the score always counts instead of jumping.
    /// </summary>
    private CountingText ScoreCounter => CounterOn(scoreText, scoreFormat, ref scoreCounter);

    /// <summary>The running round's counter: the CountingText on <see cref="roundScoreText"/> (added if missing).</summary>
    private CountingText RoundScoreCounter => CounterOn(roundScoreText, roundScoreFormat, ref roundScoreCounter);
    private CountingText roundScoreCounter;
    // Score Text's own colour, from the scene: back to it once the target is reached.
    private Color? scoreColor;
    // Combination Text's own colour, from the scene: back to it once the dice make a proper combination.
    private Color? combinationColor;

    private static CountingText CounterOn(TextMeshProUGUI text, string format, ref CountingText counter)
    {
        if (counter || !text) return counter;

        counter = text.GetComponent<CountingText>();
        if (!counter)
        {
            counter = text.gameObject.AddComponent<CountingText>();
            // The counter only has the one number to show: {1} / {2} would have nothing to fill them.
            if (!format.Contains("{1}") && !format.Contains("{2}")) counter.Format = format;
        }
        return counter;
    }

    /// <summary>
    /// The combination text's letter animation: <see cref="combinationAnimation"/>, else the TextAnimation on
    /// <see cref="combinationText"/> - added when it has none. Either way it shakes its whole text.
    /// </summary>
    private TextAnimation CombinationAnimation
    {
        get
        {
            if (combinationAnimation || !combinationText) return combinationAnimation;

            combinationAnimation = combinationText.GetComponent<TextAnimation>();
            if (!combinationAnimation)
            {
                combinationAnimation = combinationText.gameObject.AddComponent<TextAnimation>();
                combinationAnimation.animationTarget = TextAnimation.AnimationTarget.WholeText;
            }
            if (!combinationAnimation.animations.Contains(TextAnimation.AnimationType.Shake))
                combinationAnimation.animations.Add(TextAnimation.AnimationType.Shake);
            if (combinationAnimation.animationTarget is TextAnimation.AnimationTarget.None or TextAnimation.AnimationTarget.Tags)
                combinationAnimation.animationTarget = TextAnimation.AnimationTarget.Both;

            combinationAnimation.shakeStrength = 0f;
            return combinationAnimation;
        }
    }

    // The combination's shake strength right now and the tween easing it to a new hand's.
    private Tween combinationShakeTween;
    private float combinationShakeTarget = -1f;

    /// <summary>Eases the combination text's Shake Strength to the one for a hand of <paramref name="strength"/> (0..1).</summary>
    private void SetCombinationShake(float strength)
    {
        TextAnimation anim = CombinationAnimation;
        if (!anim) return;

        float target = strength > 0f ? Mathf.Lerp(combinationShakeStrength.x, combinationShakeStrength.y, strength) : 0f;
        if (Mathf.Approximately(target, combinationShakeTarget)) return;
        combinationShakeTarget = target;

        combinationShakeTween.Stop();
        if (combinationShakeBlend > 0f && isActiveAndEnabled)
            combinationShakeTween = Tween.Custom(anim, anim.shakeStrength, target, combinationShakeBlend,
                (a, v) => a.shakeStrength = v);
        else anim.shakeStrength = target;
    }

    private bool CanLock => TurnActive && HasRolled && !IsRolling && !finishing && RerollsLeft > 0;

    /// <summary>True when clicking a die would lock or unlock it right now.</summary>
    public bool CanLockNow => CanLock;

    private bool initialized;

    private void Awake() => Initialize();

    /// <summary>
    /// Sets up the dice and buttons. Runs from Awake, or from <see cref="StartTurn"/> when this sits on
    /// a table panel that starts hidden (an inactive object gets no Awake until it is shown).
    /// </summary>
    private void Initialize()
    {
        if (initialized) return;
        initialized = true;

        h.CreateStaticInstance(this, ref Instance, setDontDestroy: false);

        List<Dice> spawned = SpawnDice(startDiceCount);

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
        if (finishButton)
        {
            finishButton.onClick.AddListener(Finish);
            finishButtonLabel = finishButton.GetComponentInChildren<TMP_Text>(true);
            if (rerollsText && rerollsText.transform.IsChildOf(finishButton.transform))
                h.Out($"DiceManager: Rerolls Text '{rerollsText.name}' is the Finish button's label - it keeps " +
                      $"showing '{finishLabel}'. Point Rerolls Text at a text of its own.");
        }
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

    /// <summary>Spawns dice from <see cref="dicePrefab"/> until the table has <paramref name="count"/>. Returns the new ones.</summary>
    private List<Dice> SpawnDice(int count)
    {
        List<Dice> spawned = new List<Dice>();
        bool template = dicePrefab && dicePrefab.gameObject.scene.IsValid();
        dice.RemoveAll(die => !die || die == dicePrefab);
        if (template) dicePrefab.gameObject.SetActive(false);

        int missing = count - dice.Count;
        if (missing <= 0) return spawned;

        if (!dicePrefab)
        {
            h.Out($"DiceManager: {missing} dice short of {count} and no Dice Prefab to spawn them.");
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

    /// <summary>
    /// Puts <paramref name="count"/> dice on the table for the turn: spawns the missing ones (Extra Dice cards)
    /// and hides the ones over the count.
    /// </summary>
    private void SetDiceCount(int count)
    {
        List<Dice> spawned = SpawnDice(count);
        foreach (Dice die in spawned)
        {
            die.Init(this);
            die.SetFaces(faceSprites);
        }

        diceInTurn = count;
        for (int i = 0; i < dice.Count; i++)
            if (dice[i]) dice[i].gameObject.SetActive(i < count);

        ScatterSpawned(spawned);
    }

    /// <summary>Spreads freshly spawned dice over the area, clear of each other and of the dice already there.</summary>
    private void ScatterSpawned(List<Dice> spawned)
    {
        if (spawned.Count == 0 || !Area) return;

        List<Vector3> taken = new List<Vector3>();
        foreach (Dice die in dice)
            if (die && die.gameObject.activeSelf && !spawned.Contains(die)) taken.Add(Occupied(die, die.TablePosition));

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
    // Turn / round
    // ---------------------------------------------------------------------

    /// <summary>
    /// Opens the table for a turn of <see cref="roundsPerTurn"/> rounds against <paramref name="target"/>
    /// with <paramref name="ownedCards"/>' bonuses. <paramref name="onFinished"/> gets the result once the
    /// last round's has been shown.
    /// </summary>
    public void StartTurn(int target, IReadOnlyList<CardInstance> ownedCards, Action<DiceTurnResult> onFinished)
    {
        Initialize();

        if (TurnActive)
        {
            h.Out("DiceManager: a turn is already running.");
            return;
        }

        TurnActive = true;
        Target = target;
        cards = ownedCards ?? Array.Empty<CardInstance>();
        turnCallback = onFinished;
        RoundsInTurn = Mathf.Max(1, roundsPerTurn);
        CurrentRound = 0;
        BankedScore = 0;
        roundResults.Clear();

        StopPopups();
        turnId++;
        if (tableRoot) tableRoot.SetActive(true);
        if (resultText) resultText.gameObject.SetActive(false);
        SetDiceCount(DiceCountFor(cards));
        onTurnStarted?.Invoke();

        PrepareRound();
        if (ShouldPlayTutorial) PlayTutorial(QueueFirstRoll);
        else QueueFirstRoll();
    }

    /// <summary>
    /// Resets the dice and rerolls for the turn's next round. The first throw waits for <see cref="QueueFirstRoll"/>.
    /// With <paramref name="keepFaces"/> the dice keep the faces the last round left them on.
    /// </summary>
    private void PrepareRound(bool keepFaces = false)
    {
        CurrentRound++;
        HasRolled = false;
        finishing = false;
        landingPending = 0;
        RerollsLeft = RerollsFor(cards);

        // A turn's first round starts on random faces, later ones on the last round's - they don't score
        // until the first throw lands. Dice destroyed last round are back.
        for (int i = 0; i < Mathf.Min(diceInTurn, dice.Count); i++)
        {
            Dice die = dice[i];
            if (!die) continue;
            die.Restore();
            die.ResetDice();
            if (!keepFaces) die.ShowFace(UnityEngine.Random.Range(1, PokerDiceEvaluator.Faces + 1));
        }

        introRunning = true;
        UpdateUI();
        onRoundStarted?.Invoke();
    }

    /// <summary>The first throw becomes possible (and, with Auto First Roll, happens) after <see cref="firstRollDelay"/>.</summary>
    private void QueueFirstRoll()
    {
        firstRollTween.Stop();
        if (firstRollDelay > 0f) firstRollTween = Tween.Delay(this, firstRollDelay, manager => manager.EndIntro());
        else EndIntro();
    }

    private void EndIntro()
    {
        if (!TurnActive) return;
        introRunning = false;
        if (autoFirstRoll && !HasRolled) Roll();
        else UpdateUI();
    }

    // ---------------------------------------------------------------------
    // Tutorial
    // ---------------------------------------------------------------------

    private bool ShouldPlayTutorial =>
        tutorialDialogue && (alwaysPlayTutorial || !tutorialPlayedThisRun);

    /// <summary>A new run: the next round plays the tutorial again.</summary>
    public void ResetTutorial() => tutorialPlayedThisRun = false;

    /// <summary>Plays the tutorial over the table, then <paramref name="then"/> (if this turn is still on).</summary>
    private void PlayTutorial(Action then)
    {
        DialogueManager manager = DialogueManager.Instance;
        if (!manager || manager.DialogueActive)
        {
            h.Out(manager ? "DiceManager: a dialogue is already running, the tutorial waits for the next round."
                          : "DiceManager: no DialogueManager in the scene, skipping the tutorial.");
            then();
            return;
        }

        // A runtime copy: filling in speakers writes into the nodes, which would otherwise end up in the asset.
        DialogueContainer instance = Instantiate(tutorialDialogue);
        instance.name = tutorialDialogue.name;
        if (tutorialSpeaker) tutorialSpeaker.InitDialogue(instance);
        else
        {
            instance.AssignNodeIds();
            foreach (DialogueNode node in instance.nodes) node?.Init();
        }

        int id = turnId;
        runningTutorial = instance;
        tutorialManager = manager;

        tutorialEnded = () =>
        {
            StopTutorial();
            tutorialPlayedThisRun = true;
            PlayHistory.MarkDialoguePlayed(tutorialDialogue.name);
            if (id == turnId && TurnActive) then();
        };
        if (manager.onDialogueEnd == null) manager.onDialogueEnd = new UnityEvent();
        manager.onDialogueEnd.AddListener(tutorialEnded);

        if (showSkipDuringTutorial)
        {
            // From the first node on: the skip button shows as the tutorial's text does.
            tutorialFirstNode = () =>
            {
                manager.onStartNode.RemoveListener(tutorialFirstNode);
                tutorialFirstNode = null;
                manager.PinSkipIndicator = true;
                tutorialPinnedSkip = true;
            };
            if (manager.onStartNode == null) manager.onStartNode = new UnityEvent();
            manager.onStartNode.AddListener(tutorialFirstNode);
        }

        // A models dialogue only drives the objects bound into it: started straight through the manager
        // its slots stay empty and no model ever turns on. The controller binds them and plays their intro.
        if (instance is DialogueContainerWithModels modelsDialogue)
        {
            if (!tutorialObjects) tutorialObjects = FindFirstObjectByType<TalkableWModelsController>(FindObjectsInactive.Include);
            if (tutorialObjects)
            {
                tutorialObjects.PlayDialogue(modelsDialogue, tutorialSpeaker);
                return;
            }
            h.Out($"DiceManager: '{instance.name}' has models but no TalkableWModelsController is in the scene.");
        }

        manager.StartDialogue(instance, tutorialSpeaker);
    }

    /// <summary>Stops listening to the tutorial and drops its copy (the dialogue itself is ended by whoever ends it).</summary>
    private void StopTutorial()
    {
        DialogueManager manager = tutorialManager;
        if (manager)
        {
            if (tutorialEnded != null) manager.onDialogueEnd.RemoveListener(tutorialEnded);
            if (tutorialFirstNode != null) manager.onStartNode.RemoveListener(tutorialFirstNode);
            if (tutorialPinnedSkip) manager.PinSkipIndicator = false;
        }

        tutorialEnded = null;
        tutorialFirstNode = null;
        tutorialPinnedSkip = false;
        tutorialManager = null;
        if (runningTutorial) Destroy(runningTutorial);
        runningTutorial = null;
    }

    /// <summary>First call: throws every die. Later calls: rethrows the unlocked ones, using a reroll.</summary>
    public void Roll()
    {
        if (!TurnActive || finishing || IsRolling || introRunning) return;

        List<Dice> toRoll = new List<Dice>();
        foreach (Dice die in DiceInPlay)
            if (!HasRolled || !die.IsLocked) toRoll.Add(die);

        if (HasRolled)
        {
            if (RerollsLeft <= 0 || toRoll.Count == 0) return;
            RerollsLeft--;
        }

        if (toRoll.Count == 0) return;
        HasRolled = true;
        Throw(toRoll);
        PlaySfx(rollSfx);
        UpdateUI();
    }

    /// <summary>Throws <paramref name="toRoll"/>; the others stay put.</summary>
    private void Throw(List<Dice> toRoll)
    {
        landingPending = toRoll.Count;

        // Dice staying put (locked) block their spots; each thrown die then claims where it will land.
        List<Vector3> taken = new List<Vector3>();
        foreach (Dice die in DiceInPlay)
            if (!toRoll.Contains(die)) taken.Add(Occupied(die, die.TablePosition));

        for (int i = 0; i < toRoll.Count; i++)
        {
            Dice die = toRoll[i];
            Vector3 landAt = PickSpot(die, taken, anywhere: false);
            taken.Add(Occupied(die, landAt));

            int result = UnityEngine.Random.Range(1, PokerDiceEvaluator.Faces + 1);
            die.Roll(result, rollAnimation, faceSprites, tumbleSprites, i * rollAnimation.stagger, landAt, OnDieLanded);
        }
    }

    /// <summary>Locks or unlocks <paramref name="die"/>, when locking is allowed right now.</summary>
    public void ToggleLock(Dice die)
    {
        if (!die || !CanLock) return;

        die.SetLocked(!die.IsLocked, lockStyle);
        PlaySfx(die.IsLocked ? lockSfx : unlockSfx);
        UpdateUI();
    }

    // ---------------------------------------------------------------------
    // Die actions (cards used by right clicking a die)
    // ---------------------------------------------------------------------

    /// <summary>Dice can be acted on: the round's dice are down and it isn't being scored.</summary>
    private bool CanAct => TurnActive && HasRolled && !IsRolling && !finishing && !introRunning;

    /// <summary>The owned card a right click on <paramref name="die"/> would use right now, or null.</summary>
    public CardInstance DieActionFor(Dice die)
    {
        if (!CanAct || !InPlay(die)) return null;

        foreach (CardInstance card in cards)
        {
            CardEffect effect = card?.Effect;
            if (effect == null || !effect.ActsOn(die.Value)) continue;
            // The last die can't go: the round needs something to score.
            if (effect.type == CardEffectType.DestroyFace && DiceInPlay.Count <= 1) continue;
            return card;
        }
        return null;
    }

    /// <summary>True when right clicking <paramref name="die"/> would use a card on it.</summary>
    public bool HasDieAction(Dice die) => DieActionFor(die) != null;

    /// <summary>Right click: uses the first owned card that acts on <paramref name="die"/>'s face.</summary>
    public void UseDieAction(Dice die)
    {
        CardInstance card = DieActionFor(die);
        if (card == null) return;

        CardEffect effect = card.Effect;
        DiceScore before = shownScore;
        switch (effect.type)
        {
            case CardEffectType.RerollFace:
                // Free: no reroll used. The landing scores it like any other throw.
                Throw(new List<Dice> { die });
                PlaySfx(rollSfx);
                UpdateUI();
                return;

            case CardEffectType.DestroyFace:
                int gained = Mathf.Max(0, card.Amount);
                RerollsLeft += gained;
                die.Remove();
                PlaySfx(destroySfx);
                if (gained > 0)
                    Popup(string.Format(popups.rerollsGainedFormat, gained), ScoreAnchor, popups.scoreOffset, popups.gainColor);
                break;

            case CardEffectType.FlipFace:
                die.Flip(effect.FlipOf(die.Value));
                PlaySfx(flipSfx);
                break;
        }

        UpdateUI();
        ShowRollPopups(before, shownScore);
    }

    /// <summary>Whether a right click could still change something about the dice on the table.</summary>
    private bool AnyDieAction()
    {
        foreach (Dice die in DiceInPlay)
            if (HasDieAction(die)) return true;
        return false;
    }

    /// <summary>
    /// Scores the dice and ends the round. A round before the last banks its points and the next round
    /// starts after <see cref="nextRoundDelay"/>; the last one checks the turn's total against the target
    /// and ends the turn after <see cref="finishDelay"/>.
    /// </summary>
    public void Finish()
    {
        if (!TurnActive || !HasRolled || IsRolling || finishing) return;
        finishing = true;

        int[] values = Values();
        DiceRoundResult round = new DiceRoundResult { score = Evaluate(values, cards), values = values };
        roundResults.Add(round);

        if (!IsLastRound)
        {
            UpdateUI();
            if (nextRoundDelay > 0f) finishTween = Tween.Delay(this, nextRoundDelay, manager => manager.NextRound());
            else NextRound();
            return;
        }

        int total = BankedScore + round.score.total;
        EndTurn(total, total >= Target, RiskyTarget > 0 && total >= RiskyTarget);
    }

    /// <summary>Shows the turn's result and hands it back after <see cref="finishDelay"/>.</summary>
    private void EndTurn(int total, bool passed, bool riskyPassed)
    {
        pendingResult = new DiceTurnResult
        {
            rounds = new List<DiceRoundResult>(roundResults),
            total = total,
            target = Target,
            passed = passed,
            riskyTarget = RiskyTarget,
            riskyPassed = riskyPassed,
        };

        if (resultText)
        {
            resultText.text = pendingResult.passed ? passedText : failedText;
            resultText.gameObject.SetActive(true);
        }

        PlaySfx(pendingResult.passed ? passedSfx : failedSfx);
        UpdateUI();
        if (pendingResult.riskyPassed) Popup(popups.cardUpgradedText, GoalAnchor, popups.goalOffset, popups.riskyGoalColor);

        if (finishDelay > 0f) finishTween = Tween.Delay(this, finishDelay, manager => manager.CompleteTurn());
        else CompleteTurn();
    }

    /// <summary>
    /// Banks the finished round's points and opens the turn's next round: the dice keep their faces and are
    /// thrown again straight away (<see cref="nextRoundDelay"/> was the pause).
    /// </summary>
    private void NextRound()
    {
        if (!TurnActive) return;

        int roundScore = roundResults[roundResults.Count - 1].score.total;
        BankedScore += roundScore;
        onRoundFinished?.Invoke(roundScore);

        PrepareRound(keepFaces: true);
        introRunning = false;
        Roll();
    }

    /// <summary>
    /// Debug: ends the turn on the spot, whatever the dice show and whichever round is on - lost (under the
    /// target), passed (the target, not the risky goal) or passed with the risky goal (upgrades the next cards).
    /// </summary>
    public void DebugEndTurn(bool passed, bool risky)
    {
        if (!TurnActive || pendingResult != null) return;
        if (runningTutorial)
        {
            h.Out("DiceManager: the tutorial is playing - the debug keys work once it's over.");
            return;
        }

        finishTween.Stop();
        firstRollTween.Stop();
        foreach (Dice die in dice)
            if (die) die.StopRoll();
        landingPending = 0;
        introRunning = false;
        finishing = true;

        // A round being banked (between rounds) is already listed; otherwise the dice on the table are the round.
        if (roundResults.Count < CurrentRound)
        {
            int[] values = Values();
            roundResults.Add(new DiceRoundResult { score = Evaluate(values, cards), values = values });
        }

        int scored = 0;
        foreach (DiceRoundResult round in roundResults) scored += round.score.total;

        risky &= passed;
        int total = risky ? Mathf.Max(Target, RiskyTarget)
            : passed ? Target
            : Mathf.Clamp(scored, 0, Mathf.Max(0, Target - 1));
        h.Out($"DiceManager: debug {(risky ? "risky win" : passed ? "win" : "loss")} with {total} (target {Target}, risky {RiskyTarget}).");
        EndTurn(total, passed, risky);
    }

    private void Update()
    {
        if (!TurnActive || !DebugHotkeys.Allowed(debugKeys)) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (Pressed(keyboard, debugLoseKey)) DebugEndTurn(passed: false, risky: false);
        else if (Pressed(keyboard, debugWinKey)) DebugEndTurn(passed: true, risky: false);
        else if (Pressed(keyboard, debugRiskyWinKey)) DebugEndTurn(passed: true, risky: true);
    }

    private static bool Pressed(Keyboard keyboard, Key key) => key != Key.None && keyboard[key].wasPressedThisFrame;

    /// <summary>Drops the turn without a result (the song ended mid-turn).</summary>
    public void CancelTurn()
    {
        if (!TurnActive) return;

        finishTween.Stop();
        firstRollTween.Stop();
        turnId++;
        introRunning = false;
        StopTutorial();
        foreach (Dice die in dice)
            if (die) die.StopRoll();

        TurnActive = false;
        finishing = false;
        landingPending = 0;
        turnCallback = null;
        pendingResult = null;
        roundResults.Clear();
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

        // A card's right click could still change the dice: the player finishes themselves.
        if (autoFinishWhenOutOfRerolls && RerollsLeft <= 0 && !AnyDieAction()) Finish();
    }

    private void CompleteTurn()
    {
        DiceTurnResult result = pendingResult;
        Action<DiceTurnResult> callback = turnCallback;

        pendingResult = null;
        turnCallback = null;
        TurnActive = false;
        finishing = false;
        if (hideTableOnFinish && tableRoot) tableRoot.SetActive(false);

        onRoundFinished?.Invoke(result.rounds[result.rounds.Count - 1].score.total);
        onTurnFinished?.Invoke(result.total);
        callback?.Invoke(result);
    }

    // ---------------------------------------------------------------------
    // Scoring
    // ---------------------------------------------------------------------

    public int[] Values()
    {
        List<int> values = new List<int>();
        foreach (Dice die in DiceInPlay) values.Add(die.Value);
        return values.ToArray();
    }

    /// <summary>
    /// How strong a hand is, 0..1: its points in the scoring table against the best hand there. Card bonuses
    /// don't count - a pair stays a pair.
    /// </summary>
    public float CombinationStrength(PokerDiceCombination combination)
    {
        int best = 0;
        foreach (CombinationScore entry in combinations)
            if (entry != null && entry.enabled) best = Mathf.Max(best, entry.points);

        CombinationScore score = ScoreFor(combination);
        return best > 0 && score != null ? Mathf.Clamp01((float)score.points / best) : 0f;
    }

    /// <summary>Points for a combination in the scoring table (null when it isn't listed or is turned off).</summary>
    public CombinationScore ScoreFor(PokerDiceCombination combination)
    {
        foreach (CombinationScore entry in combinations)
            if (entry != null && entry.enabled && entry.combination == combination) return entry;
        return null;
    }

    /// <summary>
    /// The best score <paramref name="values"/> can make: every hand they contain is scored with the
    /// cards' bonuses and the highest total wins. Order: hand bonus x hand / pattern multipliers (+ dice
    /// sum), + flat / hand / face / pattern / parity bonuses, x score / parity multipliers. With
    /// <see cref="noPointsWithoutCombination"/>, dice that make no hand score 0.
    /// </summary>
    public DiceScore Evaluate(IReadOnlyList<int> values, IReadOnlyList<CardInstance> ownedCards)
    {
        int diceSum = 0;
        foreach (int v in values) diceSum += v;

        DiceScore best = null;
        foreach (PokerDiceCombination hand in PokerDiceEvaluator.Contained(values))
        {
            // No hand, no points: the dice only score through a combination.
            if (noPointsWithoutCombination && hand == PokerDiceCombination.Nothing) continue;

            CombinationScore entry = ScoreFor(hand);
            if (entry == null) continue;

            // Hand multipliers scale the combination's bonus only: the dice sum is added after them.
            float handBonus = entry.points;
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
                            if (effect.Matches(hand)) handBonus *= card.Multiplier;
                            break;
                        case CardEffectType.FaceValueBonus:
                            foreach (int v in values)
                                if (v == effect.faceValue) bonus += card.Amount;
                            break;
                        case CardEffectType.ScoreMultiplier:
                            scoreMultiplier *= card.Multiplier;
                            break;
                        case CardEffectType.PatternBonus:
                            if (effect.PatternIn(values)) bonus += card.Amount;
                            break;
                        case CardEffectType.PatternMultiplier:
                            if (effect.PatternIn(values)) handBonus *= card.Multiplier;
                            break;
                        case CardEffectType.ParityBonus:
                            foreach (int v in values)
                                if (effect.IsOfParity(v)) bonus += card.Amount;
                            break;
                        case CardEffectType.ParityMultiplier:
                            if (values.Count > 0 && AllOfParity(values, effect)) scoreMultiplier *= card.Multiplier;
                            break;
                    }
                }

            float handPoints = handBonus + (addDiceSum ? diceSum : 0);
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

        if (best != null) return best;

        CombinationScore nothing = ScoreFor(PokerDiceCombination.Nothing);
        return new DiceScore
        {
            combination = PokerDiceCombination.Nothing,
            combinationName = nothing != null && !string.IsNullOrEmpty(nothing.displayName) ? nothing.displayName : "Nothing",
        };
    }

    private static bool AllOfParity(IReadOnlyList<int> values, CardEffect effect)
    {
        foreach (int v in values)
            if (!effect.IsOfParity(v)) return false;
        return true;
    }

    /// <summary>A round's rerolls: <see cref="rerollsPerRound"/> + Extra Rerolls cards - Extra Dice cards' cost.</summary>
    private int RerollsFor(IReadOnlyList<CardInstance> ownedCards)
    {
        int rerolls = rerollsPerRound;
        foreach (CardInstance card in ownedCards)
        {
            CardEffect effect = card?.Effect;
            if (effect == null) continue;
            if (effect.type == CardEffectType.ExtraRerolls) rerolls += Mathf.Max(0, card.Amount);
            else if (effect.type == CardEffectType.ExtraDice) rerolls -= Mathf.Max(0, effect.rerollCost);
        }
        return Mathf.Max(0, rerolls);
    }

    /// <summary>Dice on the table this turn: <see cref="startDiceCount"/> + Extra Dice cards.</summary>
    private int DiceCountFor(IReadOnlyList<CardInstance> ownedCards)
    {
        int count = startDiceCount;
        foreach (CardInstance card in ownedCards)
        {
            CardEffect effect = card?.Effect;
            if (effect != null && effect.type == CardEffectType.ExtraDice) count += Mathf.Max(0, card.Amount);
        }
        return Mathf.Max(1, count);
    }

    // ---------------------------------------------------------------------
    // UI / audio
    // ---------------------------------------------------------------------

    private void UpdateUI()
    {
        bool anyUnlocked = false;
        foreach (Dice die in DiceInPlay)
            if (!die.IsLocked) anyUnlocked = true;

        bool idle = TurnActive && !finishing && !IsRolling && !introRunning;

        if (rollButton) rollButton.interactable = idle && (!HasRolled || (RerollsLeft > 0 && anyUnlocked));
        if (rollButtonLabel) rollButtonLabel.text = HasRolled ? string.Format(rerollLabel, RerollsLeft) : firstRollLabel;
        // Never into the Finish button's own label: that one always says finishLabel.
        if (rerollsText && rerollsText != finishButtonLabel) rerollsText.text = string.Format(rerollsFormat, RerollsLeft);
        if (finishButtonLabel) finishButtonLabel.text = finishLabel;
        if (riskyTargetText) riskyTargetText.text = string.Format(riskyTargetFormat, RiskyTarget, Target);
        if (roundText)
        {
            roundText.gameObject.SetActive(RoundsInTurn > 1);
            roundText.text = string.Format(roundFormat, Mathf.Max(1, CurrentRound), RoundsInTurn);
        }

        // The round's score is what the dice on the table make right now - never added up over rolls. While
        // dice are in the air the last one stays up; once they land it counts from that to the new score
        // (10 -> 20, or down). On screen it sits on top of the rounds already banked this turn; the turn's
        // first round starts at 0 and counts up from there.
        if (!TurnActive || !HasRolled) shownScore = null;
        else if (!IsRolling) shownScore = Evaluate(Values(), cards);

        UpdateTargetText();

        // The round can be finished any time once the dice are down - target reached or not.
        if (finishButton) finishButton.interactable = idle && HasRolled;

        // One round: the score is the turn's total. Several: it is what the finished rounds banked (the round
        // being scored joins it as it finishes), and the running round's points get their own label.
        bool multiRound = RoundsInTurn > 1;
        CountingText counter = ScoreCounter;
        if (counter)
        {
            if (!multiRound)
            {
                if (shownScore == null && CurrentRound <= 1) counter.SetInstant(0);
                else counter.SetValue(TurnTotal(shownScore));
            }
            else
            {
                int accumulated = BankedScore + (finishing ? shownScore?.total ?? 0 : 0);
                if (CurrentRound <= 1 && !HasRolled) counter.SetInstant(0);
                else counter.SetValue(accumulated);

                // Red until the points banked over the rounds reach the minimum goal.
                scoreColor ??= counter.BaseColor;
                counter.BaseColor = Target <= 0 || accumulated >= Target ? scoreColor.Value : scoreBelowTargetColor;
            }
        }

        if (roundScoreText)
        {
            roundScoreText.gameObject.SetActive(TurnActive && multiRound);
            CountingText roundCounter = RoundScoreCounter;
            if (roundCounter)
            {
                // A fresh round starts at 0; then it counts to what the dice make.
                if (shownScore == null) roundCounter.SetInstant(0);
                else roundCounter.SetValue(shownScore.total);
            }
        }

        if (combinationText)
        {
            // Red while the dice make no hand that counts: a disabled hand falls back to Nothing.
            combinationColor ??= combinationText.color;
            bool proper = shownScore != null && shownScore.combination != PokerDiceCombination.Nothing;
            combinationText.color = shownScore == null || proper ? combinationColor.Value : noCombinationColor;
            combinationText.text = shownScore != null ? shownScore.combinationName : "";
        }
        SetCombinationShake(shownScore != null ? CombinationStrength(shownScore.combination) : 0f);

        // Whether a die may be locked just changed (a roll started or landed): hovered dice follow.
        foreach (Dice die in dice)
            if (die) die.RefreshOutline();
    }

    /// <summary>The turn's total with <paramref name="roundScore"/> as the running round's: banked rounds + it.</summary>
    private int TurnTotal(DiceScore roundScore) => BankedScore + (roundScore?.total ?? 0);

    /// <summary>
    /// The target text shows the target until the turn's total reaches it, then the risky goal - with a
    /// punch as it switches. Back to the target if a reroll drops the total under it.
    /// </summary>
    /// <summary>The turn's total on screen has reached <see cref="Target"/> (always true without a target).</summary>
    private bool TargetReached => Target <= 0 || ((shownScore != null || CurrentRound > 1) && TurnTotal(shownScore) >= Target);

    private void UpdateTargetText()
    {
        bool reached = Target > 0 && TargetReached;
        bool switched = reached && !targetShowsRisky;
        targetShowsRisky = reached;

        if (!targetText) return;
        targetText.text = string.Format(reached ? reachedTargetFormat : targetFormat,
            reached ? RiskyTarget : Target, reached ? Target : RiskyTarget);

        if (switched && targetSwitchPunch > 0f)
        {
            targetPunchTween.Stop();
            targetText.transform.localScale = targetTextScale ??= targetText.transform.localScale;
            targetPunchTween = Tween.PunchScale(targetText.transform, Vector3.one * targetSwitchPunch, 0.3f);
        }
    }

    // ---------------------------------------------------------------------
    // Popups
    // ---------------------------------------------------------------------

    // Empty anchors pop up at the dice parent (its pivot - the middle with the default centred pivot).
    private Transform ScoreAnchor => popups.scoreAnchor ? popups.scoreAnchor : Area;
    private Transform CombinationAnchor => popups.combinationAnchor ? popups.combinationAnchor : Area;
    private Transform GoalAnchor => popups.goalAnchor ? popups.goalAnchor : Area;

    /// <summary>
    /// After a landing: how much the score changed (+10 / -5), the hand the dice make, and a goal reached
    /// (the turn's total climbing past the target or the risky goal).
    /// </summary>
    private void ShowRollPopups(DiceScore before, DiceScore after)
    {
        if (!popups.enabled || after == null) return;

        int oldTotal = TurnTotal(before);
        int newTotal = TurnTotal(after);
        int change = newTotal - oldTotal;
        if (change != 0)
            Popup(string.Format(change > 0 ? popups.gainFormat : popups.lossFormat, Mathf.Abs(change)), ScoreAnchor,
                popups.scoreOffset, change > 0 ? popups.gainColor : popups.lossColor, bad: change < 0);

        if (after.combination != PokerDiceCombination.Nothing)
            Popup(after.combinationName, CombinationAnchor, popups.combinationOffset, popups.combinationColor);

        if (Target > 0 && oldTotal < Target && newTotal >= Target)
            Popup(popups.goalReachedText, GoalAnchor, popups.goalOffset, popups.goalColor);
        if (RiskyTarget > 0 && oldTotal < RiskyTarget && newTotal >= RiskyTarget)
            Popup(popups.riskyGoalReachedText, GoalAnchor, popups.goalOffset, popups.riskyGoalColor,
                popups.riskyGoalScale, popups.riskyGoalExtraHold);
    }

    /// <summary>
    /// Pops <paramref name="text"/> up at <paramref name="anchor"/> + <paramref name="offset"/> (canvas units),
    /// <see cref="DicePopups.interval"/> after the previous popup, <paramref name="scale"/> times its usual size and
    /// held <paramref name="extraHold"/> seconds longer. It plays <see cref="badAlertSfx"/> as it appears when
    /// <paramref name="bad"/>, else <see cref="goodAlertSfx"/>.
    /// </summary>
    private void Popup(string text, Transform anchor, Vector2 offset, Color color, float scale = 1f, float extraHold = 0f,
        bool bad = false)
    {
        AudioClip sfx = bad ? badAlertSfx : goodAlertSfx;
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
            CreatePopup(text, anchor, offset, color, scale, extraHold, sfx);
            return;
        }

        popupTweens.RemoveAll(tween => !tween.isAlive);
        popupTweens.Add(Tween.Delay(anchor, delay, a =>
        {
            if (TextAlertManager.Instance) CreatePopup(text, a, offset, color, scale, extraHold, sfx);
        }));
    }

    private static void CreatePopup(string text, Transform anchor, Vector2 offset, Color color, float scale, float extraHold,
        AudioClip sfx)
    {
        AlertDamageText alert = TextAlertManager.Instance.CreateTextAlert(text, anchor, color, offset);
        if (!alert) return;
        PlaySfx(sfx);
        // Before the alert's Start, which reads the hold to schedule its fade.
        alert.transform.localScale *= scale;
        alert.holdDuration += extraHold;
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
