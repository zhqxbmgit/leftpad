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
        Assert.Equal(3, root.GetProperty("visualPackOptions").GetArrayLength());
        Assert.Equal(5, root.GetProperty("receiverUiScaleOptions").GetArrayLength());
        Assert.True(root.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void StateDto_SerializesAllEightAdvancedFieldsWithNativeDefinitions()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        using JsonDocument document = JsonDocument.Parse(
            harness.Session.CreateState("已连接").ToJson());
        JsonElement fields = document.RootElement.GetProperty("advancedFields");

        Assert.Equal(8, fields.EnumerateObject().Count());
        Assert.Equal(160, fields.GetProperty("canvasSize").GetProperty("min").GetInt32());
        Assert.Equal(800, fields.GetProperty("canvasSize").GetProperty("max").GetInt32());
        Assert.Equal(280, fields.GetProperty("canvasSize").GetProperty("default").GetInt32());
        Assert.Equal("px", fields.GetProperty("canvasSize").GetProperty("unit").GetString());
        Assert.Equal(0.5m, fields.GetProperty("petalGapDegrees").GetProperty("step").GetDecimal());
        Assert.Equal(0.5m, fields.GetProperty("fontSize").GetProperty("step").GetDecimal());
        Assert.Equal(8, fields.GetProperty("selectionPollIntervalMs").GetProperty("min").GetInt32());
        Assert.Equal(50, fields.GetProperty("selectionPollIntervalMs").GetProperty("max").GetInt32());
        Assert.Equal("basic", document.RootElement.GetProperty("activeSection").GetString());
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

    [Fact]
    public void AdvancedFieldContract_HasFrozenFourPlusFourOptionAStructure()
    {
        Assert.Equal(
            ["canvasSize", "centerRadius", "petalInnerRadius", "petalOuterRadius"],
            SettingsAdvancedFields.Left);
        Assert.Equal(
            ["textRadius", "petalGapDegrees", "fontSize", "selectionPollIntervalMs"],
            SettingsAdvancedFields.Right);
        Assert.Equal(8, SettingsAdvancedFields.All.Count);
        Assert.Equal(8, SettingsAdvancedFields.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("canvasSize", 160, 800, 1, 280, "px")]
    [InlineData("centerRadius", 1, 399, 1, 35, "px")]
    [InlineData("petalInnerRadius", 1, 399, 1, 42, "px")]
    [InlineData("petalOuterRadius", 2, 399, 1, 103, "px")]
    [InlineData("textRadius", 1, 399, 1, 73, "px")]
    [InlineData("petalGapDegrees", 0, 12, 0.5, 4, "°")]
    [InlineData("fontSize", 6, 48, 0.5, 15, "px")]
    [InlineData("selectionPollIntervalMs", 8, 50, 1, 16, "ms")]
    public void AdvancedDefinitions_MatchNativeEditorsAndRuntimeDefaults(
        string field,
        double min,
        double max,
        double step,
        double defaultValue,
        string unit)
    {
        SettingsAdvancedFieldState definition = SettingsAdvancedFields.Definition(field);

        Assert.Equal((decimal)min, definition.Min);
        Assert.Equal((decimal)max, definition.Max);
        Assert.Equal((decimal)step, definition.Step);
        Assert.Equal((decimal)defaultValue, definition.Default);
        Assert.Equal(unit, definition.Unit);
    }

    [Theory]
    [InlineData("settingsBasicChange", "BasicChange")]
    [InlineData("settingsAdvancedChange", "AdvancedChange")]
    [InlineData("settingsMappingSelectSlot", "MappingSelectSlot")]
    [InlineData("settingsMappingChange", "MappingChange")]
    [InlineData("settingsPreview", "Preview")]
    [InlineData("settingsHidePreview", "HidePreview")]
    [InlineData("settingsApplySave", "ApplySave")]
    [InlineData("settingsRestoreDefault", "RestoreDefault")]
    [InlineData("settingsRequestState", "RequestState")]
    [InlineData("showOverview", "ShowOverview")]
    [InlineData("showGamepad", "ShowGamepad")]
    [InlineData("showLogs", "ShowLogs")]
    [InlineData("showSettingsBasic", "ShowBasic")]
    [InlineData("showSettingsAdvanced", "ShowAdvanced")]
    [InlineData("showSettingsMappings", "ShowMappings")]
    [InlineData("beginDrag", "BeginDrag")]
    [InlineData("minimize", "Minimize")]
    [InlineData("closeWindow", "CloseWindow")]
    public void CommandAllowList_AcceptsOnlyNamedCommands(
        string commandName,
        string expected)
    {
        string json = commandName switch
        {
            "settingsBasicChange" =>
                "{\"command\":\"settingsBasicChange\",\"field\":\"overallSizePercent\",\"value\":100}",
            "settingsAdvancedChange" =>
                "{\"command\":\"settingsAdvancedChange\",\"field\":\"fontSize\",\"value\":15.5}",
            "settingsMappingSelectSlot" =>
                "{\"command\":\"settingsMappingSelectSlot\",\"profileId\":\"radial-6\",\"slotId\":1}",
            "settingsMappingChange" =>
                "{\"command\":\"settingsMappingChange\",\"profileId\":\"radial-6\",\"slotId\":1,\"actionKind\":\"none\"}",
            _ => JsonSerializer.Serialize(new { command = commandName })
        };

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
    [InlineData("{\"command\":\"settingsAdvancedChange\"}")]
    [InlineData("{\"command\":\"settingsAdvancedChange\",\"field\":\"unknown\",\"value\":1}")]
    [InlineData("{\"command\":\"settingsAdvancedChange\",\"field\":\"fontSize\",\"value\":\"15.5\"}")]
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
    [InlineData("canvasSize", 159)]
    [InlineData("canvasSize", 801)]
    [InlineData("centerRadius", 0)]
    [InlineData("petalOuterRadius", 1)]
    [InlineData("petalGapDegrees", 12.5)]
    [InlineData("petalGapDegrees", 4.25)]
    [InlineData("fontSize", 5.5)]
    [InlineData("fontSize", 48.5)]
    [InlineData("selectionPollIntervalMs", 7)]
    [InlineData("selectionPollIntervalMs", 51)]
    public void AdvancedChange_RejectsInvalidRangeOrStep(string field, double value)
    {
        string json = JsonSerializer.Serialize(new
        {
            command = "settingsAdvancedChange",
            field,
            value
        });

        Assert.False(ReceiverSettingsCommandAllowList.TryParse(json, out _, out string reason));
        Assert.NotEmpty(reason);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    public void SettingsFeatureFlag_DefaultsToDreamscapeAndAllowsExplicitOverride(string? value, bool expected)
    {
        Assert.Equal(expected, DreamscapeSettingsFeature.IsEnabledValue(value));
    }

    [Fact]
    public void SettingsAndOverviewFeatureFlags_AreIndependent()
    {
        Assert.NotEqual(
            DreamscapeOverviewFeature.EnvironmentVariable,
            DreamscapeSettingsFeature.EnvironmentVariable);
        Assert.True(DreamscapeSettingsFeature.IsEnabledValue(null));
        Assert.True(DreamscapeOverviewFeature.IsEnabledValue(null));
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
        Assert.Equal(
            ["radial-v5", "dark-fantasy-radial8-v1", "radial-8-minimal-v1"],
            catalog.Packs.Select(pack => pack.Id));
        Assert.Equal(
            ["Tactical HUD V5", "Dark Fantasy Radial 8", "Radial 8 Minimal V1"],
            catalog.Packs.Select(pack => pack.Name));
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

    [Theory]
    [InlineData("canvasSize", 300)]
    [InlineData("centerRadius", 34)]
    [InlineData("petalInnerRadius", 43)]
    [InlineData("petalOuterRadius", 104)]
    [InlineData("textRadius", 74)]
    [InlineData("petalGapDegrees", 4.5)]
    [InlineData("fontSize", 15.5)]
    [InlineData("selectionPollIntervalMs", 17)]
    public void EveryAdvancedField_ChangesTheSharedDraft(string field, double value)
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();

        ApplyAdvancedChange(harness.Session, field, (decimal)value);

        Assert.True(harness.Session.IsDirty);
        Assert.Equal((decimal)value,
            SettingsAdvancedFields.CreateStates(harness.Session.Draft)[field].Value);
    }

    [Fact]
    public void BasicAndAdvancedTabs_PreserveOneSharedDraft()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        ApplyIntegerChange(harness.Session, SettingsBasicFields.OverallSizePercent, 120);
        harness.Session.ShowAdvanced();
        ApplyAdvancedChange(harness.Session, SettingsAdvancedFields.FontSize, 15.5m);
        harness.Session.ShowBasic();

        Assert.Equal(120, harness.Session.Draft.ScalePercent);
        Assert.Equal(15.5f, harness.Session.Draft.FontSize);
        Assert.Equal(ReceiverSettingsSection.Basic, harness.Session.ActiveSection);
        harness.Session.ShowAdvanced();
        Assert.Equal(ReceiverSettingsSection.Advanced, harness.Session.ActiveSection);
        Assert.Equal(120, harness.Session.Draft.ScalePercent);
    }

    [Fact]
    public void PreviewAndApply_IncludeBasicAndAdvancedDraftChangesTogether()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        ApplyIntegerChange(harness.Session, SettingsBasicFields.TextOpacity, 220);
        ApplyAdvancedChange(harness.Session, SettingsAdvancedFields.FontSize, 15.5m);

        harness.Session.Preview();
        Assert.Equal(220, harness.LastPreview!.TextAlpha);
        Assert.Equal(15.5f, harness.LastPreview.FontSize);
        Assert.True(harness.Session.ApplyAndSave(out string error), error);
        Assert.Equal(220, harness.Active.TextAlpha);
        Assert.Equal(15.5f, harness.Active.FontSize);
    }

    [Fact]
    public void Deactivate_DiscardsUnsavedBasicAndAdvancedChanges()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        ApplyIntegerChange(harness.Session, SettingsBasicFields.OverallSizePercent, 120);
        ApplyAdvancedChange(harness.Session, SettingsAdvancedFields.CanvasSize, 300m);
        harness.Session.Deactivate();
        harness.Session.Activate();

        Assert.Equal(112, harness.Session.Draft.ScalePercent);
        Assert.Equal(RadialMenuSettings.Default.BaseCanvasSize, harness.Session.Draft.BaseCanvasSize);
        Assert.False(harness.Session.IsDirty);
    }

    [Fact]
    public void RestoreDefault_ResetsBasicAndAdvancedInTheSharedDraft()
    {
        SessionHarness harness = CreateHarness();
        harness.Session.Activate();
        ApplyIntegerChange(harness.Session, SettingsBasicFields.OverallSizePercent, 120);
        ApplyAdvancedChange(harness.Session, SettingsAdvancedFields.FontSize, 15.5m);

        harness.Session.RestoreDefault();

        Assert.Equal(RadialMenuSettings.Default.ScalePercent, harness.Session.Draft.ScalePercent);
        Assert.Equal(RadialMenuSettings.Default.FontSize, harness.Session.Draft.FontSize);
        Assert.Equal(0, harness.SaveCount);
    }

    [Fact]
    public void OptionAFrontend_ContainsInteractiveFourPlusFourRangesAndKeyboardFocusStyling()
    {
        string html = File.ReadAllText(Path.Combine(DreamscapeSettingsFeature.AssetDirectory, "index.html"));
        string script = File.ReadAllText(Path.Combine(DreamscapeSettingsFeature.AssetDirectory, "app.js"));
        string styles = File.ReadAllText(Path.Combine(DreamscapeSettingsFeature.AssetDirectory, "styles.css"));

        Assert.Equal(8, System.Text.RegularExpressions.Regex.Matches(
            html, "data-advanced-field=\\\"").Count);
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(html, "advanced-left-row").Count);
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(html, "advanced-right-row").Count);
        Assert.Contains("settingsAdvancedChange", script);
        Assert.Contains("addEventListener('input'", script);
        Assert.Contains(".advanced-row input[type=\"range\"]:focus-visible", styles);
        Assert.DoesNotContain("type=\"number\"", html);
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
            settings =>
            {
                harness.PreviewCount++;
                harness.LastPreview = settings;
            },
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

    private static void ApplyAdvancedChange(
        DreamscapeSettingsBasicSession session,
        string field,
        decimal value)
    {
        var message = new ReceiverSettingsMessage(
            ReceiverSettingsCommand.AdvancedChange,
            field,
            DecimalValue: value);
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
        public RadialMenuSettings? LastPreview { get; set; }
        public int UpdatePreviewCount { get; set; }
        public int HidePreviewCount { get; set; }
        public int SaveCount { get; set; }
    }
}
