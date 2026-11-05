using System.IO;
using Python.Runtime;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Python.Components.API.Interface;
using RSBot.Python.Views;

namespace RSBot.Python.Components.API.Core.Entity
{
    public class ConfigAPI : IPythonPlugin
    {
        private Main _main;

        /// <summary>
        /// Unique module name of the plugin.
        /// </summary>
        public string ModuleName => "config";

        /// <summary>
        /// Called once at startup to provide the main form to the plugin.
        /// </summary>
        /// <param name="main">Main form</param>
        public void Init(Main main)
        {
            _main = main;
        }

        private string GetConfigDir()
        {
            return Path.Combine(Kernel.BasePath, "User", ProfileManager.SelectedProfile);
        }

        private string GetConfigPath()
        {
            if (Game.Player == null)
            {
                return GetConfigDir();
            }
            return Path.Combine(GetConfigDir(), $"{Game.Player.Name}.rs");
        }

        private string GetLogDir()
        {
            return Path.Combine(Kernel.BasePath, "User", "Logs");
        }

        private string GetLogPath()
        {
            if (Game.Player == null)
            {
                return GetLogDir();
            }
            return Path.Combine(GetLogDir(), Game.Player.Name);
        }

        public bool exists(string key)
        {
            return !string.IsNullOrWhiteSpace(key) && PlayerConfig.Exists(key);
        }

        public string get_string(string key, string defaultValue = "")
        {
            return PlayerConfig.Get<string>(key, defaultValue);
        }

        public int get_int(string key, int defaultValue = 0)
        {
            return PlayerConfig.Get<int>(key, defaultValue);
        }

        public float get_float(string key, float defaultValue = 0)
        {
            return PlayerConfig.Get<float>(key, defaultValue);
        }

        public bool get_bool(string key, bool defaultValue = false)
        {
            return PlayerConfig.Get<bool>(key, defaultValue);
        }

        public void set_string(string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(key))
                PlayerConfig.Set(key, value ?? string.Empty);
        }

        public void set_int(string key, int value)
        {
            if (!string.IsNullOrWhiteSpace(key))
                PlayerConfig.Set(key, value);
        }

        public void set_float(string key, float value)
        {
            if (!string.IsNullOrWhiteSpace(key))
                PlayerConfig.Set(key, value);
        }

        public void set_bool(string key, bool value)
        {
            if (!string.IsNullOrWhiteSpace(key))
                PlayerConfig.Set(key, value);
        }

        public void set_string_array(string key, PyList values)
        {
            if (string.IsNullOrWhiteSpace(key) || values == null)
                return;

            var items = new System.Collections.Generic.List<string>();
            using (Py.GIL())
            {
                foreach (PyObject value in values)
                {
                    using (value)
                        items.Add(value.ToString());
                }
            }
            PlayerConfig.SetArray(key, items);
        }

        public PyList get_string_array(string key)
        {
            using (Py.GIL())
            {
                var result = new PyList();
                if (string.IsNullOrWhiteSpace(key))
                    return result;

                foreach (var value in PlayerConfig.GetArray<string>(key))
                    result.Append(new PyString(value ?? string.Empty));

                return result;
            }
        }

        public bool save()
        {
            if (Game.Player == null)
                return false;

            PlayerConfig.Save();
            return true;
        }

        public string get_config_dir()
        {
            return GetConfigDir();
        }

        public string get_config_path()
        {
            return GetConfigPath();
        }

        public string get_log_dir()
        {
            return GetLogDir();
        }

        public string get_log_path()
        {
            return GetLogPath();
        }
    }
}
