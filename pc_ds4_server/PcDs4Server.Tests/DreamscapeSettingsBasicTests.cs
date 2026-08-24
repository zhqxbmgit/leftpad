using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Xunit;

namespace PcDs4Server.Tests;

[Collection("Radial settings WinForms geometry")]
public sealed class DreamscapeSettingsBasicTests
{
    [Fact]
    public void WebFrontendFiles_ExistInCopiedRuntimeAssets()
    {
        string directory = DreamscapeSettingsFeature.AssetDirectory;

        Assert.True(DreamscapeSettingsHost.ValidateAssets(directory, out string error), error);
        Assert.All(
            DreamscapeSettingsHost.RequiredFrontendFiles,
            file => Assert.True(File.Exists(Path.Combine(directory, file)), file));
        Assert.Equal(
            ["index.html", "styles.css", "app.js", "static-art.png"],
            DreamscapeSettingsHost.RequiredFrontendFiles);
    }

    [Fact]
    public void StateDto_SerializesAllNineBasicFieldsAndRuntimeOptions()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();

        using JsonDocument document = JsonDocument.Parse(
            harness.Session.CreateState("已连接").ToJson());
        JsonElement root = document.RootElement;

        Assert.Equal(ReceiverSettingsBasicState.MessageType, root.GetProperty("type").GetString());
        Assert.Equal("已连接", root.GetProperty("connectionStatus").GetString());
        Assert.Equal("radial-v5", root.GetProperty("visualPackId").GetString());
        Assert.Equal(150, root.GetProperty("receiverUiScalePercent").GetInt32());
        Assert.Equal(112, root.GetProperty("overallSizePercent").GetInt32());
        Assert.Equal(180, root.GetProperty("doubleTapWindowMs").GetInt32());
        Assert.Equal(32, root.GetProperty("selectionDeadZone").GetInt32());
        Assert.Equal(90, root.GetProperty("selectedIntensity").GetInt32());
        Assert.Equal(210, root.GetProperty("petalOpacity").GetInt32());
        Assert.Equal(110, root.GetProperty("borderOpacity").GetInt32());
        Assert.Equal(230, root.GetProperty("textOpacity").GetInt32());
        Assert.Equal(2, root.GetProperty("visualPackOptions").GetArrayLength());
        Assert.Equal(5, root.GetProperty("receiverUiScaleOptions").GetArrayLength());
        Assert.True(root.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void BasicFieldContract_HasExactlyTheRequestedNineFields()
    {
        Assert.Equal(9, SettingsBasicFields.All.Count);
        Assert.Equal(9, SettingsBasicFields.All.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            [
                "visualPackId", "receiverUiScalePercent", "overallSizePercent",
                "doubleTapWindowMs", "selectionDeadZone", "selectedIntensity",
                "petalOpacity", "borderOpacity", "textOpacity"
            ],
            SettingsBasicFields.All);
    }

    [Theory]
    [InlineData("settingsBasicChange", "BasicChange")]
    [InlineData("settingsPreview", "Preview")]
    [InlineData("settingsHidePreview", "HidePreview")]
    [InlineData("settingsApplySave", "ApplySave")]
    [InlineData("settingsRestoreDefault", "RestoreDefault")]
    [InlineData("settingsRequestState", "RequestState")]
    [InlineData("showOverview", "ShowOverview")]
    [InlineData("showGamepad", "ShowGamepad")]
    [InlineData("showLogs", "ShowLogs")]
    [InlineData("showSettingsAdvanced", "ShowAdvanced")]
    [InlineData("showSettingsMappings", "ShowMappings")]
    [InlineData("beginDrag", "BeginDrag")]
    [InlineData("minimize", "Minimize")]
    [InlineData("closeWindow", "CloseWindow")]
    public void CommandAllowList_AcceptsOnlyNamedCommands(
        string commandName,
        string expected)
    {
        string json = commandName == "settingsBasicChange"
            ? "{\"command\":\"settingsBasicChange\",\"field\":\"overallSizePercent\",\"value\":100}"
            : JsonSerializer.Serialize(new { command = commandName });

        bool accepted = ReceiverSettingsCommandAllowList.TryParse(
            json,
            out ReceiverSettingsMessage message,
            out string reason);

        Assert.True(accepted, reason);
        Assert.Equal(expected, message.Command.ToString());
        Assert.Contains(commandName, ReceiverSettingsCommandAllowList.AllowedNames);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"command\":42}")]
    [InlineData("{\"command\":\"deleteEverything\"}")]
    [InlineData("{\"command\":\"settingsBasicChange\"}")]
    [InlineData("{\"command\":\"settingsBasicChange\",\"field\":\"unknown\",\"value\":1}")]
    [InlineData("{\"command\":\"settingsBasicChange\",\"field\":\"visualPackId\",\"value\":42}")]
    public void UnknownOrMalformedCommands_AreRejectedWithoutThrowing(string json)
    {
        bool accepted = ReceiverSettingsCommandAllowList.TryParse(json, out _, out string reason);

        Assert.False(accepted);
        Assert.NotEmpty(reason);
    }

