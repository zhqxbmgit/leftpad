using Xunit;

namespace PcDs4Server.Tests;

public sealed class ActionDoubleTapRecognizerTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(300);

    [Fact]
    public void SingleTap_PassesThroughImmediatelyWithoutTriggering()
    {
        var recognizer = new ActionDoubleTapRecognizer(Window);

        ActionDoubleTapResult down = recognizer.Process("cross", isPressed: true, Ms(0));
        ActionDoubleTapResult up = recognizer.Process("cross", isPressed: false, Ms(20));

        Assert.Equal(ActionDoubleTapResult.Pass, down);
        Assert.Equal(ActionDoubleTapResult.Pass, up);
    }

    [Fact]
    public void DoubleTap_PassesFirstTapAndConsumesSecondTap()
    {
        var recognizer = FirstCrossTapCompletedAt(Ms(20));

        ActionDoubleTapResult secondDown = recognizer.Process("cross", isPressed: true, Ms(100));
        ActionDoubleTapResult secondUp = recognizer.Process("cross", isPressed: false, Ms(120));

        Assert.False(secondDown.PassThrough);
        Assert.True(secondDown.ConsumeCurrentEvent);
        Assert.True(secondDown.TriggerAccepted);
        Assert.False(secondUp.PassThrough);
        Assert.True(secondUp.ConsumeCurrentEvent);
        Assert.False(secondUp.TriggerAccepted);
    }

    [Fact]
    public void FirstTap_RequiresNoBufferedEventOrDeferredResult()
    {
        var recognizer = new ActionDoubleTapRecognizer(Window);

        Assert.True(recognizer.Process("l2", true, Ms(0)).PassThrough);
        Assert.True(recognizer.Process("l2", false, Ms(1)).PassThrough);
    }

    [Fact]
    public void SecondTapAfterWindow_StartsANewPassThroughTap()
    {
        var recognizer = FirstCrossTapCompletedAt(Ms(20));

        ActionDoubleTapResult down = recognizer.Process("cross", true, Ms(321));
        ActionDoubleTapResult up = recognizer.Process("cross", false, Ms(340));

        Assert.Equal(ActionDoubleTapResult.Pass, down);
        Assert.Equal(ActionDoubleTapResult.Pass, up);
    }

    [Fact]
    public void DifferentActions_CannotFormADoubleTap()
    {
        var recognizer = FirstCrossTapCompletedAt(Ms(20));

        Assert.Equal(ActionDoubleTapResult.Pass, recognizer.Process("circle", true, Ms(50)));
        Assert.Equal(ActionDoubleTapResult.Pass, recognizer.Process("circle", false, Ms(70)));
        ActionDoubleTapResult crossDown = recognizer.Process("cross", true, Ms(90));

        Assert.Equal(ActionDoubleTapResult.Pass, crossDown);
    }

    [Fact]
    public void ActionMatching_IsCaseInsensitive()
    {
        var recognizer = FirstCrossTapCompletedAt(Ms(20));

        ActionDoubleTapResult result = recognizer.Process("CROSS", true, Ms(50));

        Assert.True(result.TriggerAccepted);
        Assert.True(result.ConsumeCurrentEvent);
    }

    [Fact]
    public void DuplicateFirstDown_ResetsBeforeANewCompleteTapCanStart()
    {
        var recognizer = new ActionDoubleTapRecognizer(Window);

        Assert.Equal(ActionDoubleTapResult.Pass, recognizer.Process("square", true, Ms(0)));
        Assert.Equal(ActionDoubleTapResult.Pass, recognizer.Process("square", true, Ms(10)));
        Assert.Equal(ActionDoubleTapResult.Pass, recognizer.Process("square", false, Ms(20)));
        Assert.Equal(ActionDoubleTapResult.Pass, recognizer.Process("square", true, Ms(50)));
        Assert.Equal(ActionDoubleTapResult.Pass, recognizer.Process("square", false, Ms(60)));
        ActionDoubleTapResult nextDown = recognizer.Process("square", true, Ms(70));

        Assert.True(nextDown.TriggerAccepted);
    }

    [Fact]
    public void OrphanUp_DoesNotTriggerAndNextTapCanStartNormally()
    {
        var recognizer = new ActionDoubleTapRecognizer(Window);

        Assert.Equal(ActionDoubleTapResult.Pass, recognizer.Process("triangle", false, Ms(0)));
        Assert.Equal(ActionDoubleTapResult.Pass, recognizer.Process("triangle", true, Ms(10)));
        Assert.Equal(ActionDoubleTapResult.Pass, recognizer.Process("triangle", false, Ms(20)));
    }

    [Fact]
    public void ExtraUpAfterFirstTap_CancelsPendingCandidate()
    {
        var recognizer = FirstCrossTapCompletedAt(Ms(20));

        Assert.Equal(ActionDoubleTapResult.Pass, recognizer.Process("cross", false, Ms(30)));
        ActionDoubleTapResult down = recognizer.Process("cross", true, Ms(40));

        Assert.Equal(ActionDoubleTapResult.Pass, down);
    }

    [Fact]
    public void ResetBetweenTaps_PreventsTrigger()
    {
        var recognizer = FirstCrossTapCompletedAt(Ms(20));
        recognizer.Reset();

        Assert.Equal(ActionDoubleTapResult.Pass, recognizer.Process("cross", true, Ms(50)));
        Assert.Equal(ActionDoubleTapResult.Pass, recognizer.Process("cross", false, Ms(70)));
    }

    [Fact]
    public void ConsumedSecondUp_RemainsConsumedEvenAfterWindowExpires()
    {
        var recognizer = FirstCrossTapCompletedAt(Ms(20));
        Assert.True(recognizer.Process("cross", true, Ms(50)).TriggerAccepted);

        ActionDoubleTapResult result = recognizer.Process("cross", false, Ms(1000));

        Assert.Equal(ActionDoubleTapResult.Consume, result);
    }

    [Fact]
    public void DuplicateConsumedDown_DoesNotRetriggerAndMatchingUpIsStillConsumed()
    {
        var recognizer = FirstCrossTapCompletedAt(Ms(20));
        Assert.True(recognizer.Process("cross", true, Ms(50)).TriggerAccepted);

        ActionDoubleTapResult duplicateDown = recognizer.Process("cross", true, Ms(55));
        ActionDoubleTapResult up = recognizer.Process("cross", false, Ms(60));

        Assert.Equal(ActionDoubleTapResult.Consume, duplicateDown);
        Assert.Equal(ActionDoubleTapResult.Consume, up);
    }

    [Fact]
    public void CompletedDoubleTap_ReturnsToStableState()
    {
        var recognizer = FirstCrossTapCompletedAt(Ms(20));
        recognizer.Process("cross", true, Ms(50));
        recognizer.Process("cross", false, Ms(60));

        Assert.Equal(ActionDoubleTapResult.Pass, recognizer.Process("cross", true, Ms(80)));
        Assert.Equal(ActionDoubleTapResult.Pass, recognizer.Process("cross", false, Ms(90)));
        Assert.True(recognizer.Process("cross", true, Ms(100)).TriggerAccepted);
    }

    [Fact]
    public void TimestampMovingBackward_DoesNotTrigger()
    {
        var recognizer = FirstCrossTapCompletedAt(Ms(100));

        ActionDoubleTapResult result = recognizer.Process("cross", true, Ms(90));

        Assert.Equal(ActionDoubleTapResult.Pass, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void InvalidAction_ResetsPendingCandidate(string action)
    {
        var recognizer = FirstCrossTapCompletedAt(Ms(20));

        Assert.Equal(ActionDoubleTapResult.Pass, recognizer.Process(action, true, Ms(30)));
        Assert.Equal(ActionDoubleTapResult.Pass, recognizer.Process("cross", true, Ms(40)));
    }

    [Fact]
    public void NonPositiveWindow_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActionDoubleTapRecognizer(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActionDoubleTapRecognizer(Ms(-1)));
    }

    private static ActionDoubleTapRecognizer FirstCrossTapCompletedAt(TimeSpan upTimestamp)
    {
        var recognizer = new ActionDoubleTapRecognizer(Window);
        recognizer.Process("cross", true, TimeSpan.Zero);
        recognizer.Process("cross", false, upTimestamp);
        return recognizer;
    }

    private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
}

