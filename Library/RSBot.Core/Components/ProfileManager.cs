using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;

namespace RSBot.Core.Components;

public class ProfileManager
{
    /// <summary>
    ///     The profile config
    /// </summary>
    private static readonly Config _config;

    /// <summary>
    ///     Get active profiles
    /// </summary>
    private static readonly ObservableCollection<string> _profiles;

    private static string _selectedProfile;

    /// <summary>
    ///     Initialize static ctor
    /// </summary>
    static ProfileManager()
    {
        _config = new Config(GetProfileConfigFileName());
        _profiles = new ObservableCollection<string>(_config.GetArray<string>("RSBot.Profiles", '|'));

        if (!_profiles.Contains("Default"))
            _profiles.Insert(0, "Default");

        // Each OasisBot process must keep its own selected profile in memory.
        // Profiles.rs is shared by installations and must not switch an already
        // running process to another instance's profile.
        _selectedProfile = _config.Get("RSBot.SelectedProfile", "Default");

        _profiles.CollectionChanged += Profiles_CollectionChanged;
    }

    /// <summary>
    ///     Get active profiles
    /// </summary>
    public static string[] Profiles => _profiles.ToArray();

    /// <summary>
    ///     If the selected profile loaded via program args <c>true</c>; otherwise <c>false</c>.
    /// </summary>
    public static bool IsProfileLoadedByArgs { get; set; }

    /// <summary>
    ///     The selected character
    /// </summary>
    public static string SelectedCharacter { get; set; }

    /// <summary>
    ///     The selected profile
    /// </summary>
    public static string SelectedProfile => _selectedProfile;

    /// <summary>
    ///     Show the profile dialog <c>true</c>; otherwise <c>false</c>
    /// </summary>
    public static bool ShowProfileDialog
    {
        get => _config.Get("RSBot.ShowProfileDialog", false);
        set
        {
            _config.Set("RSBot.ShowProfileDialog", value);
            _config.Save();
        }
    }

    /// <summary>
    ///     There have any value in the collection <c>true</c>; otherwise <c>false</c>
    /// </summary>
    public static bool Any()
    {
        return _profiles.Any();
    }

