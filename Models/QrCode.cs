namespace Panwar.Api.Models;

public class QrCode
{
    public Guid Id { get; set; }
    public required string Url { get; set; }
    public int Size { get; set; }
    public required string Foreground { get; set; }
    public required string Background { get; set; }
    public bool HasLogo { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public AppUser Creator { get; set; } = null!;
}
