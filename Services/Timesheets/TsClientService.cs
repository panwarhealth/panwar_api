using Microsoft.EntityFrameworkCore;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services.Timesheets;

public interface ITsClientService
{
    Task<IReadOnlyList<TsClientDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<TsClientDto> CreateAsync(TsClientWriteRequest data, CancellationToken cancellationToken = default);
    Task<TsClientDto?> UpdateAsync(Guid clientId, TsClientWriteRequest data, CancellationToken cancellationToken = default);
}

public class TsClientService : ITsClientService
{
    private const int MaxName = 200;
    private const int MaxPrefix = 10;

    private readonly AppDbContext _context;

    public TsClientService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TsClientDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var clients = await _context.TsClients.AsNoTracking()
            .Where(c => !c.Archived)
            .OrderBy(c => c.Name)
            .Select(c => new { Client = c, Codes = c.Jobs.Select(j => j.Code).ToList() })
            .ToListAsync(cancellationToken);
        return clients.Select(c => ToDto(c.Client, c.Codes)).ToList();
    }

    public async Task<TsClientDto> CreateAsync(TsClientWriteRequest data, CancellationToken cancellationToken = default)
    {
        var client = new TsClient { Id = Guid.NewGuid(), Name = "", CreatedAt = DateTime.UtcNow };
        await ApplyAsync(client, data, cancellationToken);
        _context.TsClients.Add(client);
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(client, []);
    }

    public async Task<TsClientDto?> UpdateAsync(Guid clientId, TsClientWriteRequest data, CancellationToken cancellationToken = default)
    {
        var client = await _context.TsClients.FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);
        if (client is null) return null;
        await ApplyAsync(client, data, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        var codes = await _context.TsJobs.Where(j => j.ClientId == clientId).Select(j => j.Code).ToListAsync(cancellationToken);
        return ToDto(client, codes);
    }

    private async Task ApplyAsync(TsClient client, TsClientWriteRequest data, CancellationToken cancellationToken)
    {
        var name = data.Name.Trim();
        var prefix = data.SuppliesOwnCodes ? null : data.CodePrefix?.Trim().ToUpperInvariant();
        if (name.Length == 0 || name.Length > MaxName) throw new TsValidationException("Client name is required");
        if (!data.SuppliesOwnCodes && (string.IsNullOrEmpty(prefix) || prefix.Length > MaxPrefix || !prefix.All(char.IsLetter)))
            throw new TsValidationException("Job code prefix must be letters only, like RB");
        if (await _context.TsClients.AnyAsync(c => c.Id != client.Id && c.Name.ToLower() == name.ToLower(), cancellationToken))
            throw new TsValidationException($"{name} already exists");

        client.Name = name;
        client.CodePrefix = prefix;
        client.SuppliesOwnCodes = data.SuppliesOwnCodes;
        client.UpdatedAt = DateTime.UtcNow;
    }

    private static TsClientDto ToDto(TsClient client, IReadOnlyCollection<string> codes)
        => new(client.Id, client.Name, client.CodePrefix, client.SuppliesOwnCodes,
            client.SuppliesOwnCodes || string.IsNullOrEmpty(client.CodePrefix) ? null : TsJobCodes.Next(client.CodePrefix, codes));
}
