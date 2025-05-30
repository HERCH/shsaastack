using Common.Configuration;

namespace Infrastructure.Broker.RabbitMq.Extensions;

public static class ConfigurationSettingsExtensions
{

    public static T GetOptions<T>(this ISettings settings, string sectionKey = null) where T : class, new()
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        sectionKey ??= typeof(T).Name.Replace("Options", "", StringComparison.OrdinalIgnoreCase);

        var optionsInstance = new T();
        settings.BindSection(sectionKey, optionsInstance); // Llama a ISettings.BindSection

        return optionsInstance;
    }
}