namespace JoystickX;

public sealed class Profile
{
    public double StickDeadzone { get; set; } = 0.08;
    public double TriggerDeadzone { get; set; } = 0.04;
    public double Sensitivity { get; set; } = 1.0;
    public bool InvertLeftY { get; set; } = false;
    public bool InvertRightY { get; set; } = false;
    public Dictionary<string,int> ButtonMap { get; set; } = new();
    public Dictionary<string,int> TriggerMap { get; set; } = new();
    public Dictionary<string,string> DpadMap { get; set; } = new();
}

public sealed class InputProcessor
{
    public Profile Profile { get; }
    public InputProcessor(Profile profile) => Profile = profile;
    public short ApplyStick(int value, bool invert, double sensitivity = 1.0)
    {
        double normalized = (value - 32768.0) / 32768.0;
        if (invert) normalized = -normalized;
        double deadzone = Profile.StickDeadzone;
        if (Math.Abs(normalized) < deadzone) return 0;
        normalized = Math.Sign(normalized) * Math.Min(1.0, (Math.Abs(normalized) - deadzone) / (1.0 - deadzone));
        normalized *= sensitivity * Profile.Sensitivity;
        return (short)Math.Clamp(normalized * 32767.0, -32768, 32767);
    }
    public byte ApplyTrigger(int value)
    {
        double normalized = Math.Clamp((value - 32768.0) / 32768.0, 0, 1);
        if (normalized < Profile.TriggerDeadzone) return 0;
        normalized = Math.Min(1, (normalized - Profile.TriggerDeadzone) / (1 - Profile.TriggerDeadzone));
        return (byte)Math.Round(normalized * 255);
    }
    public Profile DefaultProfile()
    {
        Profile.ButtonMap.Clear();
        Profile.TriggerMap.Clear();
        Profile.DpadMap.Clear();
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
