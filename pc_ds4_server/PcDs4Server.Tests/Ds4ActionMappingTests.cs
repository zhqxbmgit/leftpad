using Nefarius.ViGEm.Client.Targets.DualShock4;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class Ds4ActionMappingTests
{
    [Theory]
    [InlineData("cross")]
    [InlineData("circle")]
    [InlineData("square")]
    [InlineData("triangle")]
    [InlineData("l1")]
    [InlineData("r1")]
    [InlineData("l3")]
    [InlineData("r3")]
    public void DigitalActions_MapToRealViGEmButtons(string protocolKey)
    {
        Assert.True(Ds4ActionMapper.TryGet(protocolKey, out Ds4ActionMapping mapping));

        Assert.Equal(Ds4ActionKind.DigitalButton, mapping.Kind);
        Assert.Same(ExpectedButton(protocolKey), mapping.DigitalButton);
        Assert.Null(mapping.Trigger);
    }

    [Theory]
    [InlineData("l2")]
    [InlineData("r2")]
    public void TriggerActions_MapToRealViGEmSliders(string protocolKey)
    {
        Assert.True(Ds4ActionMapper.TryGet(protocolKey, out Ds4ActionMapping mapping));

        Assert.Equal(Ds4ActionKind.AnalogTrigger, mapping.Kind);
        Assert.Same(ExpectedTrigger(protocolKey), mapping.Trigger);
        Assert.Null(mapping.DigitalButton);
    }

    [Fact]
    public void L2Down_OutputsFullAnalogTrigger()
    {
        var state = new Ds4ControlState();
        Assert.True(Ds4ActionMapper.TryGet("l2", out Ds4ActionMapping mapping));
        Assert.True(Ds4ActionMapper.TryGetPressedState("down", out bool isPressed));

        state.SetTrigger(mapping.Trigger!, Ds4ActionMapper.GetTriggerValue(isPressed));

        Assert.Equal(byte.MaxValue, state.LeftTrigger);
    }

    [Fact]
    public void L2Up_OutputsReleasedAnalogTrigger()
    {
        var state = new Ds4ControlState();
        state.SetTrigger(DualShock4Slider.LeftTrigger, byte.MaxValue);
        Assert.True(Ds4ActionMapper.TryGet("l2", out Ds4ActionMapping mapping));
        Assert.True(Ds4ActionMapper.TryGetPressedState("up", out bool isPressed));

        state.SetTrigger(mapping.Trigger!, Ds4ActionMapper.GetTriggerValue(isPressed));

        Assert.Equal(byte.MinValue, state.LeftTrigger);
    }

    [Fact]
    public void R2Down_OutputsFullAnalogTrigger()
    {
        var state = new Ds4ControlState();
        Assert.True(Ds4ActionMapper.TryGet("r2", out Ds4ActionMapping mapping));
        Assert.True(Ds4ActionMapper.TryGetPressedState("down", out bool isPressed));

        state.SetTrigger(mapping.Trigger!, Ds4ActionMapper.GetTriggerValue(isPressed));

        Assert.Equal(byte.MaxValue, state.RightTrigger);
    }

    [Fact]
    public void R2Up_OutputsReleasedAnalogTrigger()
    {
        var state = new Ds4ControlState();
        state.SetTrigger(DualShock4Slider.RightTrigger, byte.MaxValue);
        Assert.True(Ds4ActionMapper.TryGet("r2", out Ds4ActionMapping mapping));
        Assert.True(Ds4ActionMapper.TryGetPressedState("up", out bool isPressed));

        state.SetTrigger(mapping.Trigger!, Ds4ActionMapper.GetTriggerValue(isPressed));

        Assert.Equal(byte.MinValue, state.RightTrigger);
    }

    [Fact]
    public void ReleaseAllControls_ReleasesEveryDigitalButton()
    {
        var state = new Ds4ControlState();
        state.SetDigitalButton(DualShock4Button.Cross, true);
        state.SetDigitalButton(DualShock4Button.ShoulderLeft, true);

        Ds4ControlRelease release = state.ReleaseAll(Ds4ControlResetReason.Disconnect);

        Assert.Equal(
            new[] { DualShock4Button.Cross, DualShock4Button.ShoulderLeft },
            release.DigitalButtons);
        Assert.Empty(state.ActiveButtons);
    }

    [Fact]
    public void ReleaseAllControls_ResetsL2()
    {
        var state = new Ds4ControlState();
        state.SetTrigger(DualShock4Slider.LeftTrigger, byte.MaxValue);

        Ds4ControlRelease release = state.ReleaseAll(Ds4ControlResetReason.Disconnect);

        Assert.True(release.ResetLeftTrigger);
        Assert.Equal(byte.MinValue, state.LeftTrigger);
    }

    [Fact]
    public void ReleaseAllControls_ResetsR2()
    {
        var state = new Ds4ControlState();
        state.SetTrigger(DualShock4Slider.RightTrigger, byte.MaxValue);

        Ds4ControlRelease release = state.ReleaseAll(Ds4ControlResetReason.Disconnect);

        Assert.True(release.ResetRightTrigger);
        Assert.Equal(byte.MinValue, state.RightTrigger);
    }

    [Fact]
    public void ReleaseAllControls_ResetsDPadToNeutral()
    {
        var state = new Ds4ControlState();
        state.SetDPadDirection(DualShock4DPadDirection.South);

        Ds4ControlRelease release = state.ReleaseAll(Ds4ControlResetReason.OutputFailure);

        Assert.True(release.ResetDPad);
        Assert.Equal(DualShock4DPadDirection.None, state.DPadDirection);
    }

    [Theory]
    [InlineData(Ds4ControlResetReason.Disconnect)]
    [InlineData(Ds4ControlResetReason.SessionReplacement)]
    [InlineData(Ds4ControlResetReason.ServiceStop)]
    public void SafetyReset_ReleasesDigitalAndBothTriggers(Ds4ControlResetReason reason)
    {
        var state = ActiveControls();

        Ds4ControlRelease release = state.ReleaseAll(reason);

        Assert.Single(release.DigitalButtons);
        Assert.True(release.ResetLeftTrigger);
        Assert.True(release.ResetRightTrigger);
        Assert.Empty(state.ActiveButtons);
        Assert.Equal(byte.MinValue, state.LeftTrigger);
        Assert.Equal(byte.MinValue, state.RightTrigger);
        Assert.Equal(reason, state.LastResetReason);
    }

    [Fact]
    public void UnknownAction_IsIgnoredSafely()
    {
        Assert.False(Ds4ActionMapper.TryGet("unknown", out _));
    }

    [Fact]
    public void ExistingCrossBehavior_IsPreserved()
    {
        Assert.True(Ds4ActionMapper.TryGet("CROSS", out Ds4ActionMapping mapping));
        Assert.Equal("cross", mapping.ProtocolKey);
        Assert.Same(DualShock4Button.Cross, mapping.DigitalButton);
    }

    [Fact]
    public void RepeatedL2DownThenUp_DoesNotLeaveTriggerActive()
    {
        var state = new Ds4ControlState();
        state.SetTrigger(DualShock4Slider.LeftTrigger, byte.MaxValue);
        state.SetTrigger(DualShock4Slider.LeftTrigger, byte.MaxValue);

        state.SetTrigger(DualShock4Slider.LeftTrigger, byte.MinValue);

        Assert.Equal(byte.MinValue, state.LeftTrigger);
    }

    [Fact]
    public void RepeatedR2Up_IsIdempotent()
    {
        var state = new Ds4ControlState();

        state.SetTrigger(DualShock4Slider.RightTrigger, byte.MinValue);
        state.SetTrigger(DualShock4Slider.RightTrigger, byte.MinValue);

        Assert.Equal(byte.MinValue, state.RightTrigger);
    }

    [Fact]
    public void UnknownProtocolAction_IsIgnoredSafely()
    {
        Assert.False(Ds4ActionMapper.TryGetPressedState("stop", out _));
        Assert.False(Ds4ActionMapper.TryGetPressedState("", out _));
        Assert.False(Ds4ActionMapper.TryGetPressedState(null, out _));
    }

    private static Ds4ControlState ActiveControls()
    {
        var state = new Ds4ControlState();
        state.SetDigitalButton(DualShock4Button.ShoulderRight, true);
        state.SetTrigger(DualShock4Slider.LeftTrigger, byte.MaxValue);
        state.SetTrigger(DualShock4Slider.RightTrigger, byte.MaxValue);
        return state;
    }

    private static DualShock4Button ExpectedButton(string protocolKey)
    {
        return protocolKey switch
        {
            "cross" => DualShock4Button.Cross,
            "circle" => DualShock4Button.Circle,
            "square" => DualShock4Button.Square,
            "triangle" => DualShock4Button.Triangle,
            "l1" => DualShock4Button.ShoulderLeft,
            "r1" => DualShock4Button.ShoulderRight,
            "l3" => DualShock4Button.ThumbLeft,
            "r3" => DualShock4Button.ThumbRight,
            _ => throw new ArgumentOutOfRangeException(nameof(protocolKey))
        };
    }

    private static DualShock4Slider ExpectedTrigger(string protocolKey)
    {
        return protocolKey switch
        {
            "l2" => DualShock4Slider.LeftTrigger,
            "r2" => DualShock4Slider.RightTrigger,
            _ => throw new ArgumentOutOfRangeException(nameof(protocolKey))
        };
    }
}
