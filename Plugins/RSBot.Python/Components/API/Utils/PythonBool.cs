using Python.Runtime;

namespace RSBot.Python.Components.API.Utils;

internal static class PythonBool
{
    public static PyObject FromBoolean(bool value)
    {
        using var builtins = Py.Import("builtins");
        return builtins.GetAttr(value ? "True" : "False");
    }
}
