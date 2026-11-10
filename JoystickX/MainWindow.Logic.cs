using System.Collections.ObjectModel;
using System.Diagnostics;

namespace JoystickX;

public partial class MainWindow
{
    private readonly DeviceManager _devices = new();
    private readonly PhysicalJoystickReader _reader = new();
    private readonly DriverManager _drivers = new();
    private readonly VirtualXboxController _virtual = new();
    private readonly ProfileManager _profiles = new();
    private readonly Profile _profile = new();
    private InputProcessor _processor = null!;
    private readonly ButtonMappingService _mapping = new();
    private readonly ObservableCollection<DeviceInfoModel> _deviceList = new();
    private readonly ObservableCollection<MappingRow> _mappingRows = new();
    private readonly CancellationTokenSource _cts = new();
    private DeviceInfoModel? _selected;
    private InputSnapshot _snapshot = new();
    private bool _detecting;
    private bool _guidedMappingActive;
    private int _guidedStep = 0;
    private readonly string[] _guidedSequence =
    {
        "A", "B", "X", "Y",
        "LeftShoulder", "RightShoulder",
        "LeftTrigger", "RightTrigger",
        "Back", "Start",
        "LeftThumb", "RightThumb",
        "DpadUp", "DpadDown", "DpadLeft", "DpadRight"
    };
    private string _detectingOutput = "A";
    private int[] _previousPressed = Array.Empty<int>();
    private readonly System.Windows.Threading.DispatcherTimer _guidedBlinkTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _guidedBlinkOn;
    private string _previousPovDirection = "Centered";
    private int _guidedBaselineLeftTrigger = 32768;
    private int _guidedBaselineRightTrigger = 32768;
    private int[] _guidedAxisBaselines = Array.Empty<int>();
    private int[] _detectingAxisBaselines = Array.Empty<int>();
    private int _guidedLeftTriggerAxis = -1;
    private int _guidedRightTriggerAxis = -1;

    private void InitializeJoystickX()
    {
        _processor = new InputProcessor(_profile); _processor.DefaultProfile();
        DeviceCombo.ItemsSource = _deviceList; MappingGrid.ItemsSource = _mappingRows;
        RefreshDevices(); RefreshProfiles(); RefreshMapping(); CheckDriver();
        _guidedBlinkTimer.Tick += (_, _) => Dispatcher.Invoke(BlinkGuidedTarget);
        _reader.Error += s => Dispatcher.Invoke(() => DeviceStatus.Text = "Reader error: " + s);
        Closing += (_, _) => { _cts.Cancel(); StopEmulation(); _reader.Dispose(); _devices.Dispose(); };
        _ = InputLoop();
    }

