using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace OasisBot.Manager;

internal sealed class BotEntry : INotifyPropertyChanged
{
    private string _status = "Stopped";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Bot";
    public string ExecutablePath { get; set; } = "";
    public string Profile { get; set; } = "Default";
    public string Character { get; set; } = "";
    public string LaunchMode { get; set; } = "Normal";

    // Stored so the manager can reconnect to a bot after the manager itself is closed.
    public int LastProcessId { get; set; }
    public DateTime? LastProcessStartUtc { get; set; }
    public int? LastExitCode { get; set; }
    public string LastLogPath { get; set; } = "";

    [JsonIgnore]
    public Process AttachedProcess { get; set; }

    [JsonIgnore]
    public string Status
    {
        get => _status;
        set
        {
            if (_status == value)
                return;

            _status = value;
            OnPropertyChanged(nameof(Status));
        }
    }

    [JsonIgnore]
    public string ProcessIdDisplay => LastProcessId > 0 ? LastProcessId.ToString() : "—";

    [JsonIgnore]
    public string ExitCodeDisplay => LastExitCode.HasValue ? LastExitCode.Value.ToString() : "—";

    [JsonIgnore]
    public string UptimeDisplay
    {
        get
        {
            if (LastProcessStartUtc == null || AttachedProcess == null)
                return "—";

            try
            {
                var elapsed = DateTime.UtcNow - LastProcessStartUtc.Value;
                if (elapsed < TimeSpan.Zero)
                    elapsed = TimeSpan.Zero;

                return elapsed.TotalDays >= 1
                    ? $"{(int)elapsed.TotalDays}d {elapsed.Hours:D2}:{elapsed.Minutes:D2}"
                    : $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            }
            catch
            {
                return "—";
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(ProcessIdDisplay));
        OnPropertyChanged(nameof(UptimeDisplay));
        OnPropertyChanged(nameof(Status));
    }

    public void MarkExited()
    {
        try
        {
            if (AttachedProcess != null && AttachedProcess.HasExited)
                LastExitCode = AttachedProcess.ExitCode;
        }
        catch
        {
            LastExitCode = null;
        }

        try
        {
            AttachedProcess?.Dispose();
        }
        catch
        {
            // The process may already have exited.
        }

        AttachedProcess = null;
        Status = LastExitCode.HasValue ? $"Exited ({LastExitCode.Value})" : "Exited";
        RefreshDisplay();
    }

    public void ClearProcess()
    {
        try
        {
            AttachedProcess?.Dispose();
        }
        catch
        {
            // The process may already have exited.
        }

        AttachedProcess = null;
        LastProcessId = 0;
        LastProcessStartUtc = null;
        Status = "Stopped";
        RefreshDisplay();
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
