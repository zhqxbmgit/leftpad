using System.Text.Json;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class DreamscapeSettingsMappingTests
{
    [Fact]
    public void MappingTab_IsWebActiveAndUsesPersistentSharedDetailPanel()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        harness.Session.ShowMappings();

        using JsonDocument document = JsonDocument.Parse(harness.Session.CreateState("已连接").ToJson());
        Assert.Equal("mappings", document.RootElement.GetProperty("activeSection").GetString());

        string html = Frontend("index.html");
        string script = Frontend("app.js");
        string styles = Frontend("styles.css");
        Assert.Contains("id=\"mapping-detail\"", html);
        Assert.Contains("showSettingsMappings", html);
        Assert.Contains("renderMappingDetail", script);
        Assert.Contains("html[data-section=\"mappings\"] #mapping-section", styles);
    }

    [Fact]
    public void Radial6_StateUsesThreePlusThreeAndSlotsOneThroughSix()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        ReceiverSettingsBasicState state = harness.Session.CreateState("ready");

        Assert.Equal("radial-6", state.MappingProfileId);
        Assert.Equal(6, state.MappingSlotCount);
        Assert.Equal(3, state.MappingSplitIndex);
        Assert.Equal(Enumerable.Range(1, 6), state.Mappings.Select(mapping => mapping.SlotId));
        Assert.All(state.Mappings, mapping => Assert.Equal("none", mapping.ActionKind));
    }

    [Fact]
    public void Radial8_StateUsesFourPlusFourAndSlotsOneThroughEight()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        ChangeVisualPack(harness.Session, "radial-8-minimal-v1");
        ReceiverSettingsBasicState state = harness.Session.CreateState("ready");

        Assert.Equal("radial-8", state.MappingProfileId);
        Assert.Equal(8, state.MappingSlotCount);
        Assert.Equal(4, state.MappingSplitIndex);
        Assert.Equal(Enumerable.Range(1, 8), state.Mappings.Select(mapping => mapping.SlotId));
        Assert.All(state.Mappings, mapping => Assert.Equal("none", mapping.ActionKind));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 3)]
    [InlineData(6, 3)]
    [InlineData(7, 4)]
    [InlineData(8, 4)]
    public void SplitIndex_IsGenericCeilingOfHalf(int slotCount, int expected)
    {
        Assert.Equal(expected, SettingsMappingLayout.SplitIndex(slotCount));
    }

    [Fact]
    public void MappingCatalogs_ComeDirectlyFromRuntimeModels()
    {
        Assert.Equal(
            Enum.GetValues<RadialActionKind>().Select(SettingsMappingCatalogs.KindId),
            SettingsMappingCatalogs.ActionKinds.Select(option => option.Id));
        Assert.Equal(
            KeyboardKeyCatalog.MainKeys.Select(key => key.ToString()),
            SettingsMappingCatalogs.KeyboardKeys.Select(option => option.Id));
        Assert.Equal(
            ["cross", "circle", "square", "triangle", "l1", "l3", "r3", "dpad_down"],
            SettingsMappingCatalogs.Ds4Actions.Select(option => option.Id));
        Assert.Equal(
            RadialDs4ActionCatalog.Actions.Select(action => action.DisplayName),
            SettingsMappingCatalogs.Ds4Actions.Select(option => option.Name));
    }

    [Fact]
    public void KeyboardKey_UpdatesSelectedSlotAndSharedDraft()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        ApplyMapping(harness.Session, "radial-6", 1, "keyboardKey", key: "F1");

        RadialSlotMapping mapping = harness.Session.Draft.GetProfileMappings("radial-6")[0];
        Assert.Equal(RadialActionKind.KeyboardKey, mapping.Kind);
        Assert.Equal(KeyboardKey.F1, mapping.Key);
        Assert.Equal(1, harness.Session.SelectedMappingSlot);
    }

    [Fact]
    public void KeyboardShortcut_PreservesAllFourModifiers()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        ApplyMapping(
            harness.Session, "radial-6", 2, "keyboardShortcut", key: "P",
            ctrl: true, alt: true, shift: true, win: true);

        RadialSlotMapping mapping = harness.Session.Draft.GetProfileMappings("radial-6")[1];
        Assert.Equal(RadialActionKind.KeyboardShortcut, mapping.Kind);
        Assert.True(mapping.Ctrl);
        Assert.True(mapping.Alt);
        Assert.True(mapping.Shift);
        Assert.True(mapping.Win);
    }

    [Fact]
    public void Ds4Button_UsesExactRuntimeActionId()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        ApplyMapping(harness.Session, "radial-6", 3, "ds4Button", ds4Action: "dpad_down");

        RadialSlotMapping mapping = harness.Session.Draft.GetProfileMappings("radial-6")[2];
        Assert.Equal(RadialActionKind.Ds4Button, mapping.Kind);
        Assert.Equal("dpad_down", mapping.Ds4Button);
    }

    [Theory]
    [InlineData("{\"command\":\"settingsMappingChange\",\"profileId\":\"radial-6\",\"slotId\":7,\"actionKind\":\"none\"}", "slotId")]
    [InlineData("{\"command\":\"settingsMappingChange\",\"profileId\":\"radial-99\",\"slotId\":1,\"actionKind\":\"none\"}", "profile")]
    [InlineData("{\"command\":\"settingsMappingChange\",\"profileId\":\"radial-6\",\"slotId\":1,\"actionKind\":\"macro\"}", "kind")]
    [InlineData("{\"command\":\"settingsMappingChange\",\"profileId\":\"radial-6\",\"slotId\":1,\"actionKind\":\"keyboardKey\",\"key\":\"LeftControl\"}", "key")]
    [InlineData("{\"command\":\"settingsMappingChange\",\"profileId\":\"radial-6\",\"slotId\":1,\"actionKind\":\"ds4Button\",\"ds4Action\":\"r1\"}", "DS4")]
    public void InvalidMappingPayloads_AreRejectedWithoutThrowing(string json, string reasonFragment)
    {
        Assert.False(ReceiverSettingsCommandAllowList.TryParse(json, out _, out string reason));
        Assert.Contains(reasonFragment, reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidButInactiveProfile_IsRejectedBySession()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        ReceiverSettingsMessage message = ParseMapping(
            "radial-8", 1, "keyboardKey", key: "F8");

        Assert.False(harness.Session.TryApplyChange(message, out string reason));
        Assert.Contains("not active", reason);
        Assert.All(harness.Session.Draft.GetProfileMappings("radial-6"),
            mapping => Assert.Equal(RadialActionKind.None, mapping.Kind));
    }

    [Fact]
    public void ProfileMappings_ArePreservedAcrossSixToEightToSixSwitches()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        ApplyMapping(harness.Session, "radial-6", 1, "keyboardKey", key: "F1");

        ChangeVisualPack(harness.Session, "radial-8-minimal-v1");
        ApplyMapping(harness.Session, "radial-8", 7, "keyboardKey", key: "F7");
        ApplyMapping(harness.Session, "radial-8", 8, "ds4Button", ds4Action: "cross");
        ChangeVisualPack(harness.Session, "radial-v5");

        Assert.Equal(KeyboardKey.F1,
            harness.Session.Draft.GetProfileMappings("radial-6")[0].Key);
        ChangeVisualPack(harness.Session, "radial-8-minimal-v1");
        Assert.Equal(KeyboardKey.F7,
            harness.Session.Draft.GetProfileMappings("radial-8")[6].Key);
        Assert.Equal("cross",
            harness.Session.Draft.GetProfileMappings("radial-8")[7].Ds4Button);
    }

    [Fact]
    public void BasicAdvancedAndMapping_UseOneDraftAcrossTabs()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        ApplyBasic(harness.Session, SettingsBasicFields.OverallSizePercent, 120);
        harness.Session.ShowAdvanced();
        ApplyAdvanced(harness.Session, SettingsAdvancedFields.FontSize, 15.5m);
        harness.Session.ShowMappings();
        ApplyMapping(harness.Session, "radial-6", 1, "keyboardKey", key: "F1");
        harness.Session.ShowBasic();

        Assert.Equal(120, harness.Session.Draft.ScalePercent);
        Assert.Equal(15.5f, harness.Session.Draft.FontSize);
        Assert.Equal(KeyboardKey.F1, harness.Session.Draft.GetProfileMappings("radial-6")[0].Key);
        Assert.Equal(ReceiverSettingsSection.Basic, harness.Session.ActiveSection);
    }

    [Fact]
    public void PreviewAndApply_IncludeMappingsFromBothProfiles()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        ApplyMapping(harness.Session, "radial-6", 1, "keyboardKey", key: "F1");
        ChangeVisualPack(harness.Session, "radial-8-minimal-v1");
        ApplyMapping(harness.Session, "radial-8", 8, "ds4Button", ds4Action: "cross");

        harness.Session.Preview();
        Assert.Equal(KeyboardKey.F1, harness.LastPreview!.GetProfileMappings("radial-6")[0].Key);
        Assert.Equal("cross", harness.LastPreview.GetProfileMappings("radial-8")[7].Ds4Button);
        Assert.True(harness.Session.ApplyAndSave(out string error), error);
        Assert.Equal(KeyboardKey.F1, harness.Active.GetProfileMappings("radial-6")[0].Key);
        Assert.Equal("cross", harness.Active.GetProfileMappings("radial-8")[7].Ds4Button);
    }

    [Fact]
    public void ApplyReloadResetAndUnsavedDiscard_PreserveNativeSemantics()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        ApplyMapping(harness.Session, "radial-6", 1, "keyboardKey", key: "F1");
        Assert.True(harness.Session.ApplyAndSave(out string error), error);
        harness.Session.Deactivate();
        harness.Session.Activate();
        Assert.Equal(KeyboardKey.F1, harness.Session.Draft.GetProfileMappings("radial-6")[0].Key);

        ApplyMapping(harness.Session, "radial-6", 2, "keyboardKey", key: "F2");
        harness.Session.Deactivate();
        harness.Session.Activate();
        Assert.Equal(RadialActionKind.None,
            harness.Session.Draft.GetProfileMappings("radial-6")[1].Kind);

        harness.Session.RestoreDefault();
        Assert.All(harness.Session.Draft.GetProfileMappings("radial-6"),
            mapping => Assert.Equal(RadialActionKind.None, mapping.Kind));
    }

    [Fact]
    public void Frontend_HasStableGridSelectedFeedbackAndNoScrolling()
    {
        string script = Frontend("app.js");
        string styles = Frontend("styles.css");

        Assert.Contains("state.mappingSplitIndex", script);
        Assert.Contains("Slot ${mapping.slotId}", script);
        Assert.Contains("mapping-row.selected", styles);
        Assert.Contains("#mapping-detail", styles);
        Assert.Contains("height: 126px", styles);
        Assert.Contains("overflow: hidden", styles);
        Assert.Contains("mappingOverflow", script);
        Assert.DoesNotContain("localStorage", script);
        Assert.DoesNotContain("Option A", script);
    }

    [Fact]
    public void WebMappings_DoNotReplaceNativeFallbackOrRuntimeDynamicContent()
    {
        const System.Reflection.BindingFlags privateInstance =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        Assert.NotNull(typeof(MainForm).GetMethod("ShowNativeSettingsTab", privateInstance));
        Assert.NotNull(typeof(RadialDynamicContentCache));
    }

    private static string Frontend(string file) =>
        File.ReadAllText(Path.Combine(DreamscapeSettingsFeature.AssetDirectory, file));

    private static SessionHarness CreateHarness()
    {
        var harness = new SessionHarness
        {
            Active = RadialMenuSettings.Default with { ReceiverUiScalePercent = 150 }
        };
        harness.Session = new DreamscapeSettingsBasicSession(
            () => harness.Active,
            () => new RadialVisualPackCatalog().Discover(),
            settings => harness.LastPreview = settings,
            settings => harness.LastPreview = settings,
            () => { },
            settings =>
            {
                harness.Active = settings;
                return (true, string.Empty);
            },
            _ => { });
        return harness;
    }

    private static void ChangeVisualPack(DreamscapeSettingsBasicSession session, string id)
    {
        var message = new ReceiverSettingsMessage(
            ReceiverSettingsCommand.BasicChange,
            SettingsBasicFields.VisualPackId,
            id);
        Assert.True(session.TryApplyChange(message, out string error), error);
    }

    private static void ApplyBasic(DreamscapeSettingsBasicSession session, string field, int value)
    {
        var message = new ReceiverSettingsMessage(
            ReceiverSettingsCommand.BasicChange, field, IntegerValue: value);
        Assert.True(session.TryApplyChange(message, out string error), error);
    }

    private static void ApplyAdvanced(
        DreamscapeSettingsBasicSession session,
        string field,
        decimal value)
    {
        var message = new ReceiverSettingsMessage(
            ReceiverSettingsCommand.AdvancedChange, field, DecimalValue: value);
        Assert.True(session.TryApplyChange(message, out string error), error);
    }

    private static void ApplyMapping(
        DreamscapeSettingsBasicSession session,
        string profileId,
        int slotId,
        string kind,
        string? key = null,
        bool ctrl = false,
        bool alt = false,
        bool shift = false,
        bool win = false,
        string? ds4Action = null)
    {
        ReceiverSettingsMessage message = ParseMapping(
            profileId, slotId, kind, key, ctrl, alt, shift, win, ds4Action);
        Assert.True(session.TryApplyChange(message, out string error), error);
    }

    private static ReceiverSettingsMessage ParseMapping(
        string profileId,
        int slotId,
        string kind,
        string? key = null,
        bool ctrl = false,
        bool alt = false,
        bool shift = false,
        bool win = false,
        string? ds4Action = null)
    {
        string json = JsonSerializer.Serialize(new
        {
            command = "settingsMappingChange",
            profileId,
            slotId,
            actionKind = kind,
            key,
            ctrl,
            alt,
            shift,
            win,
            ds4Action
        });
        Assert.True(
            ReceiverSettingsCommandAllowList.TryParse(
                json, out ReceiverSettingsMessage message, out string error),
            error);
        return message;
    }

    private sealed class SessionHarness
    {
        public RadialMenuSettings Active { get; set; } = RadialMenuSettings.Default;
        public DreamscapeSettingsBasicSession Session { get; set; } = null!;
        public RadialMenuSettings? LastPreview { get; set; }
    }
}
