namespace Stp.Contracts;

public sealed record DeveloperAuthStatusResponse(
    bool SetupRequired,
    bool Configured,
    string Source);

public sealed record DeveloperSetupRequest(
    string? Password,
    string? ConfirmPassword);

public sealed record DeveloperLoginRequest(string? Password);

public sealed record DeveloperSessionResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    string Username);

public sealed record DeveloperMeResponse(
    string Username,
    string Role,
    DateTimeOffset? ExpiresAt);
