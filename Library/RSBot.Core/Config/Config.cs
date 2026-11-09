using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RSBot.Core;

public class Config
{
    /// <summary>
    ///     The object that stores the configuration
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _config;

    /// <summary>
    ///     Gets the path.
    /// </summary>
    private readonly string _path;
    private readonly object _saveLock = new();

    /// <summary>
    ///     Loads the specified file.
    /// </summary>
    /// <param name="file">The file.</param>
    public Config(string file)
    {
        _path = file;

        CheckPath();

        _config = new ConcurrentDictionary<string, string>();
        var lineNumber = 0;
        foreach (var line in File.ReadAllLines(_path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var openingBrace = line.IndexOf('{');
            var closingBrace = openingBrace >= 0 ? line.IndexOf('}', openingBrace + 1) : -1;
            if (openingBrace <= 0 || closingBrace < openingBrace)
            {
                Log.Warn($"Ignoring malformed configuration line {lineNumber} in '{_path}'.");
                continue;
            }

            var key = line[..openingBrace];
            var value = line[(openingBrace + 1)..closingBrace];
            if (string.IsNullOrWhiteSpace(key))
            {
                Log.Warn($"Ignoring configuration line {lineNumber} with an empty key in '{_path}'.");
                continue;
            }

            _config.TryAdd(key, value);
        }
    }

    /// <summary>
    ///     gets is loaded
    /// </summary>
    private bool _isLoaded => _config != null;

    /// <summary>
    ///     Existses the specified key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns></returns>
    public bool Exists(string key)
    {
        if (!_isLoaded)
            return false;

        return _config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    ///     Gets the specified key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="defaultValue">The default value.</param>
    public T Get<T>(string key, T defaultValue = default)
    {
        if (!_isLoaded)
            return (T)Convert.ChangeType(false, typeof(T));

        if (!_config.TryGetValue(key, out var value))
        {
            Set(key, defaultValue);
            return defaultValue;
        }

        if (string.IsNullOrEmpty(value))
            return defaultValue;

        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch (Exception error) when (
            error is FormatException
            || error is InvalidCastException
            || error is OverflowException
            || error is ArgumentException
        )
        {
            Log.Warn($"Invalid value for configuration key '{key}' in '{_path}'. Using the configured default. {error.Message}");
            return defaultValue;
        }
    }

    /// <summary>
    ///     Gets the specified key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="defaultValue">The default value.</param>
    public TEnum GetEnum<TEnum>(string key, TEnum defaultValue)
        where TEnum : struct
    {
        if (!_isLoaded)
            return default;

        if (!_config.TryGetValue(key, out var value))
        {
            Set(key, defaultValue);
            value = defaultValue.ToString();
        }

        TEnum result;
        if (!Enum.TryParse(value, out result))
            return default;

        return result;
    }

    /// <summary>
    ///     Sets the specified key inside the config.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    public void Set<T>(string key, T value)
    {
        var setValue = value == null ? string.Empty : value.ToString();
        _config.AddOrUpdate(key, setValue, (k, v) => setValue);
    }

    /// <summary>
    ///     Check directories
    /// </summary>
    private void CheckPath()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        if (!File.Exists(_path))
            File.Create(_path).Dispose();
    }

    /// <summary>
    ///     Saves the specified file.
    /// </summary>
    /// <param name="file">The file.</param>
    public void Save()
    {
        if (!_isLoaded || string.IsNullOrWhiteSpace(_path))
            return;

        lock (_saveLock)
        {
            CheckPath();

            // Snapshot the concurrent dictionary once. Never truncate the live settings file:
            // write to a sibling file, flush it, then replace the original in one rename.
            var serializedConfig = _config
                .ToArray()
                .OrderBy(element => element.Key, StringComparer.Ordinal)
                .Select(element => element.Key + "{" + element.Value + "}")
                .ToArray();

            var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough
                ))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    foreach (var line in serializedConfig)
                        writer.WriteLine(line);

                    writer.Flush();
                    stream.Flush(true);
                }

                File.Move(temporaryPath, _path, true);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch (Exception error)
                {
                    Log.Debug($"Could not remove temporary settings file '{temporaryPath}': {error.Message}");
                }
            }
        }
    }

    /// <summary>
    ///     Sets the array.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="values">The values.</param>
    /// <param name="delimiter">The delimiter.</param>
    public void SetArray<T>(string key, IEnumerable<T> values, string delimiter = ",")
    {
        if (values == null)
            return;

        Set(key, string.Join(delimiter, values));
    }

    /// <summary>
    ///     Gets the array.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="delimiter">The delimiter.</param>
    /// <returns></returns>
    public T[] GetArray<T>(
        string key,
        char delimiter = ',',
        StringSplitOptions options = StringSplitOptions.RemoveEmptyEntries
    )
    {
        if (!_isLoaded)
            return new T[] { };

        var data = Get<string>(key)?.Split(new[] { delimiter }, options);
        if (data == null || data.Length == 0)
            return new T[] { };

        return data?.Select(p => (T)Convert.ChangeType(p, typeof(T))).ToArray();
    }

    /// <summary>
    ///     Get array the specified key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="defaultValue">The default value.</param>
    public TEnum[] GetEnums<TEnum>(string key, char delimiter = ',')
        where TEnum : struct
    {
        if (!_isLoaded)
            return new TEnum[] { };

        var data = Get<string>(key)?.Split(new[] { delimiter }, StringSplitOptions.RemoveEmptyEntries);
        if (data == null || data.Length == 0)
            return new TEnum[] { };

        return data?.Select(p => Enum.Parse<TEnum>(p)).ToArray();
    }
}
