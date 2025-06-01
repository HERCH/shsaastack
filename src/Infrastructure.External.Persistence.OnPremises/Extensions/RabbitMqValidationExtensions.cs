using Common.Extensions; // Para Format (si lo usas para mensajes de error)
using Infrastructure.External.Persistence.OnPremises.Constants; // Para RabbitMqConstants
using System;
using System.Text.RegularExpressions;

namespace Infrastructure.External.Persistence.OnPremises.Extensions;

public static class RabbitMqValidationExtensions
{
    // Reutiliza tu Resources.resx para los mensajes de error, creando nuevas entradas.
    // Ejemplo de clave de recurso: ValidationExtensions_InvalidRabbitMqName
    // private static readonly string InvalidNameResourceString = Resources.ValidationExtensions_InvalidRabbitMqName;

    private static void ValidateRabbitMqName(string name, string expression, int maxLength, string entityTypeForErrorMessage)
    {
        // Un nombre vacío puede ser válido para el exchange por defecto, pero no para exchanges/colas nombradas.
        if (string.IsNullOrWhiteSpace(name))
        {
            // string errorMessage = string.Format("RabbitMQ {0} name cannot be empty or whitespace.", entityTypeForErrorMessage);
            // Reemplazar con: Resources.YourResource_RabbitMqNameCannotBeEmpty.Format(entityTypeForErrorMessage);
            throw new ArgumentException($"RabbitMQ {entityTypeForErrorMessage} name cannot be empty or whitespace.", nameof(name));
        }

        if (name.Length > maxLength || !Regex.IsMatch(name, expression))
        {
            // string errorMessage = string.Format(InvalidNameResourceString ?? "The RabbitMQ {0} name: '{{0}}' contains illegal characters or exceeds max length ({1}). Expression: {2}", name, maxLength, expression);
            // Reemplazar con: Resources.ValidationExtensions_InvalidRabbitMqName.Format(entityTypeForErrorMessage, name, maxLength, expression);
            throw new ArgumentOutOfRangeException(nameof(name), $"The RabbitMQ {entityTypeForErrorMessage} name: '{name}' contains illegal characters or exceeds max length ({maxLength}). Allowed pattern: {expression}");
        }
    }

    public static string SanitizeAndValidateExchangeName(this string name)
    {
        // Para RabbitMQ, la sanitización común es no cambiar el case, ya que es case-sensitive.
        // Si tu convención es usar lowercase, aplícalo aquí.
        // string sanitizedName = name.ToLowerInvariant();
        string sanitizedName = name; // Mantener case por defecto
        ValidateRabbitMqName(sanitizedName, RabbitMqConstants.EntityNameValidationExpression, RabbitMqConstants.MaxEntityNameLength, "exchange");
        return sanitizedName;
    }

    public static string SanitizeAndValidateQueueName(this string name)
    {
        // string sanitizedName = name.ToLowerInvariant();
        string sanitizedName = name; // Mantener case por defecto
        ValidateRabbitMqName(sanitizedName, RabbitMqConstants.EntityNameValidationExpression, RabbitMqConstants.MaxEntityNameLength, "queue");
        return sanitizedName;
    }

    public static string SanitizeAndValidateRoutingKey(this string name, bool allowWildcards = false)
    {
        if (string.IsNullOrEmpty(name)) // Una routing key vacía es válida para el default exchange o fanout bindings
        {
            return "";
        }
        // string sanitizedName = name.ToLowerInvariant(); // Routing keys también son case-sensitive
        string sanitizedName = name;
        var expression = allowWildcards
            ? RabbitMqConstants.RoutingKeyBindValidationExpression
            : RabbitMqConstants.RoutingKeyPublishValidationExpression;
        ValidateRabbitMqName(sanitizedName, expression, RabbitMqConstants.MaxRoutingKeyLength, "routing key");
        return sanitizedName;
    }
}