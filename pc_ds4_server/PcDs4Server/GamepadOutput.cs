using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace PcDs4Server;

public interface IDirectDs4Session : IDisposable
{
    void SetButton(DualShock4Button button, bool pressed);
    void SetTrigger(DualShock4Slider trigger, byte value);
    void SetLeftStick(byte x, byte y);
    void SubmitReport();
}

public interface IDirectDs4Factory
{
    IDirectDs4Session Create();
}

public sealed class VigemDirectDs4Factory : IDirectDs4Factory
{
    public IDirectDs4Session Create() => new VigemDirectDs4Session();
}

internal sealed class VigemDirectDs4Session : IDirectDs4Session
{
    private readonly ViGEmClient _client;
    private readonly IDualShock4Controller _controller;
    private bool _disposed;

    public VigemDirectDs4Session()
    {
        _client = new ViGEmClient();
        try
        {
            _controller = _client.CreateDualShock4Controller();
            _controller.Connect();
        }
        catch
        {
            _client.Dispose();
            throw;
        }
    }

    public void SetButton(DualShock4Button button, bool pressed) => _controller.SetButtonState(button, pressed);
    public void SetTrigger(DualShock4Slider trigger, byte value) => _controller.SetSliderValue(trigger, value);
    public void SetLeftStick(byte x, byte y)
    {
        _controller.SetAxisValue(DualShock4Axis.LeftThumbX, x);
        _controller.SetAxisValue(DualShock4Axis.LeftThumbY, y);
    }
    public void SubmitReport() => _controller.SubmitReport();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.Disconnect();
        _client.Dispose();
    }
}
