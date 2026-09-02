namespace PcDs4Server;

internal sealed class RadialMouseDismissTracker
{
    private bool _isTracking;
    private bool _previousLeftButtonDown;

    public void Reset(bool initialLeftButtonDown)
    {
        _previousLeftButtonDown = initialLeftButtonDown;
        _isTracking = true;
    }

    public bool Observe(bool currentLeftButtonDown)
    {
        if (!_isTracking) return false;

        bool isNewPress = !_previousLeftButtonDown && currentLeftButtonDown;
        _previousLeftButtonDown = currentLeftButtonDown;
        return isNewPress;
    }

    public void Clear()
    {
        _isTracking = false;
        _previousLeftButtonDown = false;
    }
}
