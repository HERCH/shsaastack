namespace Common.Configuration;

/// <summary>
///     Defines a provider of simple settings
/// </summary>
public interface ISettings
{
    public bool IsConfigured { get; }

    public bool GetBool(string key, bool? defaultValue = null);

    public double GetNumber(string key, double? defaultValue = null);

    public string GetString(string key, string? defaultValue = null);
    
    /// <summary>
    /// Binds a configuration section identified by the key to the provided instance.
    /// </summary>
    /// <typeparam name="T">The type of the instance to bind to.</typeparam>
    /// <param name="sectionKey">The key of the configuration section.</param>
    /// <param name="instanceToBind">The instance to bind the configuration values to.</param>
    void BindSection<T>(string sectionKey, T instanceToBind) where T : class;
}