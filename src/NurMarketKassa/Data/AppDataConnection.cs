using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.PostgreSQL;
using NurMarketKassa.Configuration;
using NurMarketKassa.Models;

namespace NurMarketKassa.Data;

public sealed class AppDataConnection : DataConnection
{
    public AppDataConnection(PostgreSqlSettings settings)
        : base(
            PostgreSQLTools.GetDataProvider(PostgreSQLVersion.v15),
            settings.ConnectionString)
    {
        CommandTimeout = settings.CommandTimeoutSeconds;
    }

    public ITable<Product> Products => this.GetTable<Product>();
}
