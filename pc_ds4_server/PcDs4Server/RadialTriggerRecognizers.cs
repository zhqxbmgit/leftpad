namespace PcDs4Server;

public readonly record struct ActionDoubleTapResult(bool PassThrough, bool TriggerAccepted)
{
    public bool ConsumeCurrentEvent => !PassThrough;

    public static ActionDoubleTapResult Pass { get; } = new(true, false);
    public static ActionDoubleTapResult Consume { get; } = new(false, false);
    public static ActionDoubleTapResult AcceptTrigger { get; } = new(false, true);
}

public sealed class ActionDoubleTapRecognizer
{
    private enum RecognitionState
    {
        Idle,
        FirstDown,
        WaitingForSecondDown,
        ConsumingSecondTap
    }

    private readonly Func<TimeSpan> _doubleTapWindowProvider;
    private RecognitionState _state;
    private string? _action;
    private TimeSpan _firstUpTimestamp;

    public ActionDoubleTapRecognizer(TimeSpan doubleTapWindow)
        : this(() => doubleTapWindow)
    {
        if (doubleTapWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(doubleTapWindow), "The double-tap window must be positive.");
    }

    public ActionDoubleTapRecognizer(Func<TimeSpan> doubleTapWindowProvider)
    {
        _doubleTapWindowProvider = doubleTapWindowProvider ??
            throw new ArgumentNullException(nameof(doubleTapWindowProvider));
        if (_doubleTapWindowProvider() <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(doubleTapWindowProvider), "The double-tap window must be positive.");
    }

    public ActionDoubleTapResult Process(string protocolAction, bool isPressed, TimeSpan timestamp)
    {
        if (string.IsNullOrWhiteSpace(protocolAction))
        {
            Reset();
            return ActionDoubleTapResult.Pass;
        }

        if (_state == RecognitionState.ConsumingSecondTap)
        {
            if (!ActionsMatch(protocolAction))
                return ActionDoubleTapResult.Pass;

            if (!isPressed)
                Reset();

            return ActionDoubleTapResult.Consume;
        }

        if (_state == RecognitionState.WaitingForSecondDown)
        {
            TimeSpan elapsed = timestamp - _firstUpTimestamp;
            if (elapsed < TimeSpan.Zero || elapsed > _doubleTapWindowProvider() || !ActionsMatch(protocolAction))
            {
                Reset();
                return StartFresh(protocolAction, isPressed);
            }

            if (isPressed)
            {
                _state = RecognitionState.ConsumingSecondTap;
                return ActionDoubleTapResult.AcceptTrigger;
            }

            Reset();
            return ActionDoubleTapResult.Pass;
        }

        if (_state == RecognitionState.FirstDown)
        {
            if (!isPressed && ActionsMatch(protocolAction))
            {
                _state = RecognitionState.WaitingForSecondDown;
                _firstUpTimestamp = timestamp;
                return ActionDoubleTapResult.Pass;
            }

            Reset();
            return ActionDoubleTapResult.Pass;
        }

        return StartFresh(protocolAction, isPressed);
    }

    public void Reset()
    {
        _state = RecognitionState.Idle;
        _action = null;
        _firstUpTimestamp = default;
    }

    private ActionDoubleTapResult StartFresh(string protocolAction, bool isPressed)
    {
        if (isPressed)
        {
            _state = RecognitionState.FirstDown;
            _action = protocolAction;
        }

        return ActionDoubleTapResult.Pass;
    }

    private bool ActionsMatch(string protocolAction) =>
        string.Equals(_action, protocolAction, StringComparison.OrdinalIgnoreCase);
}

public readonly record struct MoveTapResult(bool TriggerAccepted);

public sealed class MoveTapRecognizer
{
    private readonly Func<TimeSpan> _doubleTapWindowProvider;
    private bool _moveDown;
    private bool _secondTapTimingEligible;
    private TimeSpan? _firstTapUpTimestamp;

    public MoveTapRecognizer(TimeSpan doubleTapWindow)
        : this(() => doubleTapWindow)
    {
        if (doubleTapWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(doubleTapWindow), "The double-tap window must be positive.");
    }

    public MoveTapRecognizer(Func<TimeSpan> doubleTapWindowProvider)
    {
        _doubleTapWindowProvider = doubleTapWindowProvider ??
            throw new ArgumentNullException(nameof(doubleTapWindowProvider));
        if (_doubleTapWindowProvider() <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(doubleTapWindowProvider), "The double-tap window must be positive.");
    }

    public MoveTapResult MoveDown(TimeSpan timestamp)
    {
        if (_moveDown)
        {
            Reset();
            return default;
        }

        _secondTapTimingEligible = false;
        if (_firstTapUpTimestamp is TimeSpan firstUp)
        {
            TimeSpan elapsed = timestamp - firstUp;
            if (elapsed < TimeSpan.Zero || elapsed > _doubleTapWindowProvider())
                _firstTapUpTimestamp = null;
            else
                _secondTapTimingEligible = true;
        }

        _moveDown = true;
        return default;
    }

    public MoveTapResult MoveUp(TimeSpan timestamp, bool directionCapturedDuringHold)
    {
        if (!_moveDown)
        {
            Reset();
            return default;
        }

        _moveDown = false;

        if (directionCapturedDuringHold)
        {
            _firstTapUpTimestamp = null;
            _secondTapTimingEligible = false;
            return default;
        }

        if (_secondTapTimingEligible)
        {
            _firstTapUpTimestamp = null;
            _secondTapTimingEligible = false;
            return new MoveTapResult(TriggerAccepted: true);
        }

        _firstTapUpTimestamp = timestamp;
        _secondTapTimingEligible = false;
        return default;
    }

    public void Reset()
    {
        _moveDown = false;
        _secondTapTimingEligible = false;
        _firstTapUpTimestamp = null;
    }
}
