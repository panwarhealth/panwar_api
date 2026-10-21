namespace Panwar.Api.Services.Timesheets;

public class TsValidationException(string message) : Exception(message);

public class TsForbiddenException() : Exception("Forbidden");
