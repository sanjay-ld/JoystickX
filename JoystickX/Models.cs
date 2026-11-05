using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JoystickX;

public class DeviceInfoModel : INotifyPropertyChanged
{
    public Guid InstanceGuid { get; set; }
    public string Name { get; set; } = "";
    public string Identifier { get; set; } = "";
    public ushort VendorId { get; set; }
    public ushort ProductId { get; set; }
    public string Status { get; set; } = "Disconnected";
    public int AxisCount { get; set; }
    public int ButtonCount { get; set; }
    public int PovCount { get; set; }
    public string DisplayName => $"{Name} (VID {VendorId:X4} PID {ProductId:X4})";
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); return true;
    }
}

public sealed class InputSnapshot
{
    public int[] ButtonStates { get; init; } = Array.Empty<int>();
    public int[] Axes { get; init; } = Array.Empty<int>();
    public int[] Pov { get; init; } = Array.Empty<int>();
}

public sealed class MappingRow
{
    public string PhysicalInput { get; set; } = "";
    public string VirtualOutput { get; set; } = "";
}
