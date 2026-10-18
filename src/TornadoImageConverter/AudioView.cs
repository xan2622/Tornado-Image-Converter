using System.Windows.Media;

namespace TornadoImageConverter;

/// <summary>
/// Audio player of the Explore tab (FLAC soundtrack). Uses the Windows MediaPlayer (Media Foundation),
/// which decodes FLAC natively since Windows 10.
/// </summary>
internal sealed class AudioView : UserControl
{
    private readonly Button btnPlay = new() { Text = "Play", Width = 80 };
    private readonly Button btnStop = new() { Text = "Stop", Width = 80 };
    private readonly TrackBar position = new() { Dock = DockStyle.Fill, TickStyle = TickStyle.None, Minimum = 0, Maximum = 1000, Enabled = false };
    private readonly Label lblTime = new() { AutoSize = true, Anchor = AnchorStyles.Left, Text = "0:00 / 0:00" };
    private readonly TrackBar volume = new() { Width = 110, TickStyle = TickStyle.None, Minimum = 0, Maximum = 100, Value = 80 };
    private readonly Label lblStatus = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly System.Windows.Forms.Timer ticker = new() { Interval = 200 };

    private MediaPlayer? player;
    private bool playing;
    private bool dragging;

    public AudioView()
    {
        BackColor = SystemColors.Control;

        var transport = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12, 12, 12, 0) };
        transport.Controls.AddRange([btnPlay, btnStop, Ui.Caption("Volume:"), volume]);

        var seek = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12, 4, 12, 0) };
        seek.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        seek.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        seek.Controls.Add(position, 0, 0);
        seek.Controls.Add(lblTime, 1, 0);

        var message = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 0) };
        message.Controls.Add(lblStatus);

        Controls.Add(message);
        Controls.Add(seek);
        Controls.Add(transport);

        btnPlay.Click += (_, _) => TogglePlay();
        btnStop.Click += (_, _) => Stop();
        volume.ValueChanged += (_, _) =>
        {
            if (player is not null)
                player.Volume = volume.Value / 100.0;
        };
        position.MouseDown += (_, _) => dragging = true;
        position.MouseUp += (_, _) =>
        {
            dragging = false;
            if (player is { NaturalDuration.HasTimeSpan: true } p)
                p.Position = TimeSpan.FromSeconds(p.NaturalDuration.TimeSpan.TotalSeconds * position.Value / position.Maximum);
        };
        ticker.Tick += (_, _) => UpdatePosition();
    }

    /// <summary>Prepares playback of a file (without starting it); throws if the file cannot be opened.</summary>
    public void Open(string path)
    {
        Stop();
        Release();

        player = new MediaPlayer { Volume = volume.Value / 100.0 };
        player.MediaOpened += (_, _) =>
        {
            position.Enabled = true;
            lblStatus.Text = "";
            UpdatePosition();
        };
        player.MediaFailed += (_, e) =>
        {
            lblStatus.Text = "Cannot play this file: " + e.ErrorException?.Message;
            btnPlay.Enabled = false;
            position.Enabled = false;
        };
        player.MediaEnded += (_, _) => Stop();

        btnPlay.Enabled = true;
        btnPlay.Text = "Play";
        position.Value = 0;
        position.Enabled = false;
        lblTime.Text = "0:00 / 0:00";
        lblStatus.Text = "Loading...";
        player.Open(new Uri(path));
    }

    /// <summary>Stops playback and releases the file (selection change, close).</summary>
    public void Close()
    {
        Stop();
        Release();
    }

    private void TogglePlay()
    {
        if (player is null)
            return;

        if (playing)
        {
            player.Pause();
            playing = false;
            btnPlay.Text = "Play";
            ticker.Stop();
        }
        else
        {
            player.Play();
            playing = true;
            btnPlay.Text = "Pause";
            ticker.Start();
        }
    }

    private void Stop()
    {
        ticker.Stop();
        playing = false;
        btnPlay.Text = "Play";
        if (player is null)
            return;

        player.Stop();
        position.Value = 0;
        UpdatePosition();
    }

    private void Release()
    {
        player?.Close();
        player = null;
    }

    private void UpdatePosition()
    {
        if (player is not { NaturalDuration.HasTimeSpan: true } p)
            return;

        TimeSpan total = p.NaturalDuration.TimeSpan;
        TimeSpan current = p.Position;
        if (!dragging && total.TotalSeconds > 0)
            position.Value = (int)Math.Clamp(current.TotalSeconds / total.TotalSeconds * position.Maximum, 0, position.Maximum);
        lblTime.Text = $"{Format(current)} / {Format(total)}";
    }

    private static string Format(TimeSpan time) =>
        time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ticker.Dispose();
            Release();
        }
        base.Dispose(disposing);
    }
}
