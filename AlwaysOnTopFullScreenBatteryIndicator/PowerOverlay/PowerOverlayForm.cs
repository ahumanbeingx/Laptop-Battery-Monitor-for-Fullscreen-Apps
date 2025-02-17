using System.Runtime.InteropServices;
using Timer = System.Windows.Forms.Timer;

namespace PowerOverlay;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new BatteryMonitor());
    }
}

public sealed class BatteryMonitor : Form
{
    // Win32 API constants and imports
    private const int HwndTopMost = -1;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoSize = 0x0001;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int x, int y, int cx, int cy,
        uint uFlags);

    // Mouse tracking
    private Point _lastLocation;
    private bool _isDragging;

    // Form properties
    private readonly Timer _updateTimer;
    private string _displayText = "";
    private bool _isVisible = true;
    private readonly Font _font;
    private readonly SolidBrush _textBrush;
    private readonly SolidBrush _bgBrush;
    private readonly ContextMenuStrip _contextMenu;
    private readonly Point _defaultLocation;
    
    // Settings form
    private Form? _settingsForm;
    private int _transparency = 100; // Start at 100 (full opacity)

    // Battery status colors
    private static readonly Color HealthyColor = Color.LimeGreen;
    private static readonly Color WarningColor = Color.Yellow;
    private static readonly Color CriticalColor = Color.Red;
    private bool _shouldFlash;
    private readonly Timer _flashTimer;

    public BatteryMonitor()
    {
        // Initialize default location
        var screen = Screen.PrimaryScreen;
        _defaultLocation = screen != null 
            ? new Point(screen.WorkingArea.Width - 220, 10)
            : new Point(100, 10);

        // Create resources first (no virtual calls)
        _font = new Font("Segoe UI", 12);
        _textBrush = new SolidBrush(Color.LimeGreen);
        _bgBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
        _contextMenu = new ContextMenuStrip();
        _updateTimer = new Timer { Interval = 100 };
        
        InitializeFormProperties();
        CreateContextMenu();
        CreateTimer();
        RegisterEvents();

        // Add flash timer
        _flashTimer = new Timer
        {
            Interval = 500, // Flash twice per second
            Enabled = false
        };
        _flashTimer.Tick += (_, _) => 
        {
            _shouldFlash = !_shouldFlash;
            Invalidate();
        };

        // Fix taskbar icon
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        ShowIcon = true;

        // Initial state
        UpdateBatteryStatus();
        ForceToTop();
    }

    private void InitializeFormProperties()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = true;
        TopMost = true;
        BackColor = Color.Black;
        Size = new Size(200, 50);
        StartPosition = FormStartPosition.Manual;
        Location = _defaultLocation;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _updateTimer.Dispose();
            _font.Dispose();
            _textBrush.Dispose();
            _bgBrush.Dispose();
            _contextMenu.Dispose();
            _flashTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void CreateContextMenu()
    {
        _contextMenu.Items.Clear();
        _contextMenu.Items.Add("Settings", null, (_, _) => ShowSettings());
        _contextMenu.Items.Add("-"); // Separator
        _contextMenu.Items.Add("Exit", null, (_, _) => Application.Exit());
        ContextMenuStrip = _contextMenu;
    }

    private void CreateTimer()
    {
        _updateTimer.Tick += TimerTick;
        _updateTimer.Enabled = true;
    }

    private void TimerTick(object? sender, EventArgs e)
    {
        UpdateBatteryStatus();
        ForceToTop();
    }

    private void RegisterEvents()
    {
        MouseDown += HandleMouseDown;
        MouseMove += HandleMouseMove;
        MouseUp += HandleMouseUp;
    }

    private void HandleMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _isDragging = true;
            _lastLocation = e.Location;
        }
    }

    private void HandleMouseMove(object? sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            Location = new Point(
                Location.X + (e.X - _lastLocation.X),
                Location.Y + (e.Y - _lastLocation.Y)
            );
        }
    }

    private void HandleMouseUp(object? sender, MouseEventArgs e)
    {
        _isDragging = false;
    }

    private void UpdateBatteryStatus()
    {
        if (!_isVisible) return;

        var power = SystemInformation.PowerStatus;
        var percent = (int)(power.BatteryLifePercent * 100);
        _displayText = power.PowerLineStatus == PowerLineStatus.Online
            ? $"{percent}% ⚡"
            : $"{percent}% ({power.BatteryLifeRemaining / 60 / 60:0}h {power.BatteryLifeRemaining / 60 % 60:00}m)";

        // Update text color based on battery percentage
        if (percent > 65)
        {
            _textBrush.Color = HealthyColor;
            _flashTimer.Enabled = false;
        }
        else if (percent > 32)
        {
            _textBrush.Color = WarningColor;
            _flashTimer.Enabled = false;
        }
        else if (percent > 5)
        {
            _textBrush.Color = CriticalColor;
            _flashTimer.Enabled = false;
        }
        else // 5% or less
        {
            _textBrush.Color = CriticalColor;
            _flashTimer.Enabled = true;
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!_isVisible) return;

        var size = e.Graphics.MeasureString(_displayText, _font);
        
        e.Graphics.FillRectangle(_bgBrush, 0, 0, size.Width + 10, size.Height + 6);
        
        // Only draw text if not flashing or in visible flash state
        if (!_flashTimer.Enabled || _shouldFlash)
        {
            e.Graphics.DrawString(_displayText, _font, _textBrush, 5, 3);
        }

        Size = new Size((int)size.Width + 10, (int)size.Height + 6);
    }

    private void ForceToTop()
    {
        if (!TopMost) TopMost = true;
        SetWindowPos(Handle, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize);
    }

    public void ToggleVisibility()
    {
        _isVisible = !_isVisible;
        Invalidate();
    }

    private void UpdateTransparency()
    {
        // Invert the transparency value for opacity calculation
        // slider at 100 (right) = 0% transparency = 1.0 opacity
        // slider at 0 (left) = 100% transparency = 0.1 opacity
        var invertedTransparency = 100 - _transparency;
        Opacity = Math.Max(0.1, 1.0 - (invertedTransparency * 0.009));
    }

    private void ShowSettings()
    {
        if (_settingsForm?.IsDisposed == false)
        {
            _settingsForm.Focus();
            return;
        }

        _settingsForm = new Form
        {
            Text = "Battery Monitor Settings",
            Size = new Size(300, 150),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterScreen
        };

        var label = new Label
        {
            Text = "Transparency:",
            Location = new Point(20, 20),
            AutoSize = true
        };

        var slider = new TrackBar
        {
            Location = new Point(20, 50),
            Width = 240,
            Minimum = 0,    // Left = fully transparent
            Maximum = 100,  // Right = fully opaque
            Value = _transparency,  // Starts at 100 (right side)
            TickFrequency = 10,
            TickStyle = TickStyle.BottomRight
        };

        var valueLabel = new Label
        {
            Text = $"{100 - _transparency}%",  // Show actual transparency percentage
            Location = new Point(slider.Right + 10, 50),
            AutoSize = true
        };

        slider.ValueChanged += (_, _) =>
        {
            _transparency = slider.Value;
            valueLabel.Text = $"{100 - _transparency}%";  // Show actual transparency percentage
            UpdateTransparency();
        };

        _settingsForm.Controls.AddRange(new Control[] { label, slider, valueLabel });
        _settingsForm.Show(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        UpdateTransparency(); // Set initial transparency
    }
}