    /// <summary>
    ///     Called after Profiles are changed
    /// </summary>
    private static void Profiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _config.SetArray("RSBot.Profiles", _profiles, "|");
        _config.Save();
    }

    /// <summary>
    ///     Set selected profile
    /// </summary>
    /// <param name="profile">The profile</param>
    public static bool SetSelectedProfile(string profile)
    {
        if (!_profiles.Any(p => p == profile))
            return false;

        _config.Set("RSBot.SelectedProfile", profile);
        _config.Save();
        _selectedProfile = profile;

        return true;
    }

    /// <summary>
    ///     Is profile exists <c>true</c>; otherwise <c>false</c>
    /// </summary>
    /// <param name="profile">The profile</param>
    public static bool ProfileExists(string profile)
    {
        return _profiles.Any(p => p.Equals(profile, StringComparison.InvariantCultureIgnoreCase));
    }

    /// <summary>
    ///     Create new profile
    /// </summary>
    /// <param name="profile">The profile</param>
    /// <param name="useAsBase">Use as base <c>true</c>; otherwise <c>false</c></param>
    /// <returns>Is created <c>true</c>; otherwise <c>false</c></returns>
    public static bool Add(string profile, bool useAsBase = false)
    {
        string[] reservedNames = { "Profiles", "Default", "Settings" };

        if (reservedNames.Any(n => n.Equals(profile, StringComparison.InvariantCultureIgnoreCase)))
            return false;

        if (ProfileExists(profile))
        {
            SetSelectedProfile(profile);
            return true;
        }

        _profiles.Add(profile);

        if (useAsBase)
            CopyOldProfileData(profile);

        var newProfileDirectory = GetProfileDirectory(profile);
        if (!Directory.Exists(newProfileDirectory))
            Directory.CreateDirectory(newProfileDirectory);

        SetSelectedProfile(profile);

        return true;
    }

    /// <summary>
    ///     Ensures a profile exists and, for a new or empty manager profile,
    ///     seeds it from a configured profile so Silkroad client settings are present.
    /// </summary>
    public static bool EnsureProfile(string profile, string preferredBaseProfile = "Default")
    {
        if (string.IsNullOrWhiteSpace(profile))
            return false;

        string[] reservedNames = { "Profiles", "Default", "Settings" };
        if (reservedNames.Any(n => n.Equals(profile, StringComparison.InvariantCultureIgnoreCase)))
            return SetSelectedProfile(profile);

        if (!ProfileExists(profile))
            _profiles.Add(profile);

        var targetDirectory = GetProfileDirectory(profile);
        if (!Directory.Exists(targetDirectory))
            Directory.CreateDirectory(targetDirectory);

        if (!HasSilkroadSettings(profile))
        {
            var candidates = new[] { preferredBaseProfile, _selectedProfile }
                .Concat(_profiles)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.InvariantCultureIgnoreCase);

            var sourceProfile = candidates.FirstOrDefault(p =>
                !p.Equals(profile, StringComparison.InvariantCultureIgnoreCase)
                && HasSilkroadSettings(p)
            );

            if (sourceProfile != null)
                CopyProfileData(sourceProfile, profile);
        }

        return SetSelectedProfile(profile);
    }

    private static bool HasSilkroadSettings(string profile)
    {
        var path = GetProfileFile(profile);
        if (!File.Exists(path))
            return false;

        try
        {
            return File.ReadLines(path).Any(line =>
                line.StartsWith("RSBot.SilkroadDirectory{", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(line.Substring("RSBot.SilkroadDirectory{".Length).TrimEnd('}'))
            );
        }
        catch
        {
            return false;
        }
    }

    private static void CopyProfileData(string sourceProfile, string targetProfile)
    {
        try
        {
            var sourceProfileFile = GetProfileFile(sourceProfile);
            var targetProfileFile = GetProfileFile(targetProfile);
            if (File.Exists(sourceProfileFile))
                File.Copy(sourceProfileFile, targetProfileFile, true);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not copy base profile settings from {sourceProfile} to {targetProfile}: {ex.Message}");
        }

        try
        {
            var sourceAutoLogin = Path.Combine(GetProfileDirectory(sourceProfile), "autologin.data");
            var targetDirectory = GetProfileDirectory(targetProfile);
            if (!Directory.Exists(targetDirectory))
                Directory.CreateDirectory(targetDirectory);

            var targetAutoLogin = Path.Combine(targetDirectory, "autologin.data");
            if (File.Exists(sourceAutoLogin))
                File.Copy(sourceAutoLogin, targetAutoLogin, true);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not copy auto-login data from {sourceProfile} to {targetProfile}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Remove the profile
    /// </summary>
    /// <param name="profile">The profile</param>
    /// <returns>Is removed <c>true</c>; otherwise <c>false</c></returns>
    public static bool Remove(string profile)
    {
        return _profiles.Remove(profile);
    }

    /// <summary>
    ///     Copies the old profile data to the new profile.
    /// </summary>
    /// <param name="profile">Name of the profile.</param>
    private static void CopyOldProfileData(string profile)
    {
        try
        {
            var oldProfileFilePath = GetProfileFile(SelectedProfile);
            var newProfileFilePath = GetProfileFile(profile);
            var oldAutoLoginFile = Path.Combine(GetProfileDirectory(SelectedProfile), "autologin.data");
            var newAutoLoginFile = Path.Combine(GetProfileDirectory(profile), "autologin.data");

            if (File.Exists(oldProfileFilePath))
                File.Copy(oldProfileFilePath, newProfileFilePath);

            if (File.Exists(oldAutoLoginFile))
                File.Copy(oldAutoLoginFile, newAutoLoginFile);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not copy old profile data to the new profile: {ex.Message}");
        }
    }

    /// <summary>
    ///     Get profile config file name
    /// </summary>
    /// <returns></returns>
    public static string GetProfileConfigFileName()
    {
        return Path.Combine(Kernel.BasePath, "User", "Profiles.rs");
    }

    public static string GetProfileFile(string profileName)
    {
        return Path.Combine(Kernel.BasePath, "User", $"{profileName}.rs");
    }

    public static string GetProfileDirectory(string profileName)
    {
        return Path.Combine(Kernel.BasePath, "User", profileName);
    }
}
