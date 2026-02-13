using System.Text.Json;
using Spectre.Console;

namespace TextToSqlAgent.Console.Configuration;

/// <summary>
/// Manages database connection strings including saving and loading from file
/// </summary>
public class ConnectionManager
{
    private const string SavedConnectionsFile = "saved-connections.json";
    private readonly string _filePath;

    public ConnectionManager()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TextToSqlAgent");
        
        Directory.CreateDirectory(appDataDir);
        _filePath = Path.Combine(appDataDir, SavedConnectionsFile);
    }

    public class SavedConnection
    {
        public string Name { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;
        public DateTime LastUsed { get; set; }
        public Core.Enums.DatabaseProvider Provider { get; set; } = Core.Enums.DatabaseProvider.SqlServer;
    }

    public class ConnectionsData
    {
        public List<SavedConnection> Connections { get; set; } = new();
        public string? LastUsedConnectionName { get; set; }
    }

    /// <summary>
    /// Load saved connections from file
    /// </summary>
    public ConnectionsData LoadConnections()
    {
        if (!File.Exists(_filePath))
        {
            return new ConnectionsData
            {
                Connections = GetDefaultConnections()
            };
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<ConnectionsData>(json);
            return data ?? new ConnectionsData { Connections = GetDefaultConnections() };
        }
        catch
        {
            return new ConnectionsData { Connections = GetDefaultConnections() };
        }
    }

    /// <summary>
    /// Save connections to file
    /// </summary>
    public void SaveConnections(ConnectionsData data)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(data, options);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠️  Could not save connections: {ex.Message}[/]");
        }
    }

    /// <summary>
    /// Add or update a connection
    /// </summary>
    public void AddOrUpdateConnection(
        ConnectionsData data, 
        string name, 
        string connectionString, 
        Core.Enums.DatabaseProvider provider = Core.Enums.DatabaseProvider.SqlServer)
    {
        var existing = data.Connections.FirstOrDefault(c => c.Name == name);
        
        if (existing != null)
        {
            existing.ConnectionString = connectionString;
            existing.Provider = provider;
            existing.LastUsed = DateTime.Now;
        }
        else
        {
            data.Connections.Add(new SavedConnection
            {
                Name = name,
                ConnectionString = connectionString,
                Provider = provider,
                LastUsed = DateTime.Now
            });
        }

        data.LastUsedConnectionName = name;
        SaveConnections(data);
    }


    /// <summary>
    /// Get default built-in connections
    /// </summary>
    private List<SavedConnection> GetDefaultConnections()
    {
        // Return empty list - users will build their own connections
        return new List<SavedConnection>();
    }

    /// <summary>
    /// Display masked connection string for security
    /// </summary>
    public static string MaskConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return string.Empty;

        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);

        static string? GetValue(string[] items, params string[] keys)
        {
            foreach (var key in keys)
            {
                var match = items.FirstOrDefault(p =>
                    p.Trim().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    var kv = match.Split('=', 2);
                    if (kv.Length == 2)
                    {
                        return kv[1].Trim();
                    }
                }
            }

            return null;
        }

        // Common patterns across providers
        var serverOrHost = GetValue(parts, "Server", "Host", "Data Source", "DataSource");
        var databaseOrFile = GetValue(parts, "Database", "Initial Catalog", "Filename", "File", "Data Source", "DataSource");

        if (serverOrHost == null && databaseOrFile == null)
        {
            return "ConnectionString=[masked]";
        }

        // For SQLite, emphasize file path
        if (connectionString.Contains("Data Source", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Filename", StringComparison.OrdinalIgnoreCase))
        {
            return $"DataSource={databaseOrFile ?? serverOrHost ?? "???"}";
        }

        return $"Server/Host={serverOrHost ?? "???"}, Database={databaseOrFile ?? "???"}";
    }
}
