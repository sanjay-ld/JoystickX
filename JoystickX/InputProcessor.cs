namespace JoystickX;

public sealed class Profile
{
    public double StickDeadzone { get; set; } = 0.08;
    public double TriggerDeadzone { get; set; } = 0.04;
    public double Sensitivity { get; set; } = 1.0;
    public bool InvertLeftY { get; set; } = true;
    public bool InvertRightY { get; set; } = true;
    public int RightStickXAxis { get; set; } = 3;
    public int RightStickYAxis { get; set; } = 4;
    public Dictionary<string,int> ButtonMap { get; set; } = new();
    public Dictionary<string,int> TriggerMap { get; set; } = new();
    public Dictionary<string,string> DpadMap { get; set; } = new();
}

public sealed class InputProcessor
{
    public Profile Profile { get; }
    private readonly Dictionary<int,(int Min,int Max,int Baseline)> _triggerRanges = new();
    public InputProcessor(Profile profile) => Profile = profile;
    public short ApplyStick(int value, bool invert, double sensitivity = 1.0)
    {
        return ApplyStick(value,invert,-1,sensitivity);
    }
    public short ApplyStick(int value,bool invert,int axis,double sensitivity=1.0)
    {
        return ApplyStick(value,invert,axis,sensitivity,32768);
    }
    public short ApplyStick(int value,bool invert,int axis,double sensitivity,int center)
    {
        double normalized=value>=center
            ? (value-center)/Math.Max(1.0,65535.0-center)
            : -(center-value)/Math.Max(1.0,center);
        if (invert) normalized = -normalized;
        double deadzone = Profile.StickDeadzone;
        if (Math.Abs(normalized) < deadzone) return 0;
        normalized = Math.Sign(normalized) * Math.Min(1.0, (Math.Abs(normalized) - deadzone) / (1.0 - deadzone));
        normalized *= sensitivity * Profile.Sensitivity;
        return (short)Math.Clamp(normalized * 32767.0, -32768, 32767);
    }
    public byte ApplyTrigger(int value)
    {
        return ApplyTrigger(value,-1);
    }
    public byte ApplyTrigger(int value,int axis)
    {
        double normalized;
        if(axis>=0)
        {
            if(!_triggerRanges.TryGetValue(axis,out var range))
            {
                range=(value,value,value);
                _triggerRanges[axis]=range;
            }
            range.Min=Math.Min(range.Min,value);
            range.Max=Math.Max(range.Max,value);
            _triggerRanges[axis]=range;
            int span=range.Max-range.Min;
            if(span<512) normalized=0;
            else
            {
                bool decreases=range.Baseline-range.Min > range.Max-range.Baseline;
                normalized=decreases
                    ? Math.Clamp((range.Baseline-value)/(double)span,0,1)
                    : Math.Clamp((value-range.Min)/(double)span,0,1);
            }
        }
        else
        {
            normalized=Math.Clamp((value-32768.0)/32768.0,0,1);
        }
        if (normalized < Profile.TriggerDeadzone) return 0;
        normalized = Math.Min(1, (normalized - Profile.TriggerDeadzone) / (1 - Profile.TriggerDeadzone));
        return (byte)Math.Round(normalized * 255);
    }
    public Profile DefaultProfile()
    {
        Profile.ButtonMap.Clear();
        Profile.TriggerMap.Clear();
        Profile.DpadMap.Clear();
        Profile.InvertLeftY=true;
        Profile.InvertRightY=true;
        Profile.RightStickXAxis=3;
        Profile.RightStickYAxis=4;
        Profile.ButtonMap["A"]=0;
        Profile.ButtonMap["B"]=1;
        Profile.ButtonMap["X"]=2;
        Profile.ButtonMap["Y"]=3;
        Profile.ButtonMap["LeftShoulder"]=4;
        Profile.ButtonMap["RightShoulder"]=5;
        Profile.TriggerMap["LeftTrigger"]=-(6+1);
        Profile.TriggerMap["RightTrigger"]=-(7+1);
        Profile.ButtonMap["Back"]=8;
        Profile.ButtonMap["Start"]=9;
        Profile.ButtonMap["LeftThumb"]=10;
        Profile.ButtonMap["RightThumb"]=11;
        Profile.DpadMap["DpadUp"]="Up";
        Profile.DpadMap["DpadDown"]="Down";
        Profile.DpadMap["DpadLeft"]="Left";
        Profile.DpadMap["DpadRight"]="Right";
        return Profile;
    }
}

public sealed class ButtonMappingService
{
    public int[] GetPressedPhysicalButtons(InputSnapshot input) => input.ButtonStates.Select((v,i) => v != 0 ? i : -1).Where(i => i >= 0).ToArray();
}

public sealed class ProfileManager
{
    private static readonly string Folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JoystickX");
    public ProfileManager() => System.IO.Directory.CreateDirectory(Folder);
    public void Save(string name, Profile profile) { var json = System.Text.Json.JsonSerializer.Serialize(profile, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }); System.IO.File.WriteAllText(System.IO.Path.Combine(Folder, name + ".json"), json); }
    public Profile Load(string name) { var path = System.IO.Path.Combine(Folder, name + ".json"); return System.IO.File.Exists(path) ? System.Text.Json.JsonSerializer.Deserialize<Profile>(System.IO.File.ReadAllText(path)) ?? new Profile() : new Profile(); }
    public IEnumerable<string> Profiles() => System.IO.Directory.GetFiles(Folder,"*.json").Select(System.IO.Path.GetFileNameWithoutExtension);
}

public sealed class CalibrationService
{
    public double Deadzone { get; set; } = .08;
    public bool InvertX { get; set; }
    public bool InvertY { get; set; }
}
