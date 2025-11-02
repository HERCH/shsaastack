using System.Text.Json;
using Common;
using Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Hosting.Common.RabbitMQ.Workers.Examples;

/// <summary>
///     Example worker that processes email messages from a queue
/// </summary>
public class EmailQueueWorkerExample : RabbitMQConsumerWorker
{
    private readonly ILogger<EmailQueueWorkerExample> _logger;

    public EmailQueueWorkerExample(
        ILogger<EmailQueueWorkerExample> logger,
        IRecorder recorder,
        RabbitMQSettings settings)
        : base(logger, recorder, settings, "emails")
    {
        _logger = logger;
    }

    protected override async Task<Result<Error>> ProcessMessageAsync(string message,
        CancellationToken cancellationToken)
    {
        try
        {
            // Deserialize email message
            var emailMessage = JsonSerializer.Deserialize<EmailMessage>(message);
            if (emailMessage == null)
            {
                return Error.Validation("Invalid email message format");
            }

            _logger.LogInformation("Sending email to {To} with subject: {Subject}",
                emailMessage.To, emailMessage.Subject);

            // Simulate email sending
            await Task.Delay(100, cancellationToken); // Replace with actual email service

            _logger.LogInformation("Email sent successfully to {To}", emailMessage.To);

            return Result.Ok;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize email message");
            return Error.Validation("Invalid JSON format");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email");
            return ex.ToError(ErrorCode.Unexpected);
        }
    }

    private class EmailMessage
    {
        public string To { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
    }
}

/// <summary>
///     Example worker that processes domain events from a topic subscription
/// </summary>
public class DomainEventsWorkerExample : RabbitMQTopicConsumerWorker
{
    private readonly ILogger<DomainEventsWorkerExample> _logger;

    public DomainEventsWorkerExample(
        ILogger<DomainEventsWorkerExample> logger,
        IRecorder recorder,
        RabbitMQSettings settings)
        : base(logger, recorder, settings, "domain_events", "apihost1-endusers")
    {
        _logger = logger;
    }

    protected override async Task<Result<Error>> ProcessMessageAsync(string message,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Processing domain event: {Message}", message);

            // Deserialize and handle domain event
            // This would typically update read models, trigger workflows, etc.
            await Task.Delay(50, cancellationToken); // Replace with actual processing

            return Result.Ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process domain event");
            return ex.ToError(ErrorCode.Unexpected);
        }
    }
}
