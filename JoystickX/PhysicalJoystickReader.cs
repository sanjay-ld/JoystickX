using SharpDX.DirectInput;

namespace JoystickX;

public sealed class PhysicalJoystickReader : IDisposable
{
    private Joystick? _joystick;
    public event Action<string>? Error;
    public bool IsConnected => _joystick != null;
    public InputSnapshot ReadSnapshot()
    {
        if (_joystick is null) return new InputSnapshot();
        try
        {
            _joystick.Poll();
            var state = _joystick.GetCurrentState();
            var sliders = state.Sliders ?? Array.Empty<int>();
            var axes = new[] { state.X, state.Y, state.Z, state.RotationX, state.RotationY, state.RotationZ }
                .Concat(sliders).ToArray();
            return new InputSnapshot { Axes = axes, ButtonStates = state.Buttons?.Select(b => b ? 1 : 0).ToArray() ?? Array.Empty<int>(), Pov = state.PointOfViewControllers ?? Array.Empty<int>() };
        }
        catch (SharpDX.SharpDXException ex)
        {
            Error?.Invoke(ex.Message); Disconnect(); return new InputSnapshot();
        }
    }
    public bool Connect(Guid instanceGuid)
    {
        Disconnect();
        var directInput = new DirectInput();
        var joystick = new Joystick(directInput, instanceGuid);
        joystick.Properties.BufferSize = 128;
        foreach (var effect in joystick.GetObjects(DeviceObjectTypeFlags.Axis)) { }
        joystick.Acquire();
        _joystick = joystick; _keepDirectInput = directInput; return true;
    }
    private DirectInput? _keepDirectInput;
    public void Disconnect()
    {
        try { _joystick?.Unacquire(); } catch { }
        _joystick?.Dispose(); _joystick = null;
        _keepDirectInput?.Dispose(); _keepDirectInput = null;
    }
    public void Dispose() => Disconnect();
}
