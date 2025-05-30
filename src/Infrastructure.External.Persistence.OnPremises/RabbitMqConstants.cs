// En: Infrastructure.External.Persistence.OnPremises/Constants/RabbitMqConstants.cs
namespace Infrastructure.External.Persistence.OnPremises.Constants; // Ajusta el namespace según tu estructura

public static class RabbitMqConstants
{
    // RabbitMQ names can be up to 255 bytes of UTF-8.
    // For simplicity and broad compatibility, a common convention is used.
    // Allowing letters, numbers, hyphen, underscore, period, colon.
    // RabbitMQ IS case-sensitive.
    // Esta expresión es un ejemplo; ajústala a tus convenciones si son más estrictas.
    public const string EntityNameValidationExpression = @"^[a-zA-Z0-9_.-:/]+$"; // Añadido '/' para compatibilidad con nombres de exchange de ASB
    public const int MaxEntityNameLength = 255;

    // Routing keys can also be complex, often dot-separated.
    // Permite '*' y '#' para binding keys, pero los routing keys de publicación suelen ser más simples.
    public const string RoutingKeyPublishValidationExpression = @"^[a-zA-Z0-9_.-:/]*$";
    public const string RoutingKeyBindValidationExpression = @"^[a-zA-Z0-9_.-:#*/]*$";
    public const int MaxRoutingKeyLength = 255;
}