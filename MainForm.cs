using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;

public class MainForm : Form
{
    private readonly Button[] _modeButtons;
    private readonly TrackBar _volumeSlider;
    private readonly Label _volumeLabel;
    private readonly TrackBar _echoDelaySlider;
    private readonly Label _echoDelayLabel;
    private readonly TrackBar _echoDecaySlider;
    private readonly Label _echoDecayLabel;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly Button _testButton;
    private readonly Label _statusLabel;
    private readonly RichTextBox _logBox;

    private readonly VoiceModEngine _engine;

    public MainForm()
    {
        try
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OtherStuff", "logo.png");
            this.Icon = new Icon(iconPath);
        }
        catch
        {
            // Icon not found, use default
        }
        Text = "Voice Modulator";
        ClientSize = new Size(360, 540);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        KeyPreview = true;
        KeyDown += MainForm_KeyDown;
        var modeNames = Enum.GetNames(typeof(ModulationMode));
        _modeButtons = new Button[modeNames.Length];
        
        for (int i = 0; i < modeNames.Length; i++)
        {
            int row = i / 3;
            int col = i % 3;
            int x = 20 + col * 110;
            int y = 20 + row * 30;
            
            _modeButtons[i] = new Button
            {
                Text = FormatModeName(modeNames[i]),
                Location = new Point(x, y),
                Size = new Size(100, 28),
                Tag = i
            };
            _modeButtons[i].Click += ModeButton_Click;
            Controls.Add(_modeButtons[i]);
        }

        _volumeLabel = new Label
        {
            Text = "Volume: 100%",
            Location = new Point(20, 110),
            Size = new Size(320, 18)
        };

        _volumeSlider = new TrackBar
        {
            Minimum = 0,
            Maximum = 200,
            Value = 100,
            TickFrequency = 20,
            SmallChange = 5,
            LargeChange = 20,
            Location = new Point(20, 126),
            Size = new Size(320, 45)
        };
        _volumeSlider.Scroll += VolumeSlider_Scroll;

        _echoDelayLabel = new Label
        {
            Text = "Echo delay: 500 ms",
            Location = new Point(20, 176),
            Size = new Size(320, 18)
        };

        _echoDelaySlider = new TrackBar
        {
            Minimum = 100,
            Maximum = 1000,
            Value = 500,
            TickFrequency = 100,
            SmallChange = 10,
            LargeChange = 100,
            Location = new Point(20, 192),
            Size = new Size(320, 45)
        };
        _echoDelaySlider.Scroll += EchoDelaySlider_Scroll;

        _echoDecayLabel = new Label
        {
            Text = "Echo decay: 40%",
            Location = new Point(20, 241),
            Size = new Size(320, 18)
        };