    private void CheckDriver() { bool ok = _drivers.IsViGEmBusInstalled(); DriverStatus.Text = ok ? "ViGEmBus service detected" : "ViGEmBus driver not detected"; DriverStatus.Foreground = new System.Windows.Media.SolidColorBrush(ok ? System.Windows.Media.Color.FromRgb(66,184,131) : System.Windows.Media.Color.FromRgb(241,165,74)); }
    private void RefreshDevices() { _deviceList.Clear(); foreach(var d in _devices.EnumerateDevices()) _deviceList.Add(d); if(_deviceList.Count>0)DeviceCombo.SelectedIndex=0; else DeviceStatus.Text="No USB game controller detected"; }
    private void RefreshProfiles() { ProfileCombo.ItemsSource=_profiles.Profiles().ToList(); if(ProfileCombo.Items.Count>0)ProfileCombo.SelectedIndex=0; }
    private void Refresh_Click(object sender,System.Windows.RoutedEventArgs e)=>RefreshDevices();
    private void DeviceCombo_SelectionChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e) { _selected=DeviceCombo.SelectedItem as DeviceInfoModel; if(_selected!=null)DeviceStatus.Text=$"{_selected.Name}\nGUID: {_selected.Identifier}\nAxes: {_selected.AxisCount}  Buttons: {_selected.ButtonCount}  POV: {_selected.PovCount}"; }
    private void Connect_Click(object sender,System.Windows.RoutedEventArgs e) { if(_selected==null){System.Windows.MessageBox.Show("Select a physical controller first.");return;} try{_reader.Connect(_selected.InstanceGuid);DeviceStatus.Text=_selected.Name+" - connected";}catch(Exception ex){System.Windows.MessageBox.Show("Could not connect:\n"+ex.Message,"Device Error",System.Windows.MessageBoxButton.OK,System.Windows.MessageBoxImage.Error);} }
    private void Start_Click(object sender,System.Windows.RoutedEventArgs e) { try{var result=_virtual.Connect();VirtualStatus.Text="Virtual Xbox 360 Controller: "+result;EmulationStatus.Text="Emulation started";StartButton.IsEnabled=false;StopButton.IsEnabled=true;}catch(Exception ex){System.Windows.MessageBox.Show(ex.Message+"\nInstall the official signed ViGEmBus driver, then restart JoystickX.","Virtual Controller Error",System.Windows.MessageBoxButton.OK,System.Windows.MessageBoxImage.Error);CheckDriver();} }
    private void Stop_Click(object sender,System.Windows.RoutedEventArgs e)=>StopEmulation();
    private void StopEmulation(){_virtual.ReleaseAll();_virtual.Disconnect();VirtualStatus.Text="Virtual Xbox 360 Controller: disconnected";EmulationStatus.Text="Emulation stopped";StartButton.IsEnabled=true;StopButton.IsEnabled=false;}
    private async Task InputLoop()
    {
        while(!_cts.IsCancellationRequested)
        {
            try
            {
                _snapshot=_reader.ReadSnapshot();
                string currentPovDirection=GetPovDirection(_snapshot);
                if(_virtual.IsConnected)_virtual.Submit(_snapshot,_processor);

                if(_detecting)
                {
                    var pressed=_mapping.GetPressedPhysicalButtons(_snapshot);
                    var found=pressed.Except(_previousPressed).FirstOrDefault(i=>i>=0);
                    int detectedTriggerAxis=-1;
                    if(_detectingOutput=="LeftTrigger" || _detectingOutput=="RightTrigger")
                    {
                        for(int axis=0;axis<_snapshot.Axes.Length;axis++)
                        {
                            int baseline=axis<_detectingAxisBaselines.Length?_detectingAxisBaselines[axis]:32768;
                            if(Math.Abs(_snapshot.Axes[axis]-baseline)>3000)
                            { detectedTriggerAxis=axis; break; }
                        }
                    }
                    if(detectedTriggerAxis>=0)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _detecting=false;
                            _profile.TriggerMap[_detectingOutput]=detectedTriggerAxis;
                            RefreshMapping();
                            HighlightSelectedMapping(_detectingOutput);
                        });
                    }
                    else if(found>=0)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _detecting=false;
                            if(_detectingOutput=="LeftTrigger" || _detectingOutput=="RightTrigger")
                            {
                                _profile.TriggerMap[_detectingOutput]=-(found+1);
                                RefreshMapping();
                                HighlightSelectedMapping(_detectingOutput);
                            }
                            else if(IsPhysicalButtonAlreadyMapped(found,_detectingOutput,out string existingOutput))
                            {
                                GuidedStatus.Text=$"Physical Button {found+1} is already assigned to {GetShortOutputName(existingOutput)}. Press a different button.";
                                DeviceStatus.Text=GuidedStatus.Text;
                            }
                            else
                            {
                                _profile.ButtonMap[_detectingOutput]=found;
                                RefreshMapping();
                                HighlightSelectedMapping(_detectingOutput);
                            }
                        });
                    }
                    _previousPressed=pressed;
                }

                if(_guidedMappingActive)
                {
                    var pressed=_mapping.GetPressedPhysicalButtons(_snapshot);
                    var found=pressed.Except(_previousPressed).FirstOrDefault(i=>i>=0);
                    var target=_guidedSequence[_guidedStep];
                    bool triggerDetected=TryDetectGuidedTrigger(target,out bool leftTrigger);
                    string? dpadDetected=target.StartsWith("Dpad",StringComparison.Ordinal)
                        ? TryDetectGuidedDpad(target,currentPovDirection)
                        : null;
                    if(found>=0 || triggerDetected || dpadDetected!=null)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if(triggerDetected)
                            {
                                string triggerOutput=leftTrigger ? "LeftTrigger" : "RightTrigger";
                                int triggerAxis=leftTrigger?_guidedLeftTriggerAxis:_guidedRightTriggerAxis;
                                if(triggerAxis>=0 && !_profile.TriggerMap.ContainsValue(triggerAxis))
                                {
                                    _profile.TriggerMap[triggerOutput]=triggerAxis;
                                    AdvanceGuidedMapping();
                                }
                                return;
                            }
                            if(dpadDetected!=null)
                            {
                                if(!_profile.DpadMap.ContainsValue(dpadDetected))
                                {
                                    _profile.DpadMap[target]=dpadDetected;
                                    AdvanceGuidedMapping();
                                }
                                else
                                {
                                    GuidedStatus.Text=$"Physical {dpadDetected} D-pad direction is already mapped. Press a different direction.";
                                }
                                return;
                            }
                            if(target=="LeftTrigger" || target=="RightTrigger")
                            {
                                if(found>=0)
                                {
                                    _profile.TriggerMap[target]=-(found+1);
                                    AdvanceGuidedMapping();
                                }
                                return;
                            }
                            if(IsPhysicalButtonAlreadyMapped(found,target,out string existingOutput))
                            {
                                GuidedStatus.Text=$"Physical Button {found+1} is already mapped to {GetShortOutputName(existingOutput)}. Please use another button.";
                                DeviceStatus.Text=GuidedStatus.Text;
                                _previousPressed=pressed;
                            }
                            else
                            {
                                _profile.ButtonMap[target]=found;
                                AdvanceGuidedMapping();
                            }
                        });
                    }
                    else
                    {
                        _previousPressed=pressed;
                    }
                }

                _previousPovDirection=currentPovDirection;
                Dispatcher.Invoke(UpdateMonitor,System.Windows.Threading.DispatcherPriority.Background);
                Dispatcher.Invoke(()=>HighlightPressedMappings(_snapshot),System.Windows.Threading.DispatcherPriority.Background);
                await Task.Delay(5,_cts.Token);
            }
            catch(OperationCanceledException){break;}
            catch(Exception ex){Dispatcher.Invoke(()=>DeviceStatus.Text="Input loop error: "+ex.Message);await Task.Delay(500);}
        }
    }
    private void UpdateMonitor(){var s=_snapshot;_processor.LockRightStickAxes(s);int Axis(int i)=>s.Axes.Length>i?s.Axes[i]:32768;int Center(int i)=>s.AxisCenters.Length>i?s.AxisCenters[i]:32768;short N(int i,bool inv)=>_processor.ApplyStick(Axis(i),inv,i,1.0,Center(i));LX.Value=N(0,false);LY.Value=N(1,_profile.InvertLeftY);int rightX=_profile.RightStickXAxis;int rightY=_profile.RightStickYAxis;RX.Value=_processor.ApplyStick(Axis(rightX),false,rightX,1.0,Center(rightX));RY.Value=_processor.ApplyStick(Axis(rightY),_profile.InvertRightY,rightY,1.0,Center(rightY));LeftStickText.Text=$"Left X: {(short)LX.Value}   Left Y: {(short)LY.Value}";RightStickText.Text=$"Right X: {(short)RX.Value}   Right Y: {(short)RY.Value}";int leftTriggerAxis=_profile.TriggerMap.TryGetValue("LeftTrigger",out int leftAxis)?leftAxis:2;int rightTriggerAxis=_profile.TriggerMap.TryGetValue("RightTrigger",out int rightAxis)?rightAxis:5;LT.Value=leftTriggerAxis>=0?_processor.ApplyTrigger(Axis(leftTriggerAxis),leftTriggerAxis):(byte)(s.ButtonStates.Length> -leftTriggerAxis-1&&s.ButtonStates[-leftTriggerAxis-1]!=0?255:0);RT.Value=rightTriggerAxis>=0?_processor.ApplyTrigger(Axis(rightTriggerAxis),rightTriggerAxis):(byte)(s.ButtonStates.Length> -rightTriggerAxis-1&&s.ButtonStates[-rightTriggerAxis-1]!=0?255:0);LTText.Text=$"LT: {(byte)LT.Value}";RTText.Text=$"RT: {(byte)RT.Value}";if(ButtonPanel.Children.Count==0)for(int i=0;i<16;i++)ButtonPanel.Children.Add(new System.Windows.Controls.Border{Margin=new System.Windows.Thickness(3),CornerRadius=new System.Windows.CornerRadius(5),Padding=new System.Windows.Thickness(9,5,9,5),Background=System.Windows.Media.Brushes.DarkSlateGray,Child=new System.Windows.Controls.TextBlock{Text=$"B{i+1}",Foreground=System.Windows.Media.Brushes.White}});for(int i=0;i<ButtonPanel.Children.Count;i++){var b=(System.Windows.Controls.Border)ButtonPanel.Children[i];b.Background=new System.Windows.Media.SolidColorBrush(i<s.ButtonStates.Length&&s.ButtonStates[i]!=0?System.Windows.Media.Color.FromRgb(66,184,131):System.Windows.Media.Color.FromRgb(37,41,50));}DpadText.Text=s.Pov.Length>0?DpadName(s.Pov[0]):"Centered / unavailable";}
    private static string DpadName(int p)=>p switch{0=>"Up",4500=>"Up + Right",9000=>"Right",13500=>"Right + Down",18000=>"Down",22500=>"Down + Left",27000=>"Left",31500=>"Left + Up",-1=>"Centered",_=>"Centered"};
    private void OpenDriver_Click(object sender,System.Windows.RoutedEventArgs e)=>Process.Start(new ProcessStartInfo{FileName=DriverManager.OfficialDownloadUrl,UseShellExecute=true});
    private void SaveProfile_Click(object sender,System.Windows.RoutedEventArgs e){if(string.IsNullOrWhiteSpace(ProfileName.Text))return;ApplySettings();_profiles.Save(ProfileName.Text,_profile);RefreshProfiles();}
    private void LoadProfile_Click(object sender,System.Windows.RoutedEventArgs e){if(ProfileCombo.SelectedItem is not string name)return;var p=_profiles.Load(name);_profile.StickDeadzone=p.StickDeadzone;_profile.TriggerDeadzone=p.TriggerDeadzone;_profile.Sensitivity=p.Sensitivity;_profile.InvertLeftY=p.InvertLeftY;_profile.InvertRightY=p.InvertRightY;_profile.RightStickXAxis=p.RightStickXAxis;_profile.RightStickYAxis=p.RightStickYAxis;DeadzoneSlider.Value=p.StickDeadzone;TriggerSlider.Value=p.TriggerDeadzone;SensitivitySlider.Value=p.Sensitivity;InvertLeftY.IsChecked=p.InvertLeftY;InvertRightY.IsChecked=p.InvertRightY;_profile.ButtonMap.Clear();foreach(var x in p.ButtonMap)_profile.ButtonMap[x.Key]=x.Value;_profile.TriggerMap.Clear();foreach(var x in p.TriggerMap)_profile.TriggerMap[x.Key]=x.Value;_profile.DpadMap.Clear();foreach(var x in p.DpadMap)_profile.DpadMap[x.Key]=x.Value;RefreshMapping();}
    private void ApplySettings(){_profile.StickDeadzone=DeadzoneSlider.Value;_profile.TriggerDeadzone=TriggerSlider.Value;_profile.Sensitivity=SensitivitySlider.Value;_profile.InvertLeftY=InvertLeftY.IsChecked==true;_profile.InvertRightY=InvertRightY.IsChecked==true;}
    private void ResetMapping_Click(object sender,System.Windows.RoutedEventArgs e){_processor.DefaultProfile();RefreshMapping();}
    private void RefreshMapping()
    {
        _mappingRows.Clear();
        string[] outputs =
        {
            "A","B","X","Y","LeftShoulder","RightShoulder",
            "LeftTrigger","RightTrigger","Back","Start","LeftThumb","RightThumb",
            "DpadUp","DpadDown","DpadLeft","DpadRight"
        };
        foreach(string output in outputs)
        {
            string physicalText="None";
            if(output=="LeftTrigger"||output=="RightTrigger")
            {
                if(_profile.TriggerMap.TryGetValue(output,out int axis))
                    physicalText=axis>=0 ? $"Physical Trigger Axis {axis+1}" : $"Physical Button {-axis}";
            }
            else if(output.StartsWith("Dpad",StringComparison.Ordinal))
            {
                if(_profile.DpadMap.TryGetValue(output,out string? direction))
                    physicalText=$"Physical D-pad {direction}";
            }
            else if(_profile.ButtonMap.TryGetValue(output,out int physical))
            {
                physicalText=$"Physical Button {physical+1}";
            }
            _mappingRows.Add(new MappingRow
            {
                PhysicalInput=physicalText,
                VirtualOutput=GetShortOutputName(output)
            });
        }
        void Label(System.Windows.Controls.TextBlock label,string output)
        {
            label.Text=_profile.ButtonMap.TryGetValue(output,out int physical)
                ? $"{GetShortOutputName(output)}: B{physical+1}"
                : $"{GetShortOutputName(output)}: -";
        }
        Label(AMapLabel,"A");Label(BMapLabel,"B");Label(XMapLabel,"X");Label(YMapLabel,"Y");
        Label(LBMapLabel,"LeftShoulder");Label(RBMapLabel,"RightShoulder");
        Label(BackMapLabel,"Back");Label(StartMapLabel,"Start");
        Label(LSMapLabel,"LeftThumb");Label(RSMapLabel,"RightThumb");
        HighlightSelectedMapping(_detectingOutput);
    }
    private static string GetShortOutputName(string output)=>output switch
    {
        "LeftShoulder"=>"LB","RightShoulder"=>"RB","Back"=>"View","Start"=>"Menu",
        "LeftThumb"=>"LS","RightThumb"=>"RS","LeftTrigger"=>"LT","RightTrigger"=>"RT",
        "DpadUp"=>"D-pad Up","DpadDown"=>"D-pad Down","DpadLeft"=>"D-pad Left","DpadRight"=>"D-pad Right",_=>output
    };
    private void DetectButton_Click(object sender,System.Windows.RoutedEventArgs e)
    {
        _detectingOutput=(MapTargetCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "A";
        _detecting=true;
        _previousPressed=_mapping.GetPressedPhysicalButtons(_snapshot);
        _detectingAxisBaselines=_snapshot.Axes.ToArray();
        DeviceStatus.Text=$"Press and hold a physical button to map it to Xbox {_detectingOutput}";
        HighlightSelectedMapping(_detectingOutput);
    }
    private void MappingButton_Click(object sender,System.Windows.Input.MouseButtonEventArgs e)
    {
        if(sender is not System.Windows.Shapes.Shape shape || shape.Tag is not string output)return;
        _detectingOutput=output;
        foreach(System.Windows.Controls.ComboBoxItem item in MapTargetCombo.Items)
        {
            if(item.Content?.ToString()==output){MapTargetCombo.SelectedItem=item;break;}
        }
        HighlightSelectedMapping(output);
        DeviceStatus.Text=$"{GetShortOutputName(output)} selected. Press Detect Physical Button, then press a joystick button.";
    }
    private void HighlightSelectedMapping(string output)
    {
        var shapes=new Dictionary<string,System.Windows.Shapes.Shape>
        {
            ["A"]=AButtonVisual,["B"]=BButtonVisual,["X"]=XButtonVisual,["Y"]=YButtonVisual,
            ["LeftShoulder"]=LBVisual,["RightShoulder"]=RBVisual,
            ["Back"]=BackVisual,["Start"]=StartVisual,
            ["LeftThumb"]=LeftThumbVisual,["RightThumb"]=RightThumbVisual,
            ["LeftTrigger"]=LTVisual,["RightTrigger"]=RTVisual,
            ["DpadUp"]=DpadUpVisual,["DpadDown"]=DpadDownVisual,
            ["DpadLeft"]=DpadLeftVisual,["DpadRight"]=DpadRightVisual
        };
        foreach(var shape in shapes.Values)
        {
            shape.Stroke=System.Windows.Media.Brushes.Transparent;
            shape.StrokeThickness=1;
        }
        AButtonVisual.Fill=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(66,165,245));
        BButtonVisual.Fill=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239,83,80));
        XButtonVisual.Fill=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(66,165,245));
        YButtonVisual.Fill=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255,202,40));
        LBVisual.Fill=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74,80,93));
        RBVisual.Fill=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74,80,93));
        BackVisual.Fill=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74,80,93));
        StartVisual.Fill=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74,80,93));
        LeftThumbVisual.Fill=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32,36,43));
        RightThumbVisual.Fill=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32,36,43));
        LTVisual.Fill=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74,80,93));
        RTVisual.Fill=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74,80,93));
        foreach(var dpad in new System.Windows.Shapes.Shape[]{DpadUpVisual,DpadDownVisual,DpadLeftVisual,DpadRightVisual})
        {
            dpad.Fill=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32,36,43));
        }
        if(shapes.TryGetValue(output,out var selected))
        {
            selected.Stroke=System.Windows.Media.Brushes.LimeGreen;
            selected.StrokeThickness=5;
        }
    }

    private void StartGuidedMapping_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_selected == null)
        {
            System.Windows.MessageBox.Show("Please connect a physical controller first.");
            return;
        }
        _profile.ButtonMap.Clear();
        _profile.TriggerMap.Clear();
        _profile.DpadMap.Clear();
        RefreshMapping();
        _guidedStep = 0;
        _guidedMappingActive = true;
        _detecting = false;
        _previousPressed=_mapping.GetPressedPhysicalButtons(_snapshot);
        _guidedAxisBaselines=_snapshot.Axes.ToArray();
        _guidedLeftTriggerAxis=-1;
        _guidedRightTriggerAxis=-1;
        _previousPovDirection=GetPovDirection(_snapshot);
        GuidedMappingOverlay.Visibility=System.Windows.Visibility.Visible;
        _guidedBlinkOn=true;
        _guidedBlinkTimer.Start();
        UpdateGuidedStep();
    }
    private void AdvanceGuidedMapping()
    {
        RefreshMapping();
        _previousPressed=_mapping.GetPressedPhysicalButtons(_snapshot);
        _guidedAxisBaselines=_snapshot.Axes.ToArray();
        _previousPovDirection=GetPovDirection(_snapshot);
        _guidedStep++;
        if(_guidedStep>=_guidedSequence.Length)
            FinishGuidedMapping();
        else
            UpdateGuidedStep();
    }
    private bool TryDetectGuidedTrigger(string target,out bool leftTrigger)
    {
        leftTrigger=target=="LeftTrigger";
        if(target!="LeftTrigger"&&target!="RightTrigger")return false;
        int otherMappedAxis=leftTrigger?_guidedRightTriggerAxis:_guidedLeftTriggerAxis;
        for(int axis=0;axis<_snapshot.Axes.Length;axis++)
        {
            if(axis==otherMappedAxis)continue;
            int baseline=axis<_guidedAxisBaselines.Length?_guidedAxisBaselines[axis]:32768;
            if(Math.Abs(_snapshot.Axes[axis]-baseline)>3000)
            {
                if(leftTrigger)_guidedLeftTriggerAxis=axis;
                else _guidedRightTriggerAxis=axis;
                return true;
            }
        }
        return false;
    }
    private static string GetPovDirection(InputSnapshot snapshot)
    {
        if(snapshot.Pov.Length==0)return "Centered";
        return DpadName(snapshot.Pov[0]);
    }
    private string? TryDetectGuidedDpad(string target,string currentDirection)
    {
        string wanted=target switch
        {
            "DpadUp"=>"Up","DpadDown"=>"Down","DpadLeft"=>"Left","DpadRight"=>"Right",_=>""
        };
        if(wanted.Length==0)return null;
        bool changed=currentDirection!=_previousPovDirection;
        bool includes=currentDirection.Contains(wanted,StringComparison.Ordinal);
        return changed&&includes?wanted:null;
    }
    private void UpdateGuidedStep()
    {
        string target=_guidedSequence[_guidedStep];
        GuidedProgress.Text=$"{_guidedStep+1}/{_guidedSequence.Length}";
        GuidedProgressBar.Maximum=_guidedSequence.Length;
        GuidedProgressBar.Value=_guidedStep+1;
        GuidedPrompt.Text=$"Press the {GetShortOutputName(target)} equivalent on your gamepad.";
        GuidedStatus.Text="Keep the button pressed briefly, then release it.";
        _detectingOutput=target;
        HighlightSelectedMapping(target);
        DeviceStatus.Text=$"Guided Setup: press physical button for Xbox {GetShortOutputName(target)}";
    }
    private void BlinkGuidedTarget()
    {
        if(!_guidedMappingActive)return;
        _guidedBlinkOn=!_guidedBlinkOn;
        HighlightSelectedMapping(_guidedSequence[_guidedStep]);
        if(_guidedBlinkOn)
        {
            var shape=GetMappingShape(_guidedSequence[_guidedStep]);
            if(shape!=null){shape.Fill=System.Windows.Media.Brushes.LimeGreen;shape.Stroke=System.Windows.Media.Brushes.White;shape.StrokeThickness=6;}
        }
    }
    private System.Windows.Shapes.Shape? GetMappingShape(string output)=>output switch
    {
        "A"=>AButtonVisual,"B"=>BButtonVisual,"X"=>XButtonVisual,"Y"=>YButtonVisual,
        "LeftShoulder"=>LBVisual,"RightShoulder"=>RBVisual,
        "Back"=>BackVisual,"Start"=>StartVisual,
        "LeftThumb"=>LeftThumbVisual,"RightThumb"=>RightThumbVisual,
        "LeftTrigger"=>LTVisual,"RightTrigger"=>RTVisual,
        "DpadUp"=>DpadUpVisual,"DpadDown"=>DpadDownVisual,
        "DpadLeft"=>DpadLeftVisual,"DpadRight"=>DpadRightVisual,_=>null
    };
    private bool IsPhysicalButtonAlreadyMapped(int physical,string currentOutput,out string existingOutput)
    {
        foreach(var pair in _profile.ButtonMap)
        {
            if(pair.Value==physical && pair.Key!=currentOutput)
            {
                existingOutput=pair.Key;
                return true;
            }
        }
        existingOutput="";
        return false;
    }
    private void FinishGuidedMapping()
    {
        _guidedMappingActive=false;
        _guidedBlinkTimer.Stop();
        GuidedMappingOverlay.Visibility=System.Windows.Visibility.Collapsed;
        DeviceStatus.Text="Guided mapping complete!";
        RefreshMapping();
        System.Windows.MessageBox.Show("All buttons have been mapped successfully!");
    }
    private void CancelGuidedMapping_Click(object sender,System.Windows.RoutedEventArgs e)
    {
        _guidedMappingActive=false;
        _guidedBlinkTimer.Stop();
        _guidedStep=0;
        GuidedMappingOverlay.Visibility=System.Windows.Visibility.Collapsed;
        DeviceStatus.Text="Guided mapping cancelled.";
        RefreshMapping();
    }
    private void HighlightPressedMappings(InputSnapshot snapshot)
    {
        if(!_guidedMappingActive)
            HighlightSelectedMapping(_detectingOutput);
        foreach(var pair in _profile.ButtonMap)
        {
            int physical=pair.Value;
            if(snapshot.ButtonStates.Length<=physical || snapshot.ButtonStates[physical]==0)continue;
            var shape=pair.Key switch
            {
                "A"=>AButtonVisual,"B"=>BButtonVisual,"X"=>XButtonVisual,"Y"=>YButtonVisual,
                "LeftShoulder"=>LBVisual,"RightShoulder"=>RBVisual,
                "Back"=>BackVisual,"Start"=>StartVisual,
                "LeftThumb"=>LeftThumbVisual,"RightThumb"=>RightThumbVisual,
                "LeftTrigger"=>LTVisual,"RightTrigger"=>RTVisual,
                "DpadUp"=>DpadUpVisual,"DpadDown"=>DpadDownVisual,
                "DpadLeft"=>DpadLeftVisual,"DpadRight"=>DpadRightVisual,_=>null
            };
            if(shape is not null){shape.Fill=System.Windows.Media.Brushes.LimeGreen;shape.Stroke=System.Windows.Media.Brushes.White;shape.StrokeThickness=4;}
        }
    }
    private void DeadzoneSlider_ValueChanged(object sender,System.Windows.RoutedPropertyChangedEventArgs<double> e){if(_processor!=null)_profile.StickDeadzone=e.NewValue;}
}
