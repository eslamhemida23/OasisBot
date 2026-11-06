using System.Windows.Forms;

namespace OasisBot.Manager;

internal sealed class AccountDialog : Form
{
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly TextBox _executable = new() { Dock = DockStyle.Fill };
    private readonly TextBox _profile = new() { Dock = DockStyle.Fill };
    private readonly TextBox _character = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _mode = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList
    };

    private readonly BotEntry _original;

    public AccountDialog(BotEntry original = null)
    {
        _original = original;
        Text = original == null ? "Add bot instance" : "Edit bot instance";
        Width = 620;
        Height = 330;
        MinimumSize = new Size(560, 300);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        _mode.Items.AddRange(new object[] { "Normal", "Launch Client", "Clientless" });

        if (original != null)
        {
            _name.Text = original.Name;
            _executable.Text = original.ExecutablePath;
            _profile.Text = original.Profile;
            _character.Text = original.Character;
            _mode.SelectedItem = original.LaunchMode;
        }
        else
        {
            _name.Text = "Bot instance";
            _profile.Text = "Default";
            _mode.SelectedItem = "Normal";
        }

        BuildLayout();
    }

    public BotEntry CreateEntry()
    {
        return new BotEntry
        {
            Id = _original?.Id ?? Guid.NewGuid().ToString("N"),
            Name = _name.Text.Trim(),
            ExecutablePath = Path.GetFullPath(_executable.Text.Trim()),
            Profile = _profile.Text.Trim(),
            Character = _character.Text.Trim(),
            LaunchMode = _mode.SelectedItem?.ToString() ?? "Normal",
            LastProcessId = _original?.LastProcessId ?? 0,
            LastProcessStartUtc = _original?.LastProcessStartUtc,
            AttachedProcess = _original?.AttachedProcess,
            Status = _original?.Status ?? "Stopped"
        };
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 2,
            RowCount = 6
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++)
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddRow(root, 0, "Instance name", _name);
        AddRow(root, 1, "OasisBot.exe", BuildExecutablePicker());
        AddRow(root, 2, "Profile", _profile);
        AddRow(root, 3, "Character", _character);
        AddRow(root, 4, "Launch mode", _mode);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };

        var save = new Button { Text = "Save", Width = 92, Height = 30 };
        var cancel = new Button { Text = "Cancel", Width = 92, Height = 30, DialogResult = DialogResult.Cancel };
        save.Click += Save_Click;
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        root.Controls.Add(actions, 0, 5);
        root.SetColumnSpan(actions, 2);

        Controls.Add(root);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private Control BuildExecutablePicker()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));

        var browse = new Button { Text = "Browse…", Dock = DockStyle.Fill };
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select OasisBot.exe",
                Filter = "OasisBot executable (OasisBot.exe)|OasisBot.exe|Executable files (*.exe)|*.exe",
                CheckFileExists = true
            };

            if (File.Exists(_executable.Text))
                dialog.InitialDirectory = Path.GetDirectoryName(_executable.Text);

            if (dialog.ShowDialog(this) == DialogResult.OK)
                _executable.Text = dialog.FileName;
        };

        panel.Controls.Add(_executable, 0, 0);
        panel.Controls.Add(browse, 1, 0);
        return panel;
    }

    private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        table.Controls.Add(
            new Label
            {
                Text = label,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                AutoEllipsis = true
            },
            0,
            row
        );

        control.Margin = new Padding(3, 5, 3, 5);
        table.Controls.Add(control, 1, row);
    }

    private void Save_Click(object sender, EventArgs e)
    {
        var name = _name.Text.Trim();
        var path = _executable.Text.Trim();
        var profile = _profile.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Enter a name for this instance.", "Missing name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _name.Focus();
            return;
        }

        if (!File.Exists(path) || !string.Equals(Path.GetFileName(path), "OasisBot.exe", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Select a valid OasisBot.exe file.", "Invalid executable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _executable.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(profile))
        {
            MessageBox.Show(this, "Enter a profile name. Use Default for the default profile.", "Missing profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _profile.Focus();
            return;
        }

        if (profile.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || profile.Contains('/') || profile.Contains('\\'))
        {
            MessageBox.Show(this, "The profile name contains characters that cannot be used in a profile folder.", "Invalid profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _profile.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
