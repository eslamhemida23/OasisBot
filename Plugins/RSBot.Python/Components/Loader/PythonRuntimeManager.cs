using System;
using System.IO;
using System.Linq;
using Python.Runtime;
using RSBot.Python.Components.API.Handler;
using RSBot.Python.Components.API.ModuleLoader;

namespace RSBot.Python.Components.Loader
{
    public class PythonRuntimeManager
    {
        public bool IsInitialized { get; private set; }

        public void Initialize(string projectDir, Action<string> log)
        {
            if (IsInitialized)
                return;

            string pythonHome = Path.Combine(projectDir, "Data", "Python", "PyRuntime");
            string pythonDll = Directory.GetFiles(pythonHome, "python31*.dll").FirstOrDefault();
            string zipPath = Directory.GetFiles(pythonHome, "python31*.zip").FirstOrDefault();

            if (pythonDll == null)
            {
                log("[Python-API] Couldn't find any Python-DLL!");
                return;
            }

            Runtime.PythonDLL = pythonDll;

            PythonEngine.PythonHome = pythonHome;
            PythonEngine.PythonPath = $"{pythonHome};{zipPath};{Path.Combine(pythonHome, "Lib")}";

            PythonEngine.Initialize();
            PythonEngine.BeginAllowThreads();

            using (Py.GIL())
            {
                try
                {
                    PythonEngine.Exec(
                        @"
import clr, sys, types
clr.AddReference('RSBot.Python')
from RSBot.Python.Components.API.ModuleLoader import PythonPluginAccessor

RSBot = types.ModuleType('RSBot')
RSBot.__path__ = []
sys.modules['RSBot'] = RSBot
plugins = PythonPluginAccessor.all()

for name, plugin in plugins.items():
    api_module = types.ModuleType(f'RSBot.{name}')
    for attr_name in dir(plugin):
        if attr_name.startswith('_') or attr_name == 'Init':
            continue
        attr = getattr(plugin, attr_name)
        if callable(attr):
            setattr(api_module, attr_name, attr)
            # Keep the legacy flat API while exposing collision-free module namespaces.
            setattr(RSBot, attr_name, attr)
    setattr(RSBot, name, api_module)
    sys.modules[api_module.__name__] = api_module

from RSBot.Python.Components.API.GUI import WFAPI
RSBot.GUI = WFAPI.GUI
if hasattr(RSBot, 'gui'):
    RSBot.gui.GUI = WFAPI.GUI
"
                    );
                    IsInitialized = true;
                    log("[Python-API] Python initialised and created RSBot Module.");
                }
                catch (PythonException ex)
                {
                    var formatted = PythonErrorHandler.FormatException(ex);
                    log(formatted);
                    IsInitialized = false;
                }
            }
        }

        public static void GenerateStub(string path)
        {
            string stubPath = Path.Combine(path, "Data", "Python", "Plugins", "RSBot.pyi");
            PythonStubGenerator.Generate(stubPath);
        }
    }
}
