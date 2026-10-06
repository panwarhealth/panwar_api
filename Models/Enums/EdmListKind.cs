namespace Panwar.Api.Models.Enums;

/// <summary>
/// Synced lists mirror the opted-in users of one of our platforms and refresh on a timer.
/// Custom lists are loaded from a CSV and only change when someone imports or edits them.
/// </summary>
public enum EdmListKind
{
    Custom = 0,
    Synced = 1
}
