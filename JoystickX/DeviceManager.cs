using SharpDX.DirectInput;

namespace JoystickX;

public sealed class DeviceManager : IDisposable
{
    private readonly DirectInput _directInput = new();
    public IReadOnlyList<DeviceInfoModel> EnumerateDevices()
    {
        var result = new List<DeviceInfoModel>();
        foreach (var device in _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AllDevices))
        {
            var info = new DeviceInfoModel
            {
                InstanceGuid = device.InstanceGuid,
                Name = device.InstanceName,
                Identifier = device.InstanceGuid.ToString(),
                VendorId = 0,
                ProductId = 0,
                Status = "Available"
            };
            try
            {
                using var joystick = new Joystick(_directInput, device.InstanceGuid);
                info.ButtonCount = joystick.Capabilities.ButtonCount;
                info.AxisCount = joystick.Capabilities.AxeCount;
                info.PovCount = joystick.Capabilities.PovCount;
            }
            catch { }
            result.Add(info);
        }
        return result;
    }
    public void Dispose() => _directInput.Dispose();
}
