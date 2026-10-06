using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Panwar.Api.Services.Edm;

namespace Panwar.Api.Functions.Edm;

// Timer triggers are singletons across instances (blob lease), so two ticks never send the same batch.
public class EdmTimerFunction
{
    private readonly IEdmSendEngine _engine;
    private readonly IEdmSyncService _sync;
    private readonly ILogger<EdmTimerFunction> _logger;

    public EdmTimerFunction(IEdmSendEngine engine, IEdmSyncService sync, ILogger<EdmTimerFunction> logger)
    {
        _engine = engine;
        _sync = sync;
        _logger = logger;
    }

    [Function("EdmSendTick")]
    public Task Send([TimerTrigger("0 * * * * *")] TimerInfo timer, FunctionContext context)
        => _engine.RunAsync(context.CancellationToken);

    [Function("EdmSyncTick")]
    public async Task Sync([TimerTrigger("0 */15 * * * *")] TimerInfo timer, FunctionContext context)
    {
        try
        {
            await _sync.SyncAllAsync(context.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "eDM list sync failed");
        }
    }
}
