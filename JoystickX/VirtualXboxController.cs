using Microsoft.Win32;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace JoystickX;

public sealed class DriverManager
{
    public const string OfficialDownloadUrl = "https://vigem.org/downloads/";
    public bool IsViGEmBusInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\ViGEmBus");
        return key is not null;
    }
}

public sealed class VirtualXboxController : IDisposable
{
    private ViGEmClient? _client;
    private IXbox360Controller? _controller;
    private readonly object _sync = new();
    public bool IsConnected { get; private set; }
    public string Connect()
    {
        if (IsConnected) return "Already connected";
        lock (_sync)
        {
            try
            {
                _client = new ViGEmClient();
                _controller = _client.CreateXbox360Controller();
                _controller.Connect();
                IsConnected = true;
                return "Connected";
            }
            catch (Exception ex) { Dispose(); throw new InvalidOperationException("ViGEmBus virtual controller creation failed: " + ex.Message, ex); }
        }
    }
    public void Submit(InputSnapshot input, InputProcessor processor)
    {
        if (!IsConnected || _controller is null) return;
        lock (_sync)
        {
            int Axis(int index) => input.Axes.Length > index ? input.Axes[index] : 32768;
            bool Button(int index) => input.ButtonStates.Length > index && input.ButtonStates[index] != 0;
            var p = processor.Profile;
            _controller.SetAxisValue(Xbox360Axis.LeftThumbX, processor.ApplyStick(Axis(0), false));
            _controller.SetAxisValue(Xbox360Axis.LeftThumbY, processor.ApplyStick(Axis(1), p.InvertLeftY));
            _controller.SetAxisValue(Xbox360Axis.RightThumbX, processor.ApplyStick(Axis(3), false));
            _controller.SetAxisValue(Xbox360Axis.RightThumbY, processor.ApplyStick(Axis(4), p.InvertRightY));
            _controller.SetSliderValue(Xbox360Slider.LeftTrigger, 0);
            _controller.SetSliderValue(Xbox360Slider.RightTrigger, 0);
            if(p.TriggerMap.TryGetValue("LeftTrigger",out int leftTriggerAxis))
                _controller.SetSliderValue(Xbox360Slider.LeftTrigger, processor.ApplyTrigger(Axis(leftTriggerAxis)));
            if(p.TriggerMap.TryGetValue("RightTrigger",out int rightTriggerAxis))
                _controller.SetSliderValue(Xbox360Slider.RightTrigger, processor.ApplyTrigger(Axis(rightTriggerAxis)));
            foreach (var pair in p.ButtonMap)
            {
                int physical = pair.Value;
                var xboxButton = GetButton(pair.Key);
                if (xboxButton is not null) _controller.SetButtonState(xboxButton, Button(physical));
            }
            if (input.Pov.Length > 0)
            {
                var pov = input.Pov[0];
                _controller.SetButtonState(Xbox360Button.Up, p.DpadMap.TryGetValue("DpadUp",out string? upDirection) && ContainsPovDirection(pov,upDirection));
                _controller.SetButtonState(Xbox360Button.Right, p.DpadMap.TryGetValue("DpadRight",out string? rightDirection) && ContainsPovDirection(pov,rightDirection));
                _controller.SetButtonState(Xbox360Button.Down, p.DpadMap.TryGetValue("DpadDown",out string? downDirection) && ContainsPovDirection(pov,downDirection));
                _controller.SetButtonState(Xbox360Button.Left, p.DpadMap.TryGetValue("DpadLeft",out string? leftDirection) && ContainsPovDirection(pov,leftDirection));
            }
            else
            {
                _controller.SetButtonState(Xbox360Button.Up,false);
                _controller.SetButtonState(Xbox360Button.Right,false);
                _controller.SetButtonState(Xbox360Button.Down,false);
                _controller.SetButtonState(Xbox360Button.Left,false);
            }
        }
    }
    private static bool ContainsPovDirection(int pov,string direction)
    {
        return direction switch
        {
            "Up"=>pov==0||pov==4500||pov==31500,
            "Right"=>pov==9000||pov==4500||pov==13500,
            "Down"=>pov==18000||pov==13500||pov==22500,
            "Left"=>pov==27000||pov==22500||pov==31500,
            _=>false
        };
    }
    private static Xbox360Button? GetButton(string name)
    {
        name=name switch
        {
            "DpadUp"=>"Up","DpadDown"=>"Down","DpadLeft"=>"Left","DpadRight"=>"Right",
            _=>name
        };
        var field = typeof(Xbox360Button).GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        return field?.GetValue(null) as Xbox360Button;
    }
    private static IEnumerable<Xbox360Button> AllButtons()
    {
        foreach (var field in typeof(Xbox360Button).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            if (field.GetValue(null) is Xbox360Button button) yield return button;
    }
    public void ReleaseAll()
    {
        if (!IsConnected || _controller is null) return;
        lock (_sync)
        {
            foreach (var button in AllButtons()) _controller.SetButtonState(button, false);
            _controller.SetAxisValue(Xbox360Axis.LeftThumbX, 0); _controller.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
            _controller.SetAxisValue(Xbox360Axis.RightThumbX, 0); _controller.SetAxisValue(Xbox360Axis.RightThumbY, 0);
            _controller.SetSliderValue(Xbox360Slider.LeftTrigger, 0); _controller.SetSliderValue(Xbox360Slider.RightTrigger, 0);
        }
    }
    public void Disconnect()
    {
        lock (_sync)
        {
            if (_controller is not null)
            {
                try { ReleaseAllLocked(); _controller.Disconnect(); } catch { }
                _controller = null;
            }
            _client?.Dispose(); _client = null; IsConnected = false;
        }
    }
    private void ReleaseAllLocked()
    {
        if (_controller is null) return;
        foreach (var button in AllButtons()) _controller.SetButtonState(button, false);
        _controller.SetAxisValue(Xbox360Axis.LeftThumbX, 0); _controller.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
        _controller.SetAxisValue(Xbox360Axis.RightThumbX, 0); _controller.SetAxisValue(Xbox360Axis.RightThumbY, 0);
        _controller.SetSliderValue(Xbox360Slider.LeftTrigger, 0); _controller.SetSliderValue(Xbox360Slider.RightTrigger, 0);
    }
    public void Dispose() => Disconnect();
}