        _echoDecaySlider = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 40,
            TickFrequency = 10,
            SmallChange = 5,
            LargeChange = 10,
            Location = new Point(20, 257),
            Size = new Size(320, 45)
        };
        _echoDecaySlider.Scroll += EchoDecaySlider_Scroll;

        _startButton = new Button
        {
            Text = "Start",
            Location = new Point(20, 306),
            Size = new Size(150, 36)
        };
        _startButton.Click += StartButton_Click;

        _stopButton = new Button
        {
            Text = "Stop",
            Location = new Point(190, 306),
            Size = new Size(150, 36),
            Enabled = false
        };
        _stopButton.Click += StopButton_Click;

        _testButton = new Button
        {
            Text = "Test Voice",
            Location = new Point(20, 348),
            Size = new Size(320, 36)
        };
        _testButton.Click += TestButton_Click;

        _statusLabel = new Label
        {
            Text = "Ready",
            Location = new Point(20, 392),
            Size = new Size(320, 18)
        };

        _logBox = new RichTextBox
        {
            ReadOnly = true,
            Location = new Point(20, 418),
            Size = new Size(320, 90),
            Text = "Logs...",
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        Controls.Add(_volumeLabel);
        Controls.Add(_volumeSlider);
        Controls.Add(_echoDelayLabel);
        Controls.Add(_echoDelaySlider);
        Controls.Add(_echoDecayLabel);
        Controls.Add(_echoDecaySlider);
        Controls.Add(_startButton);
        Controls.Add(_stopButton);
        Controls.Add(_testButton);
        Controls.Add(_statusLabel);
        Controls.Add(_logBox);

        _engine = new VoiceModEngine();
        _engine.StatusChanged += status =>
        {
            if (InvokeRequired)
            {
                BeginInvoke(() =>
                {
                    _statusLabel.Text = status;
                    AppendLog(status);
                });
            }
            else
            {
                _statusLabel.Text = status;
                AppendLog(status);
            }
        };

        SelectModeButton(0);
        _engine.Volume = _volumeSlider.Value / 100f;
        _engine.EchoDelaySeconds = _echoDelaySlider.Value / 1000f;
        _engine.EchoDecay = _echoDecaySlider.Value / 100f;
        _statusLabel.Text = $"Mode: {_engine.Mode}, Volume: {Math.Round(_engine.Volume * 100)}%";

        FormClosing += MainForm_FormClosing;
    }

    private static string FormatModeName(string name)
    {
        return name switch
        {
            "WallE" => "Wall-E",
            "DarthVader" => "Darth Vader",
            "TacticalMissile" => "Tactical",
            "BattlefieldRadio" => "Battlefield",
            "MilitaryNarrator" => "Military",
            _ => name
        };
    }

    private void ModeButton_Click(object? sender, EventArgs e)
    {
        if (sender is Button button && button.Tag is int index)
        {
            SelectModeButton(index);
        }
    }

    private void SelectModeButton(int index)
    {
        // Update button appearance
        for (int i = 0; i < _modeButtons.Length; i++)
        {
            _modeButtons[i].BackColor = (i == index) ? SystemColors.Highlight : SystemColors.Control;
            _modeButtons[i].ForeColor = (i == index) ? Color.White : SystemColors.ControlText;
        }

        // Update engine mode
        _engine.Mode = (ModulationMode)(index + 1);
        _statusLabel.Text = $"Mode: {_engine.Mode}";
        AppendLog($"Mode set to {_engine.Mode}");

        // If testing mode is active, restart test to apply new voice effect smoothly
        if (_engine.IsTesting)
        {
            _engine.StopTest();
            _engine.StartTest();
            AppendLog($"Test restarted with {_engine.Mode} mode");
        }
    }

    private void ModeCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        SelectedModeToEngine();
    }

    private void VolumeSlider_Scroll(object? sender, EventArgs e)
    {
        float volume = _volumeSlider.Value / 100f;
        _engine.Volume = volume;
        _volumeLabel.Text = $"Volume: {Math.Round(volume * 100)}%";
        AppendLog($"Volume set to {Math.Round(volume * 100)}%");
    }

    private void EchoDelaySlider_Scroll(object? sender, EventArgs e)
    {
        float delay = _echoDelaySlider.Value / 1000f;
        _engine.EchoDelaySeconds = delay;
        _echoDelayLabel.Text = $"Echo delay: {_echoDelaySlider.Value} ms";
        AppendLog($"Echo delay set to {_echoDelaySlider.Value} ms");
    }

    private void EchoDecaySlider_Scroll(object? sender, EventArgs e)
    {
        float decay = _echoDecaySlider.Value / 100f;
        _engine.EchoDecay = decay;
        _echoDecayLabel.Text = $"Echo decay: {_echoDecaySlider.Value}%";
        AppendLog($"Echo decay set to {_echoDecaySlider.Value}%");
    }

    private void SelectedModeToEngine()
    {
        // This method is no longer needed but kept for compatibility
    }

    private void StartButton_Click(object? sender, EventArgs e)
    {
        try
        {
            _engine.Start();
            _startButton.Enabled = false;
            _stopButton.Enabled = true;
            _statusLabel.Text = $"Running ({_engine.Mode})";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Start error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = $"Error: {ex.Message}";
        }
    }

    private void StopButton_Click(object? sender, EventArgs e)
    {
        // Stop production mode
        _engine.Stop();
        _startButton.Enabled = true;
        _stopButton.Enabled = false;
        _statusLabel.Text = "Stopped";

        // Also stop test mode if it's running
        if (_engine.IsTesting)
        {
            _engine.StopTest();
            _testButton.Text = "Test Voice";
            _testButton.BackColor = SystemColors.Control;
        }
    }

    private void TestButton_Click(object? sender, EventArgs e)
    {
        if (_engine.IsTesting)
        {
            _engine.StopTest();
            _testButton.Text = "Test Voice";
            _testButton.BackColor = SystemColors.Control;
        }
        else
        {
            try
            {
                _engine.StartTest();
                _testButton.Text = "Stop Test";
                _testButton.BackColor = Color.LightGreen;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Test error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _statusLabel.Text = $"Test Error: {ex.Message}";
            }
        }
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Q)
        {
            Close();
            return;
        }

        if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9)
        {
            int modeIndex = e.KeyCode - Keys.D1;
            if (modeIndex >= 0 && modeIndex < _modeButtons.Length)
            {
                SelectModeButton(modeIndex);
                AppendLog($"Hotkey: selected {_modeButtons[modeIndex].Text}");
            }
        }

        if (e.KeyCode == Keys.Space)
        {
            if (_engine.IsRunning)
            {
                StopButton_Click(null, EventArgs.Empty);
            }
            else
            {
                StartButton_Click(null, EventArgs.Empty);
            }
        }
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (InvokeRequired)
        {
            BeginInvoke(() =>
            {
                _logBox.AppendText(line + Environment.NewLine);
                _logBox.ScrollToCaret();
            });
        }
        else
        {
            _logBox.AppendText(line + Environment.NewLine);
            _logBox.ScrollToCaret();
        }
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _engine.Dispose();
    }
}