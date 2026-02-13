using System.Text.Json.Serialization;

namespace TextToSqlAgent.Core.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DatabaseProvider
{
    SqlServer,
    PostgreSQL,
    MySQL,
    SQLite
}
