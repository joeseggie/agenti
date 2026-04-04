using EastSeat.Agenti.Shared.Domain.Entities;
using EastSeat.Agenti.Shared.Domain.Enums;

namespace EastSeat.Agenti.UnitTests.Helpers.TestDataBuilders;

/// <summary>
/// Fluent builder for creating Notification test data.
/// </summary>
public class NotificationBuilder
{
    private Guid _id = Guid.NewGuid();
    private string _recipientUserId = "recipient-user-id";
    private string? _senderUserId = "sender-user-id";
    private string _message = "Test notification message";
    private NotificationPriority _priority = NotificationPriority.Normal;
    private bool _isRead = false;
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? _readAt = null;
    private Guid? _transactionId = null;

    public NotificationBuilder WithId(Guid id)
    {
        _id = id;
        return this;
    }

    public NotificationBuilder WithRecipientUserId(string recipientUserId)
    {
        _recipientUserId = recipientUserId;
        return this;
    }

    public NotificationBuilder WithSenderUserId(string? senderUserId)
    {
        _senderUserId = senderUserId;
        return this;
    }

    public NotificationBuilder AsSystemNotification()
    {
        _senderUserId = null;
        return this;
    }

    public NotificationBuilder WithMessage(string message)
    {
        _message = message;
        return this;
    }

    public NotificationBuilder WithPriority(NotificationPriority priority)
    {
        _priority = priority;
        return this;
    }

    public NotificationBuilder AsRead()
    {
        _isRead = true;
        _readAt = DateTimeOffset.UtcNow;
        return this;
    }

    public NotificationBuilder WithCreatedAt(DateTimeOffset createdAt)
    {
        _createdAt = createdAt;
        return this;
    }

    public NotificationBuilder WithTransactionId(Guid transactionId)
    {
        _transactionId = transactionId;
        return this;
    }

    public Notification Build()
    {
        return new Notification
        {
            Id = _id,
            RecipientUserId = _recipientUserId,
            SenderUserId = _senderUserId,
            Message = _message,
            Priority = _priority,
            IsRead = _isRead,
            CreatedAt = _createdAt,
            ReadAt = _readAt,
            TransactionId = _transactionId
        };
    }

    public static NotificationBuilder Default() => new();
}
