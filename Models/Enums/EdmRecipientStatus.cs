namespace Panwar.Api.Models.Enums;

/// <summary>
/// Pending until the send engine hands the message to Azure, Sent once accepted, then
/// Delivered / Bounced / Failed when the Event Grid delivery report arrives.
/// </summary>
public enum EdmRecipientStatus
{
    Pending = 0,
    Sent = 1,
    Delivered = 2,
    Bounced = 3,
    Failed = 4
}
