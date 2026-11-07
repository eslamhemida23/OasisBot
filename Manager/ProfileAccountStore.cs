using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using RSBot.Core.Network.Protocol;

namespace OasisBot.Manager;

internal sealed class LoginCredentials
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string SecondaryPassword { get; set; } = "";
    public string Servername { get; set; } = "";
    public byte? Channel { get; set; }
    public string Character { get; set; } = "";
}

internal static class ProfileAccountStore
{
    public static bool AccountExists(string executablePath, string profile, string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return false;

        var path = GetAccountsFilePath(executablePath, profile);
        return ReadAccounts(path).Any(account =>
            string.Equals(account.Username, username.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static void Upsert(string executablePath, string profile, LoginCredentials credentials)
    {
        if (credentials == null)
            throw new ArgumentNullException(nameof(credentials));

        var username = credentials.Username?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("Enter the login email or username.");

        var path = GetAccountsFilePath(executablePath, profile);
        var accounts = ReadAccounts(path);
        var account = accounts.FirstOrDefault(item =>
            string.Equals(item.Username, username, StringComparison.OrdinalIgnoreCase));

        if (account == null)
        {
            if (string.IsNullOrWhiteSpace(credentials.Password))
                throw new InvalidOperationException("A password is required when adding a new account.");

            account = new StoredGameAccount
            {
                Username = username,
                Password = credentials.Password,
                SecondaryPassword = credentials.SecondaryPassword ?? "",
                Channel = credentials.Channel ?? 1,
                Servername = credentials.Servername?.Trim() ?? "",
                SelectedCharacter = "",
                Characters = new List<string>()
            };
            accounts.Add(account);
        }
        else
        {
            account.Username = username;

            // An empty password means keep the already saved password for this account.
            if (!string.IsNullOrWhiteSpace(credentials.Password))
                account.Password = credentials.Password;

            if (!string.IsNullOrWhiteSpace(credentials.SecondaryPassword))
                account.SecondaryPassword = credentials.SecondaryPassword;

            if (!string.IsNullOrWhiteSpace(credentials.Servername))
                account.Servername = credentials.Servername.Trim();

            if (credentials.Channel.HasValue)
                account.Channel = credentials.Channel.Value;
        }

        account.Characters ??= new List<string>();
        if (!string.IsNullOrWhiteSpace(credentials.Character))
        {
            var character = credentials.Character.Trim();
            account.SelectedCharacter = character;

            if (!account.Characters.Contains(character, StringComparer.OrdinalIgnoreCase))
                account.Characters.Add(character);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Keep the exact file format used by RSBot.General's existing Accounts store.
        // The linked Blowfish implementation intentionally matches the bot's current codec.
        var serialized = JsonSerializer.Serialize(accounts);
        var bytes = Encoding.UTF8.GetBytes(serialized);
        var encoded = new Blowfish().Encode(bytes);
        File.WriteAllBytes(path, encoded);
    }

    private static string GetAccountsFilePath(string executablePath, string profile)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("The OasisBot executable path is required.", nameof(executablePath));

        if (string.IsNullOrWhiteSpace(profile))
            throw new ArgumentException("The profile name is required.", nameof(profile));

        var botDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        if (string.IsNullOrWhiteSpace(botDirectory))
            throw new InvalidOperationException("Could not resolve the OasisBot installation directory.");

        return Path.Combine(botDirectory, "User", profile, "autologin.data");
    }

    private static List<StoredGameAccount> ReadAccounts(string path)
    {
        if (!File.Exists(path))
            return new List<StoredGameAccount>();

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0)
            return new List<StoredGameAccount>();

        var decoded = new Blowfish().Decode(bytes);
        if (decoded == null)
            throw new InvalidDataException("The profile's autologin.data file is not in a valid OasisBot format.");

        var json = Encoding.UTF8.GetString(decoded).Trim('\0');
        return JsonSerializer.Deserialize<List<StoredGameAccount>>(json) ?? new List<StoredGameAccount>();
    }

    private sealed class StoredGameAccount
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string SecondaryPassword { get; set; } = "";
        public byte Channel { get; set; } = 1;
        public string Servername { get; set; } = "";
        public string SelectedCharacter { get; set; } = "";
        public List<string> Characters { get; set; } = new();
    }
}
