using System.Text.Json;
using System.Text.RegularExpressions;
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
        string styles = Frontend("styles.css") + File.ReadAllText(
            Path.Combine(DreamscapeShellMetrics.AssetDirectory, "shell.css"));
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
    public void Slot3Tab_PersistsThroughJsonReloadNormalizeControllerSessionAndResolver()
    {
        using var temporary = new TemporarySettingsFile();
        RadialSlotMappings mappings = RadialMenuSettings.Default
            .GetProfileMappings(LayoutProfileRegistry.Radial6ProfileId)
            .WithSlot(3, new RadialSlotMapping
            {
                Kind = RadialActionKind.KeyboardKey,
                Key = KeyboardKey.Tab
            });
        RadialMenuSettings expected = RadialMenuSettings.Default.SetProfileMappings(
            LayoutProfileRegistry.Radial6ProfileId,
            mappings);

        using (var savingController = new RadialMenuController(
            new FakeOverlay(),
            RadialMenuSettings.Default))
        {
            var savingStore = new RadialMenuSettingsStore(temporary.Path);
            Assert.True(RadialMenuSettingsPersistence.TryApplyAndSave(
                savingController,
                savingStore,
                expected,
                savingController.ApplySettings,
                out string saveError), saveError);
            Assert.Equal(KeyboardKey.Tab, savingController.ActiveSettings
                .GetProfileMappings(LayoutProfileRegistry.Radial6ProfileId)[2].Key);
        }

        using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(temporary.Path)))
        {
            JsonElement slot3 = document.RootElement
                .GetProperty("mappingsByProfile")
                .GetProperty("radial-6")[2];
            Assert.Equal("keyboardKey", slot3.GetProperty("kind").GetString());
            Assert.Equal("Tab", slot3.GetProperty("key").GetString());
        }

        var reloadStore = new RadialMenuSettingsStore(temporary.Path);
        RadialMenuSettingsLoadResult reload = reloadStore.Load();
        Assert.Equal(RadialMenuSettingsLoadStatus.Loaded, reload.Status);
        Assert.Equal(KeyboardKey.Tab, reload.Settings
            .GetProfileMappings(LayoutProfileRegistry.Radial6ProfileId)[2].Key);

        RadialMenuSettings normalized = reload.Settings.NormalizeMappings();
        Assert.Equal(KeyboardKey.Tab, normalized
            .GetProfileMappings(LayoutProfileRegistry.Radial6ProfileId)[2].Key);

        using var startupController = new RadialMenuController(
            new FakeOverlay(),
            normalized);
        RadialSlotMapping activeSlot3 = startupController.ActiveSettings
            .GetProfileMappings(LayoutProfileRegistry.Radial6ProfileId)[2];
        Assert.Equal(RadialActionKind.KeyboardKey, activeSlot3.Kind);
        Assert.Equal(KeyboardKey.Tab, activeSlot3.Key);

        var session = new DreamscapeSettingsBasicSession(
            () => startupController.ActiveSettings,
            () => new RadialVisualPackCatalog().Discover(),
            _ => { },
            _ => { },
            () => { },
            _ => (true, string.Empty),
            _ => { });
        session.Activate();
        ReceiverSettingsBasicState state = session.CreateState("ready");
        Assert.Null(session.SelectedMappingSlot);
        Assert.Null(state.SelectedMappingSlot);
        Assert.Equal("keyboardKey", state.Mappings[2].ActionKind);
        Assert.Equal("Tab", state.Mappings[2].Key);
        Assert.Equal("Tab", state.Mappings[2].Summary);

        RadialSlotMapping resolved = RadialActionResolver.GetMapping(
            startupController.ActiveSettings,
            LayoutProfileRegistry.Radial6ProfileId,
            3);
        Assert.Equal(RadialActionKind.KeyboardKey, resolved.Kind);
        Assert.Equal(KeyboardKey.Tab, resolved.Key);
    }

    [Fact]
    public void MappingSummaries_UseStableCompactRuntimeValues()
    {
        Assert.Equal(string.Empty, SettingsMappingCatalogs.Summary(RadialSlotMapping.None));
        Assert.Equal("Tab", SettingsMappingCatalogs.Summary(new RadialSlotMapping
        {
            Kind = RadialActionKind.KeyboardKey,
            Key = KeyboardKey.Tab
        }));
        Assert.Equal("Ctrl + Alt + Shift + Win + K", SettingsMappingCatalogs.Summary(
            new RadialSlotMapping
            {
                Kind = RadialActionKind.KeyboardShortcut,
                Key = KeyboardKey.K,
                Ctrl = true,
                Alt = true,
                Shift = true,
                Win = true
            }));
        Assert.Equal("CROSS", SettingsMappingCatalogs.Summary(new RadialSlotMapping
        {
            Kind = RadialActionKind.Ds4Button,
            Ds4Button = "cross"
        }));
    }

    [Fact]
    public void Frontend_RowUsesCSharpSummaryAndApplyStatusUsesExistingOutput()
    {
        string script = Frontend("app.js");
        string html = Frontend("index.html");

        Assert.Contains("${mapping.summary || 'ACTION TYPE'}", script);
        Assert.Contains("status.type !== 'receiverSettingsStatus'", script);
        Assert.Contains("output.textContent = status.message", script);
        Assert.Contains("output.dataset.tone = status.tone", script);
        Assert.Contains("id=\"status-message\"", html);
        Assert.DoesNotContain("localStorage", script);
    }

    [Fact]
    public void ApplyStatusMessages_DistinguishSuccessFromFailure()
    {
        ReceiverSettingsStatusMessage success = ReceiverSettingsStatusMessage.Saved();
        ReceiverSettingsStatusMessage failure =
            ReceiverSettingsStatusMessage.SaveFailed("simulated disk error");

        Assert.Equal("设置已保存", success.Message);
        Assert.Equal("success", success.Tone);
        Assert.Equal("保存失败：simulated disk error", failure.Message);
        Assert.Equal("error", failure.Tone);
        Assert.Contains("receiverSettingsStatus", success.ToJson());
        Assert.DoesNotContain("设置已保存", failure.Message);
    }

    [Fact]
    public void Frontend_HasStableGridSelectedFeedbackAndNoScrolling()
    {
        string script = Frontend("app.js");
        string styles = Frontend("styles.css") + File.ReadAllText(
            Path.Combine(DreamscapeShellMetrics.AssetDirectory, "shell.css"));

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
    public void MappingOverlay_DelegatesHitTestingInsteadOfOwningTheFullCanvas()
    {
        string styles = Frontend("styles.css");

        Assert.Equal("none", CssProperty(styles, "#mapping-section", "pointer-events"));
        Assert.Equal("none", CssProperty(styles, "#mapping-grid", "pointer-events"));
        Assert.Null(CssProperty(styles, ".mapping-content-mask", "pointer-events"));
        Assert.Null(CssProperty(styles, ".mapping-heading", "pointer-events"));
        Assert.Equal("auto", CssProperty(styles, ".mapping-row", "pointer-events"));
        Assert.Equal("auto", CssProperty(styles, "#mapping-detail", "pointer-events"));
    }

    [Theory]
    [InlineData("showSettingsBasic")]
    [InlineData("showSettingsAdvanced")]
    [InlineData("showSettingsMappings")]
    public void SettingsTabs_AreOutsideTheNonHitTestingMappingOverlay(string command)
    {
        string html = Frontend("index.html");
        int tab = html.IndexOf($"data-command=\"{command}\"", StringComparison.Ordinal);
        int mappingStart = html.IndexOf("<section id=\"mapping-section\"", StringComparison.Ordinal);
        int mappingEnd = html.IndexOf("</section>", mappingStart, StringComparison.Ordinal);

        Assert.InRange(tab, 0, mappingStart - 1);
        Assert.DoesNotContain(command, html[(mappingStart + 1)..mappingEnd]);
    }

    [Theory]
    [InlineData("preview-button", "settingsPreview")]
    [InlineData("hide-preview-button", "settingsHidePreview")]
    [InlineData("apply-button", "settingsApplySave")]
    [InlineData("restore-button", "settingsRestoreDefault")]
    public void BottomActions_AreSiblingsAfterTheNonHitTestingMappingOverlay(
        string id,
        string command)
    {
        string html = Frontend("index.html");
        int mappingStart = html.IndexOf("<section id=\"mapping-section\"", StringComparison.Ordinal);
        int mappingEnd = html.IndexOf("</section>", mappingStart, StringComparison.Ordinal);
        int action = html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);

        Assert.True(action > mappingEnd);
        Assert.Contains($"data-command=\"{command}\"", html[action..]);
    }

    [Fact]
    public void PointerOwnershipFix_PreservesRadialSixAndEightGeometry()
    {
        string styles = Frontend("styles.css");

        Assert.Equal("calc(329px + var(--row) * 68px)",
            CssProperty(styles, ".mapping-row", "top"));
        Assert.Equal("62px", CssProperty(styles, ".mapping-row", "height"));
        Assert.Equal("calc(335px + var(--row) * 83px)",
            CssProperty(styles, "html[data-mapping-slots=\"6\"] .mapping-row", "top"));
        Assert.Equal("70px",
            CssProperty(styles, "html[data-mapping-slots=\"6\"] .mapping-row", "height"));
        Assert.Equal("608px", CssProperty(styles, "#mapping-detail", "top"));
        Assert.Equal("598px",
            CssProperty(styles, "html[data-mapping-slots=\"6\"] #mapping-detail", "top"));
    }

    [Fact]
    public void SettingsMarkup_HasNineBasicFieldsAndExactlyEightAdvancedFieldIcons()
    {
        string html = Frontend("index.html");
        string basic = SectionBody(html, "settings-form");
        string advanced = SectionBody(html, "advanced-form");
        MatchCollection rows = Regex.Matches(
            advanced,
            @"(?ms)<div\s+class=""advanced-row\s+(?<side>advanced-(?:left|right)-row)""[^>]*>(?<body>.*?)</div>",
            RegexOptions.CultureInvariant);

        Assert.Equal(9, Regex.Matches(
            basic, @"<div\s+class=""setting-row\b", RegexOptions.CultureInvariant).Count);
        Assert.Equal(8, rows.Count);
        Assert.Equal(4, rows.Cast<Match>().Count(row =>
            row.Groups["side"].Value == "advanced-left-row"));
        Assert.Equal(4, rows.Cast<Match>().Count(row =>
            row.Groups["side"].Value == "advanced-right-row"));
        Assert.All(rows.Cast<Match>(), row =>
        {
            Assert.Single(Regex.Matches(
                row.Groups["body"].Value,
                @"<span\s+class=""advanced-icon""",
                RegexOptions.CultureInvariant).Cast<Match>());
            Assert.Single(Regex.Matches(
                row.Groups["body"].Value,
                @"<input\s+[^>]*data-advanced-field=",
                RegexOptions.CultureInvariant).Cast<Match>());
        });
        Assert.Equal(rows.Count, Regex.Matches(
            advanced, @"<span\s+class=""advanced-icon""", RegexOptions.CultureInvariant).Count);
    }

    [Fact]
    public void AdvancedStaticIconMasks_AreSectionScopedAndPreserveRealIconsAboveThem()
    {
        string styles = Frontend("styles.css");

        Assert.Equal("none", CssProperty(styles, "#advanced-form", "display"));
        Assert.Equal("block", CssProperty(
            styles, "html[data-section=\"advanced\"] #advanced-form", "display"));
        Assert.Equal("none", CssProperty(
            styles, "html[data-section=\"mappings\"] #advanced-form", "display"));
        Assert.Equal("none", CssPropertyInRuleGroup(
            styles, "html[data-section=\"advanced\"] #settings-form", "display"));
        Assert.Equal("none", CssPropertyInRuleGroup(
            styles, "html[data-section=\"mappings\"] #settings-form", "display"));
        Assert.Equal("1", CssProperty(styles, ".advanced-row", "z-index"));
        Assert.Equal("#fff9f7", CssProperty(styles, ".advanced-icon", "background"));
        Assert.Equal("454px", CssPropertyInRuleGroup(
            styles, "#advanced-form::before", "height"));
        Assert.Equal("382px", CssPropertyInRuleGroup(
            styles, "#advanced-form::after", "height"));
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

    private static string SectionBody(string html, string id)
    {
        Match section = Regex.Match(
            html,
            $@"(?ms)<section\s+id=""{Regex.Escape(id)}""[^>]*>(?<body>.*?)</section>",
            RegexOptions.CultureInvariant);
        Assert.True(section.Success, $"Missing section #{id}.");
        return section.Groups["body"].Value;
    }

    private static string? CssProperty(string styles, string selector, string property)
    {
        string escapedSelector = Regex.Escape(selector);
        Match rule = Regex.Match(
            styles,
            $@"(?ms)^\s*{escapedSelector}\s*\{{(?<body>.*?)\}}",
            RegexOptions.CultureInvariant);
        Assert.True(rule.Success, $"Missing CSS rule for {selector}.");

        Match declaration = Regex.Match(
            rule.Groups["body"].Value,
            $@"(?m)(?:^|;)\s*{Regex.Escape(property)}\s*:\s*(?<value>[^;]+)",
            RegexOptions.CultureInvariant);
        return declaration.Success ? declaration.Groups["value"].Value.Trim() : null;
    }

    private static string? CssPropertyInRuleGroup(
        string styles,
        string selector,
        string property)
    {
        Match rule = Regex.Matches(
                styles,
                @"(?ms)(?<selectors>[^{}]+)\{(?<body>[^{}]*)\}",
                RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Single(match =>
                match.Groups["selectors"].Value
                    .Split(',')
                    .Select(candidate => candidate.Trim())
                    .Contains(selector, StringComparer.Ordinal) &&
                Regex.IsMatch(
                    match.Groups["body"].Value,
                    $@"(?m)(?:^|;)\s*{Regex.Escape(property)}\s*:",
                    RegexOptions.CultureInvariant));
        Match declaration = Regex.Match(
            rule.Groups["body"].Value,
            $@"(?m)(?:^|;)\s*{Regex.Escape(property)}\s*:\s*(?<value>[^;]+)",
            RegexOptions.CultureInvariant);
        return declaration.Success ? declaration.Groups["value"].Value.Trim() : null;
    }

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

    private sealed class FakeOverlay : IRadialMenuOverlay
    {
        public bool IsVisible { get; private set; }
        public void ShowAt(System.Drawing.Point screenPoint, RadialMenuSettings settings, int selectedSlot) =>
            IsVisible = true;
        public void Hide() => IsVisible = false;
        public void Dispose() => IsVisible = false;
    }

    private sealed class TemporarySettingsFile : IDisposable
    {
        public TemporarySettingsFile()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"leftpad-settings-mapping-{Guid.NewGuid():N}.json");
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path)) File.Delete(Path);
            if (File.Exists(Path + ".tmp")) File.Delete(Path + ".tmp");
        }
    }
}