    [Theory]
    [InlineData("receiverUiScalePercent", 110)]
    [InlineData("overallSizePercent", 59)]
    [InlineData("overallSizePercent", 141)]
    [InlineData("doubleTapWindowMs", 79)]
    [InlineData("doubleTapWindowMs", 501)]
    [InlineData("selectionDeadZone", 7)]
    [InlineData("selectionDeadZone", 81)]
    [InlineData("selectedIntensity", -1)]
    [InlineData("petalOpacity", 256)]
    public void BasicChange_RejectsValuesOutsideExistingRuntimeSemantics(string field, int value)
    {
        string json = JsonSerializer.Serialize(new
        {
            command = "settingsBasicChange",
            field,
            value
        });

        Assert.False(ReceiverSettingsCommandAllowList.TryParse(json, out _, out string reason));
        Assert.NotEmpty(reason);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    public void SettingsFeatureFlag_DefaultsOffAndRequiresExplicitOptIn(string? value, bool expected)
    {
        Assert.Equal(expected, DreamscapeSettingsFeature.IsEnabledValue(value));
    }

    [Fact]
    public void SettingsAndOverviewFeatureFlags_AreIndependent()
    {
        Assert.NotEqual(
            DreamscapeOverviewFeature.EnvironmentVariable,
            DreamscapeSettingsFeature.EnvironmentVariable);
        Assert.False(DreamscapeSettingsFeature.IsEnabledValue(null));
        Assert.False(DreamscapeOverviewFeature.IsEnabledValue(null));
    }

    [Fact]
    public void ReferenceMetadata_MatchesMeasuredSettingsTarget()
    {
        Assert.Equal(1672, DreamscapeSettingsReferenceMetadata.ReferenceWidth);
        Assert.Equal(941, DreamscapeSettingsReferenceMetadata.ReferenceHeight);
        Assert.Equal(
            "BE2ACF99E4DDF5CA2FD8544E3ED3271BDD397CF22728D3D65C0ADB6B8B8BDAB8",
            DreamscapeSettingsReferenceMetadata.ReferenceSha256);
        Assert.True(DreamscapeSettingsReferenceMetadata.Elements.Count >= 28);
        Assert.Contains(DreamscapeSettingsReferenceMetadata.Elements,
            element => element is { Name: "Settings surface", X: 306, Y: 247, Width: 1234, Height: 658 });
        Assert.Contains(DreamscapeSettingsReferenceMetadata.Elements,
            element => element is { Name: "Restore button", X: 1119, Y: 786, Width: 201, Height: 82 });
    }

    [Fact]
    public void SliderRanges_ReuseExistingRadialSettingsLimits()
    {
        Assert.Equal(
            new SettingsBasicRange(RadialMenuSettings.MinimumScalePercent, RadialMenuSettings.MaximumScalePercent, 1),
            SettingsBasicFields.Ranges[SettingsBasicFields.OverallSizePercent]);
        Assert.Equal(
            new SettingsBasicRange(RadialMenuSettings.MinimumDoubleTapWindowMs, RadialMenuSettings.MaximumDoubleTapWindowMs, 1),
            SettingsBasicFields.Ranges[SettingsBasicFields.DoubleTapWindowMs]);
        Assert.Equal(
            new SettingsBasicRange(RadialMenuSettings.MinimumSelectionDeadZone, RadialMenuSettings.MaximumSelectionDeadZone, 1),
            SettingsBasicFields.Ranges[SettingsBasicFields.SelectionDeadZone]);
        Assert.Equal(new SettingsBasicRange(0, 255, 1), SettingsBasicFields.Ranges[SettingsBasicFields.TextOpacity]);
    }

    [Fact]
    public void ReceiverUiScale_UsesTheExistingFivePresets()
    {
        Assert.Equal([100, 125, 150, 175, 200], ReceiverUiScaling.Presets);
        Assert.All(ReceiverUiScaling.Presets, value => Assert.True(ReceiverUiScaling.IsPreset(value)));
    }

    [Fact]
    public void ProductionVisualPackCatalog_ProvidesOnlyProductionOptions()
    {
        RadialVisualPackCatalogSnapshot catalog = new RadialVisualPackCatalog().Discover();

        Assert.Empty(catalog.Issues);
        Assert.Equal(["radial-v5", "radial-8-minimal-v1"], catalog.Packs.Select(pack => pack.Id));
        Assert.Equal(["Tactical HUD V5", "Radial 8 Minimal V1"], catalog.Packs.Select(pack => pack.Name));
    }

    [Fact]
    public void Activate_LoadsActiveSettingsAndDeactivateDiscardsUnsavedDraft()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        Assert.Equal(harness.Active, harness.Session.Draft);

        ApplyIntegerChange(harness.Session, SettingsBasicFields.OverallSizePercent, 120);
        Assert.True(harness.Session.IsDirty);
        harness.Session.Deactivate();
        harness.Session.Activate();

        Assert.Equal(112, harness.Session.Draft.ScalePercent);
        Assert.False(harness.Session.IsDirty);
    }

