using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace PeribindLauncher;

internal sealed class LauncherForm : Form
{
    private readonly LauncherEngine _engine;

    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;
    private readonly Label _downloadInfoLabel;
    private readonly Button _playButton;
    private readonly Button _retryButton;
    private readonly LinkLabel _notesLink;

    private bool _isBusy;
    private string _notesUrl = string.Empty;

    public LauncherForm(LauncherEngine engine)
    {
        _engine = engine;

        Text = "Peribind Launcher";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 255);
        BackColor = Color.FromArgb(22, 27, 34);
        BackgroundImage = LoadBitmapResource("Peribind_MainScreen.png");
        BackgroundImageLayout = ImageLayout.Stretch;

        var icon = LoadIconResource("PeribindLogo.ico");
        if (icon != null)
        {
            Icon = icon;
        }

        _statusLabel = new Label
        {
            Text = "Initializing...",
            Location = new Point(20, 100),
            Size = new Size(520, 28),
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            BackColor = Color.Transparent,
            ForeColor = Color.WhiteSmoke
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(20, 132),
            Size = new Size(520, 20),
            Style = ProgressBarStyle.Marquee
        };

        _downloadInfoLabel = new Label
        {
            Text = string.Empty,
            ForeColor = Color.Gainsboro,
            Location = new Point(20, 156),
            Size = new Size(520, 20),
            BackColor = Color.Transparent
        };

        _notesLink = new LinkLabel
        {
            Text = "View patch notes",
            AutoSize = true,
            Location = new Point(20, 210),
            BackColor = Color.Transparent,
            LinkColor = Color.FromArgb(192, 223, 255),
            ActiveLinkColor = Color.FromArgb(242, 248, 255),
            VisitedLinkColor = Color.FromArgb(192, 223, 255),
            LinkBehavior = LinkBehavior.HoverUnderline,
            Visible = false
        };
        _notesLink.LinkClicked += OnNotesClicked;

        _playButton = new Button
        {
            Text = "Play",
            Location = new Point(360, 204),
            Size = new Size(84, 34),
            Enabled = false
        };
        StyleActionButton(_playButton);
        _playButton.Click += OnPlayClicked;

        _retryButton = new Button
        {
            Text = "Retry",
            Location = new Point(456, 204),
            Size = new Size(84, 34),
            Enabled = false
        };
        StyleActionButton(_retryButton);
        _retryButton.Click += async (_, _) => await RunLauncherFlowAsync();

        Controls.Add(_statusLabel);
        Controls.Add(_progressBar);
        Controls.Add(_downloadInfoLabel);
        Controls.Add(_notesLink);
        Controls.Add(_playButton);
        Controls.Add(_retryButton);

        Shown += async (_, _) => await RunLauncherFlowAsync();
        FormClosed += (_, _) => _engine.Dispose();
    }

    private static void StyleActionButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Color.WhiteSmoke;
        button.ForeColor = Color.WhiteSmoke;
        button.BackColor = Color.FromArgb(44, 52, 61);
        button.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
    }

    private static Bitmap? LoadBitmapResource(string fileName)
    {
        var stream = OpenResourceStream(fileName);
        if (stream == null)
        {
            return null;
        }

        using (stream)
        using (var image = Image.FromStream(stream))
        {
            return new Bitmap(image);
        }
    }

    private static Icon? LoadIconResource(string fileName)
    {
        var stream = OpenResourceStream(fileName);
        if (stream == null)
        {
            return null;
        }

        using (stream)
        {
            return new Icon(stream);
        }
    }

    private static Stream? OpenResourceStream(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        return resourceName == null ? null : assembly.GetManifestResourceStream(resourceName);
    }

    private async Task RunLauncherFlowAsync()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        _retryButton.Enabled = false;
        _playButton.Enabled = false;
        _notesLink.Visible = false;
        _downloadInfoLabel.Text = string.Empty;
        _statusLabel.ForeColor = Color.WhiteSmoke;
        SetProgressIndeterminate(true);

        try
        {
            var progress = new Progress<LauncherProgress>(UpdateProgress);
            var result = await _engine.CheckAndUpdateAsync(progress, CancellationToken.None);

            _statusLabel.Text = result.StatusMessage;

            _notesUrl = result.NotesUrl ?? string.Empty;
            _notesLink.Visible = !string.IsNullOrWhiteSpace(_notesUrl);

            SetProgressPercent(100);
            _playButton.Enabled = true;
            _retryButton.Enabled = true;
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = Color.Firebrick;
            _statusLabel.Text = ex.Message;
            _downloadInfoLabel.Text = string.Empty;
            SetProgressIndeterminate(false);

            _playButton.Enabled = _engine.HasInstalledGame();
            _retryButton.Enabled = true;
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void UpdateProgress(LauncherProgress progress)
    {
        _statusLabel.Text = progress.Status;
        _downloadInfoLabel.Text = FormatDownloadInfo(progress);

        if (progress.Percent.HasValue)
        {
            SetProgressPercent(progress.Percent.Value);
        }
        else
        {
            SetProgressIndeterminate(true);
        }
    }

    private static string FormatDownloadInfo(LauncherProgress progress)
    {
        if (!progress.DownloadedBytes.HasValue && !progress.TotalBytes.HasValue)
        {
            return string.Empty;
        }

        var downloaded = progress.DownloadedBytes.GetValueOrDefault();
        var downloadedMb = downloaded / (1024d * 1024d);

        if (progress.TotalBytes.HasValue && progress.TotalBytes.Value > 0)
        {
            var total = progress.TotalBytes.Value;
            var totalMb = total / (1024d * 1024d);
            var etaText = progress.Eta.HasValue ? $" | ETA {FormatEta(progress.Eta.Value)}" : string.Empty;
            return $"Download: {downloadedMb:0.0} MB / {totalMb:0.0} MB{etaText}";
        }

        return $"Downloaded: {downloadedMb:0.0} MB";
    }

    private static string FormatEta(TimeSpan eta)
    {
        if (eta <= TimeSpan.Zero)
        {
            return "00:00";
        }

        if (eta.TotalHours >= 1.0)
        {
            return eta.ToString(@"hh\:mm\:ss");
        }

        return eta.ToString(@"mm\:ss");
    }

    private void SetProgressIndeterminate(bool indeterminate)
    {
        _progressBar.Style = indeterminate ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        if (!indeterminate)
        {
            _progressBar.Value = 0;
        }
    }

    private void SetProgressPercent(int percent)
    {
        var bounded = Math.Clamp(percent, 0, 100);
        if (_progressBar.Style != ProgressBarStyle.Continuous)
        {
            _progressBar.Style = ProgressBarStyle.Continuous;
        }
        _progressBar.Value = bounded;
    }

    private void OnPlayClicked(object? sender, EventArgs e)
    {
        try
        {
            _engine.StartInstalledGame();
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Peribind Launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnNotesClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_notesUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _notesUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Peribind Launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
