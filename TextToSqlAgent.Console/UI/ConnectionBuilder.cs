using Spectre.Console;
using TextToSqlAgent.Core.Enums;
using TextToSqlAgent.Infrastructure.Configuration;

namespace TextToSqlAgent.Console.UI;

/// <summary>
/// Interactive UI for building database connection strings
/// Supports SQL Server, MySQL, PostgreSQL, and SQLite
/// </summary>
public static class ConnectionBuilder
{
    /// <summary>
    /// Prompt user to select database provider and build connection string
    /// </summary>
    public static (string connectionString, string serverName, string databaseName, DatabaseProvider provider) BuildConnectionString()
    {
        AnsiConsole.WriteLine();
        DisplayHeader();
        AnsiConsole.WriteLine();

        // Step 1: Select Database Provider
        var provider = PromptDatabaseProvider();
        if (provider == null)
        {
            AnsiConsole.MarkupLine("[yellow]← Operation cancelled[/]");
            return (string.Empty, string.Empty, string.Empty, DatabaseProvider.SqlServer);
        }

        AnsiConsole.WriteLine();

        // Build connection string based on provider
        return provider.Value switch
        {
            DatabaseProvider.SqlServer => BuildSqlServerConnection(),
            DatabaseProvider.MySQL => BuildMySqlConnection(),
            DatabaseProvider.PostgreSQL => BuildPostgreSqlConnection(),
            DatabaseProvider.SQLite => BuildSQLiteConnection(),
            _ => (string.Empty, string.Empty, string.Empty, DatabaseProvider.SqlServer)
        };
    }

    private static DatabaseProvider? PromptDatabaseProvider()
    {
        AnsiConsole.MarkupLine("[bold cyan]╔══════════════════════════════════════════════╗[/]");
        AnsiConsole.MarkupLine("[bold cyan]║[/]  [bold white]Select Database Provider[/]                [bold cyan]║[/]");
        AnsiConsole.MarkupLine("[bold cyan]╚══════════════════════════════════════════════╝[/]");
        AnsiConsole.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Which database do you want to connect to?[/]")
                .PageSize(6)
                .AddChoices(new[]
                {
                    "🗄️  SQL Server",
                    "🐬 MySQL",
                    "🐘 PostgreSQL",
                    "📦 SQLite",
                    "← Cancel"
                }));

