using Panwar.Api.Shared.Extensions;
namespace Panwar.Api.Services.Timesheets;

public class TsValidationException(string message) : RequestValidationException(message);

public class TsForbiddenException() : Exception("Forbidden");
