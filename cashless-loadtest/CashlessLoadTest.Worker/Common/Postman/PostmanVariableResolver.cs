using System.Text.RegularExpressions;

namespace CashlessLoadTest.Worker.Common.Postman;

/// <summary>
/// Resolves Postman {{variable}} placeholders using environment/settings values.
/// </summary>
public class PostmanVariableResolver
{
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex VariablePattern = new(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);

    public PostmanVariableResolver() { }

    public PostmanVariableResolver(PostmanEnvironmentLoader env)
    {
        foreach (var kv in env.Variables)
        {
            _variables[kv.Key] = kv.Value;
        }
    }

    public void SetVariable(string key, string value)
    {
        _variables[key] = value;
    }

    public string GetVariable(string key, string defaultValue = "")
    {
        return _variables.TryGetValue(key, out var value) ? value : defaultValue;
    }

    /// <summary>
    /// Replaces all {{variable}} in the input with their resolved values.
    /// </summary>
    public string Resolve(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        return VariablePattern.Replace(input, match =>
        {
            var varName = match.Groups[1].Value;
            return _variables.TryGetValue(varName, out var value) ? value : match.Value;
        });
    }

    /// <summary>
    /// Resolves all values in a dictionary.
    /// </summary>
    public Dictionary<string, string> ResolveAll(Dictionary<string, string> dict)
    {
        var result = new Dictionary<string, string>();
        foreach (var kv in dict)
        {
            result[kv.Key] = Resolve(kv.Value);
        }
        return result;
    }
}
