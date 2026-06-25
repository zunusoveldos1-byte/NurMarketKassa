using NurMarketKassa.Configuration;
using NurMarketKassa.Core.Contracts;

namespace NurMarketKassa.Services;

public sealed class WpfMySqlConnectionSettings : IMySqlConnectionSettings
{
    private readonly MySqlSettings _settings;

    public WpfMySqlConnectionSettings(MySqlSettings settings) => _settings = settings;

    public bool IsEnabled =>
        _settings.Enabled && !string.IsNullOrWhiteSpace(_settings.ConnectionString);

    public string ConnectionString => _settings.ConnectionString;

    public int CommandTimeoutSeconds => _settings.CommandTimeoutSeconds;
}
