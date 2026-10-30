using FashionStore.Backend.Middleware;
using Npgsql;

namespace FashionStore.Backend.Data;

public sealed class DatabaseMode(IConfiguration configuration)
{
    public bool OrdersEnabled => CustomerFeaturesEnabled && configuration.GetValue<bool>("Database:EnableSupabaseOrders");
    public bool BankTransferEnabled => OrdersEnabled && configuration.GetValue<bool>("Orders:EnableBankTransfer");
    public string WebOrderEmployeeId => configuration["Orders:EmployeeId"]?.Trim() ?? "";
    public bool CustomerFeaturesEnabled => configuration.GetValue<bool>("Database:EnableSupabaseCustomerFeatures");
    public bool IsSupabase => configuration["Database:Provider"]?.ToLowerInvariant() switch
    {
        null or "" or "sqlserver" => false,
        "supabase" => true,
        _ => throw new CatalogConfigurationException("Database:Provider must be SqlServer or Supabase.")
    };

    public string SupabaseConnection
    {
        get
        {
            var raw=configuration.GetConnectionString("Supabase") is { Length: > 0 } connection ? connection :
                configuration["YODYSTORE_CONNECTION_STRING"] is { Length: > 0 } partnerConnection ? partnerConnection :
                throw new CatalogConfigurationException("Configure ConnectionStrings:Supabase or YODYSTORE_CONNECTION_STRING.");
            // Shared timestamp-without-time-zone fields contain UTC wall times.
            // Keep LOCALTIMESTAMP/defaults consistent even if the database session default changes.
            try { return new NpgsqlConnectionStringBuilder(raw){Timezone="UTC"}.ConnectionString; }
            catch(ArgumentException) { throw new CatalogConfigurationException("Invalid Supabase connection configuration."); }
        }
    }
}
