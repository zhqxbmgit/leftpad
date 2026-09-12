namespace PcDs4Server;

internal sealed class StartupToggleControl : UserControl
{
    private readonly CheckBox _toggle = new()
    {
        Name = "startWithWindowsCheckBox",
        Text = "Start with Windows",
        AutoSize = true,
        Dock = DockStyle.Left,
        ForeColor = ThemeColors.TextMain
    };
    private readonly Label _notice = new()
    {
        Name = "startWithWindowsNotice",
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        ForeColor = ThemeColors.Error,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private StartupRegistration? _registration;
    private bool _applyingState;

    public StartupToggleControl()
    {
        Name = "startWithWindowsControl";
        MinimumSize = new Size(180, 22);
        Controls.Add(_notice);
        Controls.Add(_toggle);
        _toggle.CheckedChanged += (_, _) => ToggleChanged();
    }

    internal bool EnabledFromRegistry => _toggle.Checked;
    internal string Notice => _notice.Text;

    internal void Initialize(StartupRegistration registration)
    {
        if (_registration != null)
            throw new InvalidOperationException("Windows startup is already initialized.");

        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        _toggle.Enabled = true;
        Apply(registration.Read());
    }

    internal void SetEnabledForTesting(bool enabled)
    {
        if (_registration is null)
            throw new InvalidOperationException("Windows startup has not been initialized.");

        _toggle.Checked = enabled;
    }

    private void ToggleChanged()
    {
        if (_applyingState || _registration is null) return;
        Apply(_registration.SetEnabled(_toggle.Checked));
    }

    private void Apply(StartupState state)
    {
        _applyingState = true;
        try
        {
            _toggle.Checked = state.Enabled;
            _notice.Text = state.Notice;
            _notice.Visible = state.Notice.Length != 0;
        }
        finally
        {
            _applyingState = false;
        }
    }
}
