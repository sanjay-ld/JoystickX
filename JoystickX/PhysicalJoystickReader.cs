using SharpDX.DirectInput;

namespace JoystickX;

public sealed class PhysicalJoystickReader : IDisposable
{
    private Joystick? _joystick;
    private int[] _axisCenters = Array.Empty<int>();
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
            return new InputSnapshot { Axes = axes, AxisCenters = _axisCenters, ButtonStates = state.Buttons?.Select(b => b ? 1 : 0).ToArray() ?? Array.Empty<int>(), Pov = state.PointOfViewControllers ?? Array.Empty<int>() };
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
        Joystick? joystick = null;
        try
        {
            joystick = new Joystick(directInput, instanceGuid);
            joystick.Properties.BufferSize = 128;
            joystick.Acquire();
            var axisCenters = CaptureAxisCenters(joystick);
            _joystick = joystick;
            _keepDirectInput = directInput;
            _axisCenters = axisCenters;
            return true;
        }
        catch
        {
            joystick?.Dispose();
            directInput.Dispose();
            throw;
        }
    }
    private static int[] CaptureAxisCenters(Joystick joystick)
    {
        var sums = new long[6];
        int samples = 12;
        for (int sample = 0; sample < samples; sample++)
        {
            joystick.Poll();
            var state = joystick.GetCurrentState();
            int[] axes = { state.X, state.Y, state.Z, state.RotationX, state.RotationY, state.RotationZ };
            for (int axis = 0; axis < axes.Length; axis++)
                sums[axis] += axes[axis];
            Thread.Sleep(5);
        }
        return sums.Select(sum => Math.Clamp((int)(sum / samples), 0, 65535)).ToArray();
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
