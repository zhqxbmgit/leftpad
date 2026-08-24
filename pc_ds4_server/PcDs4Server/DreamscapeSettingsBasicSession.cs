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
    internal RadialMenuSettings Draft => _draft;

    public void Activate()
    {
        ClosePreview();
        _catalog = _discoverCatalog();
        _draft = _activeSettings().NormalizeMappings();
        IsDirty = false;
        IsActive = true;
    }

    public void Deactivate()
    {
        ClosePreview();
        IsActive = false;
        IsDirty = false;
    }

    public bool TryApplyChange(
        ReceiverSettingsMessage message,
        out string rejectionReason)
    {
        rejectionReason = string.Empty;
        if (!IsActive || message.Command != ReceiverSettingsCommand.BasicChange || message.Field == null)
        {
            rejectionReason = "Settings Basic session is not active.";
            return false;
        }

        RadialMenuSettings candidate = message.Field switch
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
            _ => _draft
        };
        if (!string.IsNullOrEmpty(rejectionReason))
            return false;
        if (!candidate.TryValidate(out rejectionReason))
            return false;

        _draft = candidate;
        IsDirty = !BasicEquals(_draft, _activeSettings());
        if (IsPreviewActive)
            _updatePreview(_draft);
        return true;
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
        IsDirty = !BasicEquals(_draft, _activeSettings());
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

    private void ClosePreview()
    {
        if (IsPreviewActive)
            _hidePreview();
        IsPreviewActive = false;
    }

    private void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("Settings Basic session is not active.");
    }

    private static bool BasicEquals(RadialMenuSettings left, RadialMenuSettings right) =>
        left.VisualPackId == right.VisualPackId &&
        left.MappingProfileId == right.MappingProfileId &&
        left.ReceiverUiScalePercent == right.ReceiverUiScalePercent &&
        left.ScalePercent == right.ScalePercent &&
        left.DoubleTapWindowMs == right.DoubleTapWindowMs &&
        left.SelectionDeadZone == right.SelectionDeadZone &&
        left.HighlightAlpha == right.HighlightAlpha &&
        left.FillAlpha == right.FillAlpha &&
        left.BorderAlpha == right.BorderAlpha &&
        left.TextAlpha == right.TextAlpha;
}
