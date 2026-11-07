using System;
using System.Drawing;
using System.IO;
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

    private readonly TextBox _loginUsername = new() { Dock = DockStyle.Fill };
    private readonly TextBox _loginPassword = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _secondaryPassword = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _serverName = new() { Dock = DockStyle.Fill };
    private readonly TextBox _channel = new() { Dock = DockStyle.Fill };

    private readonly BotEntry _original;

    public bool HasLoginDetails =>
        !string.IsNullOrWhiteSpace(_loginUsername.Text)
        || !string.IsNullOrWhiteSpace(_loginPassword.Text)
        || !string.IsNullOrWhiteSpace(_secondaryPassword.Text)
        || !string.IsNullOrWhiteSpace(_serverName.Text);

    public LoginCredentials Credentials => new()
    {
        Username = _loginUsername.Text.Trim(),
        Password = _loginPassword.Text,
        SecondaryPassword = _secondaryPassword.Text,
        Servername = _serverName.Text.Trim(),
        Channel = string.IsNullOrWhiteSpace(_channel.Text) ? null : byte.Parse(_channel.Text.Trim()),
        Character = _character.Text.Trim()
    };

    public AccountDialog(BotEntry original = null)
    {
        _original = original;
        Text = original == null ? "Add bot instance" : "Edit bot instance";
        Width = 650;
        Height = 620;
        MinimumSize = new Size(600, 580);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        _mode.Items.AddRange(new object[] { "Launch Client", "Clientless" });

        if (original != null)
        {
            _name.Text = original.Name;
            _executable.Text = original.ExecutablePath;
            _profile.Text = original.Profile;
            _character.Text = original.Character;
            _mode.SelectedItem = string.Equals(original.LaunchMode, "Clientless", StringComparison.OrdinalIgnoreCase)
                ? "Clientless"
                : "Launch Client";
        }
        else
        {
            _name.Text = "Bot instance";
            _profile.Text = "Default";
            _mode.SelectedItem = "Launch Client";
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
            LaunchMode = _mode.SelectedItem?.ToString() ?? "Launch Client",
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
            RowCount = 12
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (var i = 0; i < 10; i++)
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddRow(root, 0, "Instance name", _name);
        AddRow(root, 1, "OasisBot.exe", BuildExecutablePicker());
        AddRow(root, 2, "Profile", _profile);
        AddRow(root, 3, "Character", _character);
        AddRow(root, 4, "Launch mode", _mode);
        AddRow(root, 5, "Login email / username", _loginUsername);
        AddRow(root, 6, "Login password", _loginPassword);
        AddRow(root, 7, "Secondary password", _secondaryPassword);
        AddRow(root, 8, "Server name", _serverName);
        AddRow(root, 9, "Channel (optional)", _channel);

        var hint = new Label
        {
            Text = "Login details are saved inside this profile's User\\<Profile>\\autologin.data file. Existing accounts are kept. Leave the password blank when updating an account to keep its saved password. New accounts require a password.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText,
            AutoEllipsis = true,
            Padding = new Padding(3, 4, 3, 4)
        };
        root.Controls.Add(hint, 0, 10);
        root.SetColumnSpan(hint, 2);

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
        root.Controls.Add(actions, 0, 11);
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

        if (profile.Equals("Profiles", StringComparison.OrdinalIgnoreCase) || profile.Equals("Settings", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Profiles and Settings are reserved by OasisBot. Choose another profile name.", "Reserved profile name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _profile.Focus();
            return;
        }

        if (profile.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || profile.Contains('/') || profile.Contains('\\'))
        {
            MessageBox.Show(this, "The profile name contains characters that cannot be used in a profile folder.", "Invalid profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _profile.Focus();
            return;
        }

        var character = _character.Text.Trim();
        if (character.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || character.Contains('/') || character.Contains('\\'))
        {
            MessageBox.Show(this, "The character name contains characters that cannot be used in a character settings file.", "Invalid character", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _character.Focus();
            return;
        }

        var username = _loginUsername.Text.Trim();
        var password = _loginPassword.Text;
        if (!string.IsNullOrWhiteSpace(password) && string.IsNullOrWhiteSpace(username))
        {
            MessageBox.Show(this, "Enter the login email or username for this password.", "Missing login", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _loginUsername.Focus();
            return;
        }

        if (!string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password))
        {
            try
            {
                if (!ProfileAccountStore.AccountExists(path, profile, username))
                {
                    MessageBox.Show(this, "Enter a password for this new account. Leave it blank only when updating an account already saved in this profile.", "Missing password", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _loginPassword.Focus();
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not read the saved accounts for this profile.\n\n{ex.Message}", "Account file error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(_channel.Text)
            && (!byte.TryParse(_channel.Text.Trim(), out var channel) || channel < 1))
        {
            MessageBox.Show(this, "Channel must be a number from 1 to 255, or left blank to keep the saved value (new accounts default to 1).", "Invalid channel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _channel.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
