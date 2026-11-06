using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OasisBot.Manager;

internal sealed class MainForm : Form
{
    private readonly BindingList<BotEntry> _entries = new();
    private readonly DataGridView _grid = new();
    private readonly Label _summary = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly string _configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OasisBotManager",
        "instances.json"
    );

    private readonly Button _editButton = new() { Text = "Edit", Width = 82, Height = 32 };
    private readonly Button _removeButton = new() { Text = "Remove", Width = 82, Height = 32 };
    private readonly Button _startButton = new() { Text = "Start", Width = 82, Height = 32 };
    private readonly Button _stopButton = new() { Text = "Stop", Width = 82, Height = 32 };

    public MainForm()
    {
        Text = "OasisBot Manager";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 560);
        Size = new Size(1120, 680);
        Font = new Font("Segoe UI", 9F);
        BuildLayout();
        LoadEntries();

        _grid.DataSource = _entries;
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
                EditSelected();
        };

        _timer.Tick += (_, _) => RefreshProcessStates();
        _timer.Start();

        FormClosing += (_, _) =>
        {
            _timer.Stop();
            SaveEntries();
        };

        RefreshProcessStates();
        UpdateSummary();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = new Panel { Dock = DockStyle.Fill };
        var title = new Label
        {
            Text = "OasisBot Manager",
            Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold),
            Dock = DockStyle.Left,
            AutoSize = true
        };
        _summary.Dock = DockStyle.Right;
        _summary.TextAlign = ContentAlignment.MiddleRight;
        _summary.AutoSize = true;
        heading.Controls.Add(_summary);
        heading.Controls.Add(title);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 5)
        };
        actions.Controls.Add(MakeButton("Add instance", 112, (_, _) => AddEntry()));
        actions.Controls.Add(_editButton);
        actions.Controls.Add(_removeButton);
        actions.Controls.Add(new Label { Width = 14 });
        actions.Controls.Add(_startButton);
        actions.Controls.Add(_stopButton);
        actions.Controls.Add(MakeButton("Start all", 92, async (_, _) => await StartAllAsync()));
        actions.Controls.Add(MakeButton("Stop all", 92, async (_, _) => await StopAllAsync()));

        _editButton.Click += (_, _) => EditSelected();
        _removeButton.Click += (_, _) => RemoveSelected();
        _startButton.Click += async (_, _) => await StartSelectedAsync();
        _stopButton.Click += async (_, _) => await StopSelectedAsync();

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.MultiSelect = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.Columns.Add(TextColumn("Instance", nameof(BotEntry.Name), 1.2F));
        _grid.Columns.Add(TextColumn("Profile", nameof(BotEntry.Profile), 0.9F));
        _grid.Columns.Add(TextColumn("Character", nameof(BotEntry.Character), 1.0F));
        _grid.Columns.Add(TextColumn("Launch mode", nameof(BotEntry.LaunchMode), 1.0F));
        _grid.Columns.Add(TextColumn("Status", nameof(BotEntry.Status), 0.9F));
        _grid.Columns.Add(TextColumn("PID", nameof(BotEntry.ProcessIdDisplay), 0.55F));
        _grid.Columns.Add(TextColumn("Uptime", nameof(BotEntry.UptimeDisplay), 0.8F));

        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(actions, 0, 1);
        root.Controls.Add(_grid, 0, 2);
        Controls.Add(root);

        UpdateButtons();
    }

    private static DataGridViewTextBoxColumn TextColumn(string header, string property, float fill)
    {
        return new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = property,
            FillWeight = fill,
            SortMode = DataGridViewColumnSortMode.Automatic
        };
    }

    private static Button MakeButton(string text, int width, EventHandler click)
    {
        var button = new Button { Text = text, Width = width, Height = 32 };
        button.Click += click;
        return button;
    }

    private BotEntry SelectedEntry()
    {
        return _grid.CurrentRow?.DataBoundItem as BotEntry;
    }

    private void UpdateButtons()
    {
        var selected = SelectedEntry();
        var selectedIsRunning = selected != null && IsRunning(selected);
        _editButton.Enabled = selected != null && !selectedIsRunning;
        _removeButton.Enabled = selected != null && !selectedIsRunning;
        _startButton.Enabled = selected != null && !selectedIsRunning;
        _stopButton.Enabled = selectedIsRunning;
    }

    private void AddEntry()
    {
        using var dialog = new AccountDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _entries.Add(dialog.CreateEntry());
        SaveEntries();
        RefreshProcessStates();
    }

    private void EditSelected()
    {
        var selected = SelectedEntry();
        if (selected == null || IsRunning(selected))
            return;

        using var dialog = new AccountDialog(selected);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var edited = dialog.CreateEntry();
        selected.Name = edited.Name;
        selected.ExecutablePath = edited.ExecutablePath;
        selected.Profile = edited.Profile;
        selected.Character = edited.Character;
        selected.LaunchMode = edited.LaunchMode;
        selected.RefreshDisplay();
        SaveEntries();
    }

    private void RemoveSelected()
    {
        var selected = SelectedEntry();
        if (selected == null || IsRunning(selected))
            return;

        if (MessageBox.Show(
            this,
            $"Remove the instance '{selected.Name}' from the manager?",
            "Remove instance",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question
        ) != DialogResult.Yes)
            return;

        _entries.Remove(selected);
        SaveEntries();
        UpdateSummary();
        UpdateButtons();
    }

    private async Task StartSelectedAsync()
    {
        var selected = SelectedEntry();
        if (selected != null)
            await StartEntryAsync(selected);
    }

    private async Task StartAllAsync()
    {
        foreach (var entry in _entries.ToList())
        {
            if (IsRunning(entry))
                continue;

            await StartEntryAsync(entry);
            await Task.Delay(1200);
        }
    }

    private async Task StopSelectedAsync()
    {
        var selected = SelectedEntry();
        if (selected != null)
            await StopEntryAsync(selected);
    }

    private async Task StopAllAsync()
    {
        foreach (var entry in _entries.ToList())
        {
            if (IsRunning(entry))
                await StopEntryAsync(entry);
        }
    }

    private async Task StartEntryAsync(BotEntry entry)
    {
        if (IsRunning(entry))
            return;

        if (!File.Exists(entry.ExecutablePath))
        {
            MessageBox.Show(this, $"OasisBot.exe was not found:\n{entry.ExecutablePath}", "Executable not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var duplicate = _entries.FirstOrDefault(other =>
            other != entry
            && IsRunning(other)
            && PathsEqual(other.ExecutablePath, entry.ExecutablePath)
            && string.Equals(NormalizeProfile(other.Profile), NormalizeProfile(entry.Profile), StringComparison.OrdinalIgnoreCase)
        );

        if (duplicate != null)
        {
            MessageBox.Show(
                this,
                $"'{duplicate.Name}' is already running with this installation and profile.\nUse a different profile for each running instance to avoid shared configuration files.",
                "Profile already in use",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(entry.ExecutablePath);
            var startInfo = new ProcessStartInfo
            {
                FileName = fullPath,
                WorkingDirectory = Path.GetDirectoryName(fullPath)!,
                UseShellExecute = false
            };

            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add(NormalizeProfile(entry.Profile));

            if (!string.IsNullOrWhiteSpace(entry.Character))
            {
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add(entry.Character.Trim());
            }

            if (entry.LaunchMode == "Launch Client")
                startInfo.ArgumentList.Add("--launch-client");
            else if (entry.LaunchMode == "Clientless")
                startInfo.ArgumentList.Add("--launch-clientless");

            startInfo.ArgumentList.Add("--manager-controlled");

            var process = Process.Start(startInfo);
            if (process == null)
                throw new InvalidOperationException("Windows did not return a process handle.");

            process.EnableRaisingEvents = true;
            entry.AttachedProcess = process;
            entry.LastProcessId = process.Id;
            entry.LastProcessStartUtc = process.StartTime.ToUniversalTime();
            entry.Status = "Running";
            entry.RefreshDisplay();
            SaveEntries();
            UpdateSummary();

            await Task.Delay(250);
            RefreshProcessStates();
        }
        catch (Exception ex)
        {
            entry.ClearProcess();
            SaveEntries();
            MessageBox.Show(this, $"Could not start '{entry.Name}'.\n\n{ex.Message}", "Start failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        UpdateButtons();
    }

    private async Task StopEntryAsync(BotEntry entry)
    {
        var process = entry.AttachedProcess;
        if (process == null || !IsRunning(entry))
        {
            entry.ClearProcess();
            SaveEntries();
            UpdateSummary();
            return;
        }

        try
        {
            process.Refresh();
            var closeSent = process.CloseMainWindow();
            if (closeSent && await WaitForExitAsync(process, TimeSpan.FromSeconds(12)))
            {
                entry.ClearProcess();
                SaveEntries();
                RefreshProcessStates();
                return;
            }

            var answer = MessageBox.Show(
                this,
                $"'{entry.Name}' did not close normally.\n\nForce stop OasisBot and its child game-client process?",
                "Force stop",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (answer == DialogResult.Yes)
            {
                process.Kill(entireProcessTree: true);
                await WaitForExitAsync(process, TimeSpan.FromSeconds(8));
                entry.ClearProcess();
                SaveEntries();
                RefreshProcessStates();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not stop '{entry.Name}'.\n\n{ex.Message}", "Stop failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshProcessStates();
        }

        UpdateButtons();
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        try
        {
            if (process.HasExited)
                return true;

            var waitTask = process.WaitForExitAsync();
            var completed = await Task.WhenAny(waitTask, Task.Delay(timeout));
            if (completed != waitTask)
                return false;

            await waitTask;
            return true;
        }
        catch
        {
            try
            {
                return process.HasExited;
            }
            catch
            {
                return true;
            }
        }
    }

    private void LoadEntries()
    {
        try
        {
            var directory = Path.GetDirectoryName(_configPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory!);

            if (!File.Exists(_configPath))
                return;

            var json = File.ReadAllText(_configPath);
            var saved = JsonSerializer.Deserialize<List<BotEntry>>(json) ?? new List<BotEntry>();
            foreach (var entry in saved)
            {
                if (string.IsNullOrWhiteSpace(entry.Id))
                    entry.Id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(entry.Profile))
                    entry.Profile = "Default";

                _entries.Add(entry);
                TryAttachSavedProcess(entry);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not load the manager settings.\n\n{ex.Message}", "Settings error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void TryAttachSavedProcess(BotEntry entry)
    {
        if (entry.LastProcessId <= 0)
            return;

        Process process = null;
        try
        {
            process = Process.GetProcessById(entry.LastProcessId);
            if (process.HasExited || !PathsEqual(process.MainModule?.FileName, entry.ExecutablePath))
                throw new InvalidOperationException("Saved process no longer matches this instance.");

            if (entry.LastProcessStartUtc.HasValue)
            {
                var actualStart = process.StartTime.ToUniversalTime();
                if (Math.Abs((actualStart - entry.LastProcessStartUtc.Value).TotalSeconds) > 3)
                    throw new InvalidOperationException("The saved process ID has been reused.");
            }

            process.EnableRaisingEvents = true;
            entry.AttachedProcess = process;
            entry.Status = "Running";
            entry.RefreshDisplay();
        }
        catch
        {
            try { process?.Dispose(); } catch { }
            entry.ClearProcess();
        }
    }

    private void RefreshProcessStates()
    {
        foreach (var entry in _entries)
        {
            if (entry.AttachedProcess == null)
            {
                if (entry.LastProcessId > 0)
                    TryAttachSavedProcess(entry);
                continue;
            }

            try
            {
                if (entry.AttachedProcess.HasExited)
                {
                    entry.ClearProcess();
                    SaveEntries();
                }
                else
                {
                    entry.Status = "Running";
                    entry.RefreshDisplay();
                }
            }
            catch
            {
                entry.ClearProcess();
                SaveEntries();
            }
        }

        UpdateSummary();
        UpdateButtons();
    }

    private bool IsRunning(BotEntry entry)
    {
        if (entry.AttachedProcess == null)
            return false;

        try
        {
            return !entry.AttachedProcess.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateSummary()
    {
        var running = _entries.Count(IsRunning);
        _summary.Text = $"{running} running  /  {_entries.Count} configured";
    }

    private void SaveEntries()
    {
        try
        {
            var directory = Path.GetDirectoryName(_configPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory!);

            var json = JsonSerializer.Serialize(_entries.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save manager settings.\n\n{ex.Message}", "Save error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string NormalizeProfile(string profile)
    {
        return string.IsNullOrWhiteSpace(profile) ? "Default" : profile.Trim();
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
