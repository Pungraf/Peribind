using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace PeribindLauncher;

internal sealed class LauncherForm : Form
{
    private readonly LauncherEngine _engine;

    private readonly Label _statusLabel;
    private readonly Label _versionsLabel;
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

        var titleLabel = new Label
        {
            Text = "Peribind",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            Location = new Point(20, 16),
            Size = new Size(300, 34)
        };

        _statusLabel = new Label
        {
            Text = "Initializing...",
            Location = new Point(20, 62),
            Size = new Size(520, 24)
        };

        _versionsLabel = new Label
        {
            Text = "Version: -",
            ForeColor = Color.DimGray,
            Location = new Point(20, 88),
            Size = new Size(520, 24)
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(20, 124),
            Size = new Size(520, 22),
            Style = ProgressBarStyle.Marquee
        };

        _downloadInfoLabel = new Label
        {
            Text = string.Empty,
            ForeColor = Color.DimGray,
            Location = new Point(20, 150),
            Size = new Size(520, 20)
        };

        _notesLink = new LinkLabel
        {
            Text = "Patch notes",
            AutoSize = true,
            Location = new Point(20, 176),
            Visible = false
        };
        _notesLink.LinkClicked += OnNotesClicked;

        _playButton = new Button
        {
            Text = "Play",
            Location = new Point(360, 214),
            Size = new Size(90, 30),
            Enabled = false
        };
        _playButton.Click += OnPlayClicked;

        _retryButton = new Button
        {
            Text = "Retry",
            Location = new Point(455, 214),
            Size = new Size(85, 30),
            Enabled = false
        };
        _retryButton.Click += async (_, _) => await RunLauncherFlowAsync();

        Controls.Add(titleLabel);
        Controls.Add(_statusLabel);
        Controls.Add(_versionsLabel);
        Controls.Add(_progressBar);
        Controls.Add(_downloadInfoLabel);
        Controls.Add(_notesLink);
        Controls.Add(_playButton);
        Controls.Add(_retryButton);

        Shown += async (_, _) => await RunLauncherFlowAsync();
        FormClosed += (_, _) => _engine.Dispose();
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
        _statusLabel.ForeColor = Color.Black;
        SetProgressIndeterminate(true);

        try
        {
            var progress = new Progress<LauncherProgress>(UpdateProgress);
            var result = await _engine.CheckAndUpdateAsync(progress, CancellationToken.None);

            _statusLabel.Text = result.StatusMessage;
            _versionsLabel.Text = $"Version local: {result.LocalVersion} | remote: {result.RemoteVersion}";

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
            _versionsLabel.Text = "Launcher cannot continue. Fix issue and retry.";
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
            return $"{downloadedMb:0.0} MB / {totalMb:0.0} MB{etaText}";
        }

        return $"{downloadedMb:0.0} MB downloaded";
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
