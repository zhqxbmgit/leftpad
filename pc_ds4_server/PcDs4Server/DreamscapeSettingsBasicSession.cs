namespace PcDs4Server;

internal sealed class DreamscapeSettingsBasicSession
{
    private readonly Func<RadialMenuSettings> _activeSettings;
    private readonly Func<RadialVisualPackCatalogSnapshot> _discoverCatalog;
    private readonly Action<RadialMenuSettings> _preview;
    private readonly Action<RadialMenuSettings> _updatePreview;
    private readonly Action _hidePreview;
    private readonly Func<RadialMenuSettings, (bool Success, string Error)> _applySave;
    private readonly Action<string> _log;
    private RadialVisualPackCatalogSnapshot? _catalog;
    private RadialMenuSettings _draft = RadialMenuSettings.Default;

    public DreamscapeSettingsBasicSession(
        Func<RadialMenuSettings> activeSettings,
        Func<RadialVisualPackCatalogSnapshot> discoverCatalog,
        Action<RadialMenuSettings> preview,
        Action<RadialMenuSettings> updatePreview,
        Action hidePreview,
        Func<RadialMenuSettings, (bool Success, string Error)> applySave,
        Action<string> log)
    {
        _activeSettings = activeSettings ?? throw new ArgumentNullException(nameof(activeSettings));
        _discoverCatalog = discoverCatalog ?? throw new ArgumentNullException(nameof(discoverCatalog));
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _updatePreview = updatePreview ?? throw new ArgumentNullException(nameof(updatePreview));
        _hidePreview = hidePreview ?? throw new ArgumentNullException(nameof(hidePreview));
        _applySave = applySave ?? throw new ArgumentNullException(nameof(applySave));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public bool IsActive { get; private set; }
    public bool IsPreviewActive { get; private set; }
    public bool IsDirty { get; private set; }
    public ReceiverSettingsSection ActiveSection { get; private set; } = ReceiverSettingsSection.Basic;
    public int? SelectedMappingSlot { get; private set; }
    internal RadialMenuSettings Draft => _draft;

    public void Activate()
    {
        ClosePreview();
        _catalog = _discoverCatalog();
        _draft = _activeSettings().NormalizeMappings();
        IsDirty = false;
        ActiveSection = ReceiverSettingsSection.Basic;
        SelectedMappingSlot = null;
        IsActive = true;
    }

    public void Deactivate()
    {
        ClosePreview();
        IsActive = false;
        IsDirty = false;
        SelectedMappingSlot = null;
    }

    public bool TryApplyChange(
        ReceiverSettingsMessage message,
        out string rejectionReason)
    {
        rejectionReason = string.Empty;
        if (!IsActive ||
            (message.Command != ReceiverSettingsCommand.BasicChange &&
             message.Command != ReceiverSettingsCommand.AdvancedChange &&
             message.Command != ReceiverSettingsCommand.MappingChange) ||
            (message.Command != ReceiverSettingsCommand.MappingChange && message.Field == null))
        {
            rejectionReason = "Settings session is not active or the change command is invalid.";
            return false;
        }

        string previousProfileId = _draft.MappingProfileId;
        RadialMenuSettings candidate = message.Command switch
        {
            ReceiverSettingsCommand.BasicChange => ApplyBasicChange(message, out rejectionReason),
            ReceiverSettingsCommand.AdvancedChange => ApplyAdvancedChange(message, out rejectionReason),
            ReceiverSettingsCommand.MappingChange => ApplyMappingChange(message, out rejectionReason),
            _ => _draft
        };
        if (!string.IsNullOrEmpty(rejectionReason))
            return false;
        if (!candidate.TryValidate(out rejectionReason))
            return false;

        _draft = candidate;
        if (!string.Equals(previousProfileId, _draft.MappingProfileId, StringComparison.Ordinal))
            SelectedMappingSlot = null;
        IsDirty = !SettingsEqual(_draft, _activeSettings());
        if (IsPreviewActive)
            _updatePreview(_draft);
        return true;
    }

    public void ShowBasic()
    {
        EnsureActive();
        ActiveSection = ReceiverSettingsSection.Basic;
    }

    public void ShowAdvanced()
    {
        EnsureActive();
        ActiveSection = ReceiverSettingsSection.Advanced;
    }

    public void ShowMappings()
    {
        EnsureActive();
        ActiveSection = ReceiverSettingsSection.Mapping;
    }

    public bool TrySelectMappingSlot(
        ReceiverSettingsMessage message,
        out string rejectionReason)
    {
        rejectionReason = string.Empty;
        if (!IsActive || message.Command != ReceiverSettingsCommand.MappingSelectSlot ||
            message.ProfileId == null || message.SlotId == null)
        {
            rejectionReason = "Settings session is not active or the mapping selection is invalid.";
            return false;
        }
        if (!string.Equals(message.ProfileId, _draft.MappingProfileId, StringComparison.Ordinal))
        {
            rejectionReason = $"Mapping profile '{message.ProfileId}' is not active.";
            return false;
        }
        int slotCount = ActiveLayoutDefinition().SlotCount;
        if (message.SlotId < 1 || message.SlotId > slotCount)
        {
            rejectionReason = $"Slot must be between 1 and {slotCount}.";
            return false;
        }
        SelectedMappingSlot = message.SlotId.Value;
        return true;
    }

    private RadialMenuSettings ApplyBasicChange(
        ReceiverSettingsMessage message,
        out string rejectionReason)
    {
        rejectionReason = string.Empty;
        return message.Field switch
        {
            SettingsBasicFields.VisualPackId => ChangeVisualPack(message.StringValue, out rejectionReason),
            SettingsBasicFields.ReceiverUiScalePercent => _draft with
            {
                ReceiverUiScalePercent = message.IntegerValue!.Value
            },
            SettingsBasicFields.OverallSizePercent => _draft with
            {
                ScalePercent = message.IntegerValue!.Value
            },
            SettingsBasicFields.DoubleTapWindowMs => _draft with
            {
                DoubleTapWindowMs = message.IntegerValue!.Value
            },
            SettingsBasicFields.SelectionDeadZone => _draft with
            {
                SelectionDeadZone = message.IntegerValue!.Value
            },
            SettingsBasicFields.SelectedIntensity => _draft with
            {
                HighlightAlpha = message.IntegerValue!.Value
            },
            SettingsBasicFields.PetalOpacity => _draft with
            {
                FillAlpha = message.IntegerValue!.Value
            },
            SettingsBasicFields.BorderOpacity => _draft with
            {
                BorderAlpha = message.IntegerValue!.Value
            },
            SettingsBasicFields.TextOpacity => _draft with
            {
                TextAlpha = message.IntegerValue!.Value
            },
            _ => RejectUnknownField(message.Field, out rejectionReason)
        };
    }

    private RadialMenuSettings ApplyAdvancedChange(
        ReceiverSettingsMessage message,
        out string rejectionReason)
    {
        rejectionReason = string.Empty;
        decimal value = message.DecimalValue!.Value;
        return message.Field switch
        {
            SettingsAdvancedFields.CanvasSize => _draft with { BaseCanvasSize = decimal.ToInt32(value) },
            SettingsAdvancedFields.CenterRadius => _draft with { HubRadius = decimal.ToInt32(value) },
            SettingsAdvancedFields.PetalInnerRadius => _draft with { PetalInnerRadius = decimal.ToInt32(value) },
            SettingsAdvancedFields.PetalOuterRadius => _draft with { PetalOuterRadius = decimal.ToInt32(value) },
            SettingsAdvancedFields.TextRadius => _draft with { TextRadius = decimal.ToInt32(value) },
            SettingsAdvancedFields.PetalGapDegrees => _draft with { PetalGapDegrees = (float)value },
            SettingsAdvancedFields.FontSize => _draft with { FontSize = (float)value },
            SettingsAdvancedFields.SelectionPollIntervalMs => _draft with
            {
                SelectionPollIntervalMs = decimal.ToInt32(value)
            },
            _ => RejectUnknownField(message.Field, out rejectionReason)
        };
    }

    private RadialMenuSettings ApplyMappingChange(
        ReceiverSettingsMessage message,
        out string rejectionReason)
    {
        rejectionReason = string.Empty;
        if (message.ProfileId == null || message.SlotId == null || message.ActionKind == null)
            return RejectUnknownField("mapping", out rejectionReason);
        if (!string.Equals(message.ProfileId, _draft.MappingProfileId, StringComparison.Ordinal))
        {
            rejectionReason = $"Mapping profile '{message.ProfileId}' is not active.";
            return _draft;
        }

        LayoutDefinition layout = ActiveLayoutDefinition();
        int slotId = message.SlotId.Value;
        if (slotId < 1 || slotId > layout.SlotCount)
        {
            rejectionReason = $"Slot must be between 1 and {layout.SlotCount}.";
            return _draft;
        }

        var mapping = new RadialSlotMapping
        {
            Kind = message.ActionKind.Value,
            Key = message.Key,
            Ctrl = message.Ctrl,
            Alt = message.Alt,
            Shift = message.Shift,
            Win = message.Win,
            Ds4Button = message.Ds4Action
        };
        if (!mapping.TryValidate(out rejectionReason))
            return _draft;

        SelectedMappingSlot = slotId;
        RadialSlotMappings mappings = _draft
            .GetProfileMappings(layout.ProfileId)
            .WithSlot(slotId, mapping);
        return _draft.SetProfileMappings(layout.ProfileId, mappings);
    }

    public void Preview()
    {
        EnsureActive();
        _preview(_draft);
        IsPreviewActive = true;
    }

    public void HidePreview()
    {
        EnsureActive();
        ClosePreview();
    }

    public bool ApplyAndSave(out string error)
    {
        EnsureActive();
        (bool success, string applyError) = _applySave(_draft);
        error = applyError;
        if (!success)
            return false;
        _draft = _activeSettings().NormalizeMappings();
        IsDirty = false;
        return true;
    }

    public void RestoreDefault()
    {
        EnsureActive();
        _draft = RadialMenuSettings.Default.NormalizeMappings();
        IsDirty = !SettingsEqual(_draft, _activeSettings());
        if (IsPreviewActive)
            _updatePreview(_draft);
    }

    public ReceiverSettingsBasicState CreateState(string connectionStatus)
    {
        RadialVisualPackCatalogSnapshot catalog = _catalog ?? _discoverCatalog();
        SettingsBasicVisualPackOption[] options = catalog.Packs
            .Select(pack => new SettingsBasicVisualPackOption(
                pack.Id,
                pack.Name,
                pack.Definition.LayoutDefinition.ProfileId))
            .ToArray();
        return new ReceiverSettingsBasicState(
            ReceiverSettingsBasicState.MessageType,
            connectionStatus,
            _draft.VisualPackId,
            options,
            _draft.ReceiverUiScalePercent,
            ReceiverUiScaling.Presets.ToArray(),
            _draft.ScalePercent,
            _draft.DoubleTapWindowMs,
            _draft.SelectionDeadZone,
            _draft.HighlightAlpha,
            _draft.FillAlpha,
            _draft.BorderAlpha,
            _draft.TextAlpha,
            SettingsBasicFields.Ranges,
            SettingsAdvancedFields.CreateStates(_draft),
            _draft.MappingProfileId,
            ActiveLayoutDefinition().SlotCount,
            SettingsMappingLayout.SplitIndex(ActiveLayoutDefinition().SlotCount),
            SelectedMappingSlot,
            CreateMappingStates(),
            SettingsMappingCatalogs.ActionKinds,
            SettingsMappingCatalogs.KeyboardKeys,
            SettingsMappingCatalogs.Ds4Actions,
            ActiveSection switch
            {
                ReceiverSettingsSection.Advanced => "advanced",
                ReceiverSettingsSection.Mapping => "mappings",
                _ => "basic"
            },
            IsPreviewActive,
            IsDirty,
            IsActive && options.Length > 0);
    }

    private RadialMenuSettings ChangeVisualPack(string? id, out string rejectionReason)
    {
        RadialVisualPackCatalogEntry? pack = _catalog?.Find(id);
        if (pack == null)
        {
            rejectionReason = $"Unknown production visual pack '{id ?? "<null>"}'.";
            return _draft;
        }
        rejectionReason = string.Empty;
        return _draft with
        {
            VisualPackId = pack.Id,
            MappingProfileId = pack.Definition.LayoutDefinition.ProfileId
        };
    }

    private LayoutDefinition ActiveLayoutDefinition()
    {
        RadialVisualPackCatalogEntry? pack = _catalog?.Find(_draft.VisualPackId);
        if (pack != null)
            return pack.Definition.LayoutDefinition;
        LayoutProfileRegistration profile = LayoutProfileRegistry.GetRequired(_draft.MappingProfileId);
        return new LayoutDefinition(
            profile.ProfileId,
            profile.Family,
            profile.SlotCount,
            new LayoutCanvasDefinition(1, 1, "settings"),
            new LayoutPointDefinition(0, 0),
            profile.SelectionModel,
            "settings",
            Enumerable.Range(1, profile.SlotCount)
                .Select(slot => new RadialSlotDefinition(
                    slot,
                    profile.ExpectedAngles[slot - 1],
                    new LayoutPointDefinition(0, 0),
                    new LayoutPointDefinition(0, 0))));
    }

    private SettingsMappingSlotState[] CreateMappingStates()
    {
        LayoutDefinition layout = ActiveLayoutDefinition();
        RadialSlotMappings mappings = _draft.GetProfileMappings(layout.ProfileId);
        return mappings.Select((mapping, index) => new SettingsMappingSlotState(
            index + 1,
            SettingsMappingCatalogs.KindId(mapping.Kind),
            mapping.Key?.ToString(),
            mapping.Ctrl,
            mapping.Alt,
            mapping.Shift,
            mapping.Win,
            mapping.Ds4Button)).ToArray();
    }

    private void ClosePreview()
    {
        if (IsPreviewActive)
            _hidePreview();
        IsPreviewActive = false;
    }

    private RadialMenuSettings RejectUnknownField(string? field, out string rejectionReason)
    {
        rejectionReason = $"Unknown Settings field '{field ?? "<null>"}'.";
        return _draft;
    }

    private void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("Settings session is not active.");
    }

    private static bool SettingsEqual(RadialMenuSettings left, RadialMenuSettings right) =>
        left.NormalizeMappings() == right.NormalizeMappings();
}