    [Theory]
    [InlineData("receiverUiScalePercent", 175)]
    [InlineData("overallSizePercent", 120)]
    [InlineData("doubleTapWindowMs", 225)]
    [InlineData("selectionDeadZone", 40)]
    [InlineData("selectedIntensity", 100)]
    [InlineData("petalOpacity", 200)]
    [InlineData("borderOpacity", 120)]
    [InlineData("textOpacity", 220)]
    public void EveryNumericBasicField_ChangesTheDraft(string field, int value)
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();

        ApplyIntegerChange(harness.Session, field, value);

        Assert.True(harness.Session.IsDirty);
        Assert.Contains(value.ToString(), harness.Session.CreateState("ready").ToJson());
    }

    [Fact]
    public void VisualPackChange_AlsoUsesItsProductionMappingProfile()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        Assert.True(ReceiverSettingsCommandAllowList.TryParse(
            "{\"command\":\"settingsBasicChange\",\"field\":\"visualPackId\",\"value\":\"radial-8-minimal-v1\"}",
            out ReceiverSettingsMessage message,
            out string parseError), parseError);

        Assert.True(harness.Session.TryApplyChange(message, out string applyError), applyError);
        Assert.Equal("radial-8-minimal-v1", harness.Session.Draft.VisualPackId);
        Assert.Equal("radial-8", harness.Session.Draft.MappingProfileId);
    }

    [Fact]
    public void UnknownVisualPack_IsRejectedByTheSession()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        var message = new ReceiverSettingsMessage(
            ReceiverSettingsCommand.BasicChange,
            SettingsBasicFields.VisualPackId,
            "prototype-only-pack");

        Assert.False(harness.Session.TryApplyChange(message, out string reason));
        Assert.Contains("Unknown production visual pack", reason);
        Assert.Equal("radial-v5", harness.Session.Draft.VisualPackId);
    }

    [Fact]
    public void PreviewAndHide_UseControllerDelegatesAndLiveDraftUpdates()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        harness.Session.Preview();
        ApplyIntegerChange(harness.Session, SettingsBasicFields.OverallSizePercent, 125);
        harness.Session.HidePreview();

        Assert.Equal(1, harness.PreviewCount);
        Assert.Equal(1, harness.UpdatePreviewCount);
        Assert.Equal(1, harness.HidePreviewCount);
        Assert.False(harness.Session.IsPreviewActive);
    }

    [Fact]
    public void ApplyAndSave_CommitsDraftAndPreservesExistingPreviewSemantics()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        harness.Session.Preview();
        ApplyIntegerChange(harness.Session, SettingsBasicFields.TextOpacity, 200);

        Assert.True(harness.Session.ApplyAndSave(out string error), error);
        Assert.Equal(1, harness.SaveCount);
        Assert.Equal(200, harness.Active.TextAlpha);
        Assert.False(harness.Session.IsDirty);
        Assert.True(harness.Session.IsPreviewActive);
        Assert.Equal(0, harness.HidePreviewCount);
    }

    [Fact]
    public void FailedApply_PreservesDraftAndDirtyState()
    {
        SessionHarness harness = CreateHarness(saveSucceeds: false);
        harness.Session.Activate();
        ApplyIntegerChange(harness.Session, SettingsBasicFields.BorderOpacity, 130);

        Assert.False(harness.Session.ApplyAndSave(out string error));
        Assert.Equal("simulated save failure", error);
        Assert.True(harness.Session.IsDirty);
        Assert.Equal(130, harness.Session.Draft.BorderAlpha);
    }

    [Fact]
    public void RestoreDefault_ChangesDraftWithoutPersistingUntilApply()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();

        harness.Session.RestoreDefault();

        Assert.Equal(RadialMenuSettings.Default.NormalizeMappings(), harness.Session.Draft);
        Assert.True(harness.Session.IsDirty);
        Assert.Equal(0, harness.SaveCount);
        Assert.Equal(112, harness.Active.ScalePercent);
    }

    [Fact]
    public void Deactivate_ClosesAnActivePreview()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        harness.Session.Preview();

        harness.Session.Deactivate();

        Assert.Equal(1, harness.HidePreviewCount);
        Assert.False(harness.Session.IsActive);
        Assert.False(harness.Session.IsPreviewActive);
    }

    [Fact]
    public void WebView2Host_ConstructionIsSafeBeforeRuntimeInitialization()
    {
        RunInSta(() =>
        {
            SessionHarness harness = CreateHarness();
            harness.Session.Activate();
            using var host = new DreamscapeSettingsHost(
                () => harness.Session.CreateState("未连接"),
                _ => { },
                _ => { });

            Assert.Equal(DockStyle.Fill, host.Dock);
            Assert.Equal("dreamscapeSettingsHost", host.Name);
            Assert.False(host.IsInitialized);
            Assert.Single(host.Controls);
            Assert.IsAssignableFrom<UserControl>(host);
        });
    }

    [Fact]
    public void MainFormLifecycleAndNativeSettingsEntryPoints_RemainPresent()
    {
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        Assert.NotNull(typeof(MainForm).GetMethod("MainForm_FormClosing", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("ExitProgram", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("ShowMainForm", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("WndProc", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("SetDreamscapeSettingsVisibility", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("ShowNativeSettingsTab", privateInstance));
    }

    private static SessionHarness CreateHarness(bool saveSucceeds = true)
    {
        var harness = new SessionHarness
        {
            Active = RadialMenuSettings.Default with
            {
                ReceiverUiScalePercent = 150,
                ScalePercent = 112,
                DoubleTapWindowMs = 180,
                SelectionDeadZone = 32,
                HighlightAlpha = 90,
                FillAlpha = 210,
                BorderAlpha = 110,
                TextAlpha = 230
            }
        };
        harness.Session = new DreamscapeSettingsBasicSession(
            () => harness.Active,
            () => new RadialVisualPackCatalog().Discover(),
            _ => harness.PreviewCount++,
            _ => harness.UpdatePreviewCount++,
            () => harness.HidePreviewCount++,
            draft =>
            {
                harness.SaveCount++;
                if (!saveSucceeds)
                    return (false, "simulated save failure");
                harness.Active = draft;
                return (true, string.Empty);
            },
            _ => { });
        return harness;
    }

    private static void ApplyIntegerChange(
        DreamscapeSettingsBasicSession session,
        string field,
        int value)
    {
        var message = new ReceiverSettingsMessage(
            ReceiverSettingsCommand.BasicChange,
            field,
            IntegerValue: value);
        Assert.True(session.TryApplyChange(message, out string error), error);
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class SessionHarness
    {
        public RadialMenuSettings Active { get; set; } = RadialMenuSettings.Default;
        public DreamscapeSettingsBasicSession Session { get; set; } = null!;
        public int PreviewCount { get; set; }
        public int UpdatePreviewCount { get; set; }
        public int HidePreviewCount { get; set; }
        public int SaveCount { get; set; }
    }
}