        return choice switch
        {
            "🗄️  SQL Server" => DatabaseProvider.SqlServer,
            "🐬 MySQL" => DatabaseProvider.MySQL,
            "🐘 PostgreSQL" => DatabaseProvider.PostgreSQL,
            "📦 SQLite" => DatabaseProvider.SQLite,
            _ => null
        };
    }

    private static DatabaseProvider? ShowComingSoon(DatabaseProvider provider)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[yellow]⚠️  {provider} support is coming soon![/]");
        AnsiConsole.MarkupLine("[grey]Currently only SQL Server is fully implemented.[/]");
        AnsiConsole.WriteLine();
        
        var retry = AnsiConsole.Confirm("[cyan]Would you like to select a different provider?[/]");
        
        if (retry)
        {
            return PromptDatabaseProvider();
        }
        
        return null;
    }

    private static void DisplayHeader()
    {
        var panel = new Panel("[bold cyan]Database Connection Builder[/]\n[grey]Interactive wizard for creating database connections[/]")
        {
            Border = BoxBorder.Double,
            BorderStyle = new Style(Color.Cyan)
        };
        AnsiConsole.Write(panel);
    }

    #region SQL Server Connection Builder

    private static (string connectionString, string serverName, string databaseName, DatabaseProvider provider) BuildSqlServerConnection()
    {
        DisplaySqlServerHeader();
        AnsiConsole.WriteLine();

        // Step 1: Server Name
        var serverName = PromptServerName();
        if (string.IsNullOrEmpty(serverName))
        {
            AnsiConsole.MarkupLine("[yellow]← Operation cancelled[/]");
            return (string.Empty, string.Empty, string.Empty, DatabaseProvider.SqlServer);
        }

        // Step 2: Database Name
        var databaseName = PromptDatabaseName();
        if (string.IsNullOrEmpty(databaseName))
        {
            return (string.Empty, string.Empty, string.Empty, DatabaseProvider.SqlServer);
        }

        // Step 3: Authentication Method
        var authChoice = PromptAuthenticationMethod();
        if (authChoice == -1)
        {
            return BuildSqlServerConnection(); // Restart
        }

        string userId = string.Empty;
        string password = string.Empty;

        if (authChoice == 1) // SQL Server Authentication
        {
            userId = PromptUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BuildSqlServerConnection();
            }

            password = PromptPassword();
            if (string.IsNullOrEmpty(password))
            {
                return BuildSqlServerConnection();
            }
        }

        // Build connection string
        var connectionString = BuildSqlServerConnectionString(serverName, databaseName, userId, password, authChoice == 1);

        // Test connection
        AnsiConsole.WriteLine();
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("[yellow]Testing connection...[/]", ctx =>
            {
                System.Threading.Thread.Sleep(500); // Brief pause for UX
            });

        DisplayConnectionSummary(serverName, databaseName, userId, authChoice == 1, DatabaseProvider.SqlServer);

        return (connectionString, serverName, databaseName, DatabaseProvider.SqlServer);
    }

    private static void DisplaySqlServerHeader()
    {
        AnsiConsole.MarkupLine("[bold blue]╔══════════════════════════════════════════════╗[/]");
        AnsiConsole.MarkupLine("[bold blue]║[/]  [bold white]🗄️  SQL Server Connection[/]              [bold blue]║[/]");
        AnsiConsole.MarkupLine("[bold blue]╚══════════════════════════════════════════════╝[/]");
    }

    private static string PromptServerName()
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Server name or IP:[/]")
                .DefaultValue("localhost")
                .AllowEmpty());
    }

    private static string PromptDatabaseName()
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Database name:[/]")
                .AllowEmpty());
    }

    private static int PromptAuthenticationMethod()
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Authentication method:[/]")
                .PageSize(3)
                .AddChoices(new[]
                {
                    "🪟 Windows Authentication (Integrated Security)",
                    "🔐 SQL Server Authentication (User ID + Password)",
                    "← Go Back"
                }));

        return choice switch
        {
            "🪟 Windows Authentication (Integrated Security)" => 0,
            "🔐 SQL Server Authentication (User ID + Password)" => 1,
            _ => -1
        };
    }

    private static string PromptUserId()
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]User ID:[/]")
                .AllowEmpty());
    }

    private static string PromptPassword()
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Password:[/]")
                .Secret()
                .AllowEmpty());
    }

    private static string BuildSqlServerConnectionString(
        string server, 
        string database, 
        string userId, 
        string password, 
        bool useSqlAuth)
    {
        if (useSqlAuth)
        {
            return $"Server={server};Database={database};User Id={userId};Password={password};TrustServerCertificate=True;";
        }
        else
        {
            return $"Server={server};Database={database};Integrated Security=True;TrustServerCertificate=True;";
        }
    }

    private static void DisplayConnectionSummary(
        string server,
        string database,
        string userId,
        bool useSqlAuth,
        DatabaseProvider provider)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Green);

        table.AddColumn(new TableColumn("[bold]Setting[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Value[/]"));

        table.AddRow("[cyan]Provider[/]", $"[white]{provider}[/]");
        table.AddRow("[cyan]Server / Host[/]", $"[white]{server}[/]");
        table.AddRow("[cyan]Database[/]", $"[white]{database}[/]");

        var authDescription = provider switch
        {
            DatabaseProvider.SqlServer when !useSqlAuth
                => "[white]Windows Authentication (Integrated Security)[/]",
            _ when useSqlAuth && !string.IsNullOrWhiteSpace(userId)
                => $"[white]User/Password ({userId})[/]",
            _ when useSqlAuth
                => "[white]User/Password Authentication[/]",
            _ => "[white]Default authentication[/]"
        };

        table.AddRow("[cyan]Authentication[/]", authDescription);
        table.AddRow("[cyan]Status[/]", "[green]✓ Ready to connect[/]");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    #endregion

    #region MySQL Connection Builder

    private static (string connectionString, string serverName, string databaseName, DatabaseProvider provider) BuildMySqlConnection()
    {
        AnsiConsole.MarkupLine("[bold cyan]╔══════════════════════════════════════════════╗[/]");
        AnsiConsole.MarkupLine("[bold cyan]║[/]  [bold white]🐬  MySQL Connection[/]                    [bold cyan]║[/]");
        AnsiConsole.MarkupLine("[bold cyan]╚══════════════════════════════════════════════╝[/]");
        AnsiConsole.WriteLine();

        var host = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Server/Host:[/]")
                .DefaultValue("localhost")
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(host))
        {
            AnsiConsole.MarkupLine("[yellow]← Operation cancelled[/]");
            return (string.Empty, string.Empty, string.Empty, DatabaseProvider.MySQL);
        }

        var port = AnsiConsole.Prompt(
            new TextPrompt<int>("[cyan]Port:[/]")
                .DefaultValue(3306));

        var database = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Database name:[/]")
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(database))
        {
            AnsiConsole.MarkupLine("[yellow]← Operation cancelled[/]");
            return (string.Empty, string.Empty, string.Empty, DatabaseProvider.MySQL);
        }

        var userId = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]User ID:[/]")
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(userId))
        {
            AnsiConsole.MarkupLine("[yellow]← Operation cancelled[/]");
            return (string.Empty, string.Empty, string.Empty, DatabaseProvider.MySQL);
        }

        var password = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Password:[/]")
                .Secret()
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(password))
        {
            AnsiConsole.MarkupLine("[yellow]← Operation cancelled[/]");
            return (string.Empty, string.Empty, string.Empty, DatabaseProvider.MySQL);
        }

        var connectionString =
            $"Server={host};Port={port};Database={database};User Id={userId};Password={password};SslMode=Preferred;";

        AnsiConsole.WriteLine();
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("[yellow]Preparing connection...[/]", _ =>
            {
                System.Threading.Thread.Sleep(300);
            });

        DisplayConnectionSummary(host, database, userId, useSqlAuth: true, DatabaseProvider.MySQL);

        return (connectionString, host, database, DatabaseProvider.MySQL);
    }

    #endregion

    #region PostgreSQL Connection Builder

    private static (string connectionString, string serverName, string databaseName, DatabaseProvider provider) BuildPostgreSqlConnection()
    {
        AnsiConsole.MarkupLine("[bold cyan]╔══════════════════════════════════════════════╗[/]");
        AnsiConsole.MarkupLine("[bold cyan]║[/]  [bold white]🐘  PostgreSQL Connection[/]                [bold cyan]║[/]");
        AnsiConsole.MarkupLine("[bold cyan]╚══════════════════════════════════════════════╝[/]");
        AnsiConsole.WriteLine();

        var host = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Host:[/]")
                .DefaultValue("localhost")
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(host))
        {
            AnsiConsole.MarkupLine("[yellow]← Operation cancelled[/]");
            return (string.Empty, string.Empty, string.Empty, DatabaseProvider.PostgreSQL);
        }

        var port = AnsiConsole.Prompt(
            new TextPrompt<int>("[cyan]Port:[/]")
                .DefaultValue(5432));

        var database = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Database name:[/]")
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(database))
        {
            AnsiConsole.MarkupLine("[yellow]← Operation cancelled[/]");
            return (string.Empty, string.Empty, string.Empty, DatabaseProvider.PostgreSQL);
        }

        var userId = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Username:[/]")
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(userId))
        {
            AnsiConsole.MarkupLine("[yellow]← Operation cancelled[/]");
            return (string.Empty, string.Empty, string.Empty, DatabaseProvider.PostgreSQL);
        }

        var password = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Password:[/]")
                .Secret()
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(password))
        {
            AnsiConsole.MarkupLine("[yellow]← Operation cancelled[/]");
            return (string.Empty, string.Empty, string.Empty, DatabaseProvider.PostgreSQL);
        }

        var connectionString =
            $"Host={host};Port={port};Database={database};Username={userId};Password={password};";

        AnsiConsole.WriteLine();
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("[yellow]Preparing connection...[/]", _ =>
            {
                System.Threading.Thread.Sleep(300);
            });

        DisplayConnectionSummary(host, database, userId, useSqlAuth: true, DatabaseProvider.PostgreSQL);

        return (connectionString, host, database, DatabaseProvider.PostgreSQL);
    }

    #endregion

    #region SQLite Connection Builder

    private static (string connectionString, string serverName, string databaseName, DatabaseProvider provider) BuildSQLiteConnection()
    {
        AnsiConsole.MarkupLine("[bold cyan]╔══════════════════════════════════════════════╗[/]");
        AnsiConsole.MarkupLine("[bold cyan]║[/]  [bold white]📦  SQLite Connection[/]                  [bold cyan]║[/]");
        AnsiConsole.MarkupLine("[bold cyan]╚══════════════════════════════════════════════╝[/]");
        AnsiConsole.WriteLine();

        var filePath = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Database file path (.db/.sqlite):[/]")
                .DefaultValue("data.db")
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(filePath))
        {
            AnsiConsole.MarkupLine("[yellow]← Operation cancelled[/]");
            return (string.Empty, string.Empty, string.Empty, DatabaseProvider.SQLite);
        }

        var fullPath = Path.GetFullPath(filePath);
        var dbName = Path.GetFileNameWithoutExtension(fullPath);

        var connectionString = $"Data Source={fullPath};";

        AnsiConsole.WriteLine();
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("[yellow]Preparing connection...[/]", _ =>
            {
                System.Threading.Thread.Sleep(300);
            });

        DisplayConnectionSummary(fullPath, dbName, userId: string.Empty, useSqlAuth: false, DatabaseProvider.SQLite);

        return (connectionString, fullPath, dbName, DatabaseProvider.SQLite);
    }

    #endregion
}