public sealed class MoveTapRecognizerTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(300);

    [Fact]
    public void TwoValidMoveTapsWithinWindow_TriggerOnSecondUp()
    {
        var recognizer = FirstValidTapCompletedAt(Ms(20));

        MoveTapResult secondDown = recognizer.MoveDown(Ms(100));
        MoveTapResult secondUp = recognizer.MoveUp(Ms(120), directionCapturedDuringHold: false);

        Assert.False(secondDown.TriggerAccepted);
        Assert.True(secondUp.TriggerAccepted);
    }

    [Fact]
    public void SingleValidMoveTap_DoesNotTrigger()
    {
        var recognizer = new MoveTapRecognizer(Window);

        Assert.False(recognizer.MoveDown(Ms(0)).TriggerAccepted);
        Assert.False(recognizer.MoveUp(Ms(20), false).TriggerAccepted);
    }

    [Fact]
    public void MoveDoubleTapAfterWindow_DoesNotTrigger()
    {
        var recognizer = FirstValidTapCompletedAt(Ms(20));

        recognizer.MoveDown(Ms(321));
        MoveTapResult result = recognizer.MoveUp(Ms(340), false);

        Assert.False(result.TriggerAccepted);
    }

    [Fact]
    public void DirectionalFirstHold_DoesNotBecomeFirstTap()
    {
        var recognizer = new MoveTapRecognizer(Window);
        recognizer.MoveDown(Ms(0));
        recognizer.MoveUp(Ms(20), directionCapturedDuringHold: true);

        recognizer.MoveDown(Ms(50));
        MoveTapResult result = recognizer.MoveUp(Ms(70), false);

        Assert.False(result.TriggerAccepted);
    }

    [Fact]
    public void DirectionalSecondHold_CancelsFirstTapCandidate()
    {
        var recognizer = FirstValidTapCompletedAt(Ms(20));
        recognizer.MoveDown(Ms(50));

        MoveTapResult directionalUp = recognizer.MoveUp(Ms(70), directionCapturedDuringHold: true);
        recognizer.MoveDown(Ms(90));
        MoveTapResult nextUp = recognizer.MoveUp(Ms(110), directionCapturedDuringHold: false);

        Assert.False(directionalUp.TriggerAccepted);
        Assert.False(nextUp.TriggerAccepted);
    }

    [Fact]
    public void ResetBetweenMoveTaps_PreventsTrigger()
    {
        var recognizer = FirstValidTapCompletedAt(Ms(20));
        recognizer.Reset();

        recognizer.MoveDown(Ms(50));
        Assert.False(recognizer.MoveUp(Ms(70), false).TriggerAccepted);
    }

    [Fact]
    public void OrphanMoveUp_DoesNotTriggerAndResetsCandidate()
    {
        var recognizer = FirstValidTapCompletedAt(Ms(20));

        Assert.False(recognizer.MoveUp(Ms(30), false).TriggerAccepted);
        recognizer.MoveDown(Ms(50));
        Assert.False(recognizer.MoveUp(Ms(70), false).TriggerAccepted);
    }

    [Fact]
    public void DuplicateMoveDown_DoesNotTriggerAndResetsCandidate()
    {
        var recognizer = FirstValidTapCompletedAt(Ms(20));

        Assert.False(recognizer.MoveDown(Ms(50)).TriggerAccepted);
        Assert.False(recognizer.MoveDown(Ms(55)).TriggerAccepted);
        Assert.False(recognizer.MoveUp(Ms(70), false).TriggerAccepted);
        Assert.False(recognizer.MoveDown(Ms(80)).TriggerAccepted);
        Assert.False(recognizer.MoveUp(Ms(90), false).TriggerAccepted);
        Assert.False(recognizer.MoveDown(Ms(100)).TriggerAccepted);
        Assert.True(recognizer.MoveUp(Ms(110), false).TriggerAccepted);
    }

    [Fact]
    public void TriggerNeverOccursOnSecondMoveDown()
    {
        var recognizer = FirstValidTapCompletedAt(Ms(20));

        MoveTapResult result = recognizer.MoveDown(Ms(50));

        Assert.False(result.TriggerAccepted);
    }

    [Fact]
    public void SecondMoveTapMustCompleteWithinWindow()
    {
        var recognizer = FirstValidTapCompletedAt(Ms(20));
        recognizer.MoveDown(Ms(50));

        MoveTapResult result = recognizer.MoveUp(Ms(321), false);

        Assert.False(result.TriggerAccepted);
    }

    [Fact]
    public void CompletedMoveDoubleTap_ReturnsToStableState()
    {
        var recognizer = FirstValidTapCompletedAt(Ms(20));
        recognizer.MoveDown(Ms(50));
        Assert.True(recognizer.MoveUp(Ms(70), false).TriggerAccepted);

        recognizer.MoveDown(Ms(90));
        Assert.False(recognizer.MoveUp(Ms(110), false).TriggerAccepted);
    }

    [Fact]
    public void TimestampMovingBackward_DoesNotTrigger()
    {
        var recognizer = FirstValidTapCompletedAt(Ms(100));
        recognizer.MoveDown(Ms(90));

        Assert.False(recognizer.MoveUp(Ms(95), false).TriggerAccepted);
    }

    [Fact]
    public void NonPositiveWindow_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MoveTapRecognizer(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MoveTapRecognizer(Ms(-1)));
    }

    private static MoveTapRecognizer FirstValidTapCompletedAt(TimeSpan upTimestamp)
    {
        var recognizer = new MoveTapRecognizer(Window);
        recognizer.MoveDown(TimeSpan.Zero);
        recognizer.MoveUp(upTimestamp, directionCapturedDuringHold: false);
        return recognizer;
    }

    private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
}
