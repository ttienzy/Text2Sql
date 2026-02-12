using Spectre.Console;
using Microsoft.Data.SqlClient;
using TextToSqlAgent.Infrastructure.Configuration;

namespace TextToSqlAgent.Console.UI;

/// <summary>
/// Interactive UI for building SQL Server connection strings step-by-step
/// Supports Windows Authentication and SQL Server Authentication
/// TrustServerCertificate is always True by default
/// </summary>
public static class ConnectionBuilder
{
    /// <summary>
    /// Prompt user to build connection string interactively with step-by-step wizard
    /// </summary>
    public static (string connectionString, string serverName, string databaseName) BuildConnectionString()
    {
        AnsiConsole.WriteLine();
        DisplaySqlServerHeader();
        AnsiConsole.WriteLine();

        // Step 1: Server Name
        var serverName = PromptServerName();
        if (string.IsNullOrEmpty(serverName))
        {
            AnsiConsole.MarkupLine("[yellow]← Operation cancelled[/]");
            return (string.Empty, string.Empty, string.Empty);
        }

        // Step 2: Database Name
        var databaseName = PromptDatabaseName();
        if (string.IsNullOrEmpty(databaseName))
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        // Step 3: Authentication Method
        var authChoice = PromptAuthenticationMethod();
        if (authChoice == -1) // User chose to go back
        {
            return BuildConnectionString(); // Restart the flow
        }

        string userId = string.Empty;
        string password = string.Empty;

        if (authChoice == 1) // SQL Server Authentication
        {
            // Step 4: User ID (only for SQL Server Auth)
            userId = PromptUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BuildConnectionString(); // Restart if user goes back
            }

            // Step 5: Password (only for SQL Server Auth)
            password = PromptPassword();
            if (string.IsNullOrEmpty(password))
            {
                return BuildConnectionString(); // Restart if user goes back
            }
        }

        // Build connection string
        var connectionString = BuildConnectionStringCore(serverName, databaseName, userId, password, authChoice == 1);

        // Test connection immediately
        AnsiConsole.WriteLine();
        var connectionResult = TestConnectionWithDetails(connectionString);

        if (!connectionResult.Success)
        {
            // Show error and offer retry options
            DisplayConnectionError(connectionResult);
            AnsiConsole.WriteLine();

            var retryOption = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]What would you like to do?[/]")
                    .PageSize(4)
                    .AddChoices(new[]
                    {
                        "🔄 Try Again",
                        "✏️  Edit Connection Details",
                        "📋 Show Technical Details",
                        "← Start Over"
                    }));

            AnsiConsole.WriteLine();

            if (retryOption == "← Start Over")
            {
                return (string.Empty, string.Empty, string.Empty);
            }

            if(retryOption == "📋 Show Technical Details")
            {
                DisplayTechnicalErrorDetails(connectionResult);
                AnsiConsole.WriteLine();

                var afterDetails = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[yellow]What would you like to do?[/]")
                        .PageSize(3)
                        .AddChoices(new[]
                        {
                            "🔄 Try Again",
                            "✏️  Edit Connection Details",
                            "← Start Over"
                        }));

                if (afterDetails == "← Start Over")
                {
                    return (string.Empty, string.Empty, string.Empty);
                }

                if (afterDetails == "🔄 Try Again")
                {
                    return BuildConnectionString();
                }

                // Edit details - fall through to retry loop
            }
            else if (retryOption == "🔄 Try Again")
            {
                return BuildConnectionString();
            }

            // ✏️ Edit - loop for retry
            return RetryWithEdits(serverName, databaseName, userId, password, authChoice == 1);
        }

        // Connection successful - display summary
        DisplayConnectionSummary(serverName, databaseName, userId, authChoice == 1);

        return (connectionString, serverName, databaseName);
    }

    private static (string connectionString, string serverName, string databaseName) RetryWithEdits(
        string currentServer, string currentDatabase, string currentUserId, string currentPassword, bool useSqlAuth)
    {
        while (true)
        {
            var editOption = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]What would you like to edit?[/]")
                    .PageSize(5)
                    .AddChoices(new[]
                    {
                        "📍 Server Name",
                        "📦 Database",
                        "🔐 Authentication Method",
                        "✓ All Correct - Try Again",
                        "← Start Over"
                    }));

            if (editOption == "← Start Over")
            {
                return (string.Empty, string.Empty, string.Empty);
            }

            if (editOption == "✓ All Correct - Try Again")
            {
                var newConnectionString = BuildConnectionStringCore(
                    currentServer, currentDatabase, currentUserId, currentPassword, useSqlAuth);

                var result = TestConnectionWithDetails(newConnectionString);

                if (!result.Success)
                {
                    DisplayConnectionError(result);
                    AnsiConsole.WriteLine();
                    continue; // Loop again for more edits
                }

                DisplayConnectionSummary(currentServer, currentDatabase, currentUserId, useSqlAuth);
                return (newConnectionString, currentServer, currentDatabase);
            }

            // Handle individual edits
            switch (editOption)
            {
                case "📍 Server Name":
                    var newServer = PromptServerNameForEdit(currentServer);
                    if (!string.IsNullOrEmpty(newServer))
                        currentServer = newServer;
                    break;

                case "📦 Database":
                    var newDb = PromptDatabaseNameForEdit(currentDatabase);
                    if (!string.IsNullOrEmpty(newDb))
                        currentDatabase = newDb;
                    break;

                case "🔐 Authentication Method":
                    var newAuth = PromptAuthenticationMethodForEdit(useSqlAuth ? "SQL Server Authentication" : "Windows Authentication");
                    if (newAuth == -1) // Go back
                        continue;
                    else if (newAuth == 0)
                    {
                        useSqlAuth = false;
                        currentUserId = string.Empty;
                        currentPassword = string.Empty;
                    }
                    else if (newAuth == 1)
                    {
                        useSqlAuth = true;
                        currentUserId = PromptUserIdForEdit(currentUserId);
                        currentPassword = PromptPasswordForEdit(currentPassword);
                    }
                    break;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[dim]Current settings: Server={currentServer}, Database={currentDatabase}, Auth={(useSqlAuth ? "SQL" : "Windows")}[/]");
            AnsiConsole.WriteLine();
        }
    }

    private static ConnectionTestResult TestConnectionWithDetails(string connectionString)
    {
        try
        {
            using var connection = new SqlConnection(connectionString);
            
            // Use a timeout for the test
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            connection.OpenAsync(cts.Token).GetAwaiter().GetResult();
            
            return new ConnectionTestResult
            {
                Success = true,
                Message = "Connection successful"
            };
        }
        catch (SqlException ex)
        {
            return new ConnectionTestResult
            {
                Success = false,
                ErrorType = GetErrorType(ex.Number),
                ErrorCode = ex.Number.ToString(),
                Message = GetUserFriendlyMessage(ex.Number),
                TechnicalDetails = ex.Message,
                Hints = GetConnectionHints(ex.Number)
            };
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult
            {
                Success = false,
                ErrorType = "Network",
                ErrorCode = "GEN",
                Message = GetGenericErrorMessage(ex.Message),
                TechnicalDetails = ex.Message,
                Hints = new List<string>
                {
                    "Check your network connection",
                    "Verify the server name is correct",
                    "Ensure SQL Server is running and accessible"
                }
            };
        }
    }

    private static string GetErrorType(int errorCode)
    {
        return errorCode switch
        {
            -1 or 2 or 53 => "ServerNotFound",
            4060 => "DatabaseNotFound",
            18456 => "AuthenticationFailed",
            18487 or 18488 => "PasswordExpired",
            233 or 121 => "ConnectionTimeout",
            40615 => "RemoteConnection",
            17 or 64 => "NetworkError",
            20 or 26 => "InstanceNotFound",
            _ => "Unknown"
        };
    }

    private static string GetUserFriendlyMessage(int errorCode)
    {
        return errorCode switch
        {
            -1 or 2 or 53 => "Cannot connect to server. Please check the server name.",
            4060 => "Cannot open database. The database name may be incorrect or you don't have access.",
            18456 => "Login failed. Check your username and password.",
            18487 => "Password has expired. Please change your password.",
            18488 => "Password must be changed. Please update your password.",
            233 => "Connection timeout. Server may be unreachable.",
            121 => "Connection timeout. Server may be unreachable.",
            17 => "Network error. Cannot connect to server.",
            64 => "Network error. Connection was closed.",
            20 => "Specified database instance not found.",
            26 => "Error locating server instance.",
            40615 => "Remote connections are disabled on the server.",
            _ => $"Connection failed (Error code: {errorCode})"
        };
    }

    private static string GetGenericErrorMessage(string exceptionMessage)
    {
        if (exceptionMessage.Contains("A network-related or instance-specific error"))
            return "Cannot connect to server. Check server name and network connection.";
        if (exceptionMessage.Contains("Login failed"))
            return "Login failed. Check username and password.";
        if (exceptionMessage.Contains("timeout"))
            return "Connection timed out. Server may be busy or unreachable.";
        return "An error occurred while connecting to the database.";
    }

    private static List<string> GetConnectionHints(int errorCode)
    {
        var hints = new List<string>();

        switch (errorCode)
        {
            case -1 or 2 or 53:
                hints.Add("Verify the server name is correct (e.g., localhost, ., .\\SQLEXPRESS)");
                hints.Add("Check if SQL Server is running");
                hints.Add("Ensure TCP/IP protocol is enabled in SQL Server Configuration Manager");
                hints.Add("Check firewall settings allow SQL Server connections");
                break;
            case 4060:
                hints.Add("Verify the database name is spelled correctly");
                hints.Add("Ensure you have permission to access this database");
                hints.Add("Check if the database exists on the server");
                break;
            case 18456:
                hints.Add("Check the username is correct");
                hints.Add("Verify the password is correct");
                hints.Add("Ensure the login exists on the SQL Server");
                hints.Add("Check if the login is locked out or disabled");
                break;
            case 18487:
            case 18488:
                hints.Add("Password has expired and must be changed");
                hints.Add("Contact your database administrator");
                break;
            case 233:
            case 121:
                hints.Add("Connection timed out");
                hints.Add("Check if the server is reachable");
                hints.Add("Verify the port (default 1433) is open");
                break;
            case 17:
            case 64:
                hints.Add("Network error occurred");
                hints.Add("Check your network connection");
                hints.Add("Verify the server is online");
                break;
            case 20:
            case 26:
                hints.Add("Cannot find the specified SQL Server instance");
                hints.Add("Verify the instance name is correct");
                hints.Add("Check if the named instance is running");
                break;
            case 40615:
                hints.Add("Remote connections are disabled");
                hints.Add("Enable remote connections in SQL Server settings");
                break;
            default:
                hints.Add("Review the error message for specific details");
                hints.Add("Check SQL Server logs for more information");
                break;
        }

        return hints;
    }

    private static void DisplayConnectionError(ConnectionTestResult result)
    {
        var errorPanel = new Panel(
            new Markup($"[bold red]❌ {result.Message}[/]\n\n[dim]Error Code: {result.ErrorCode}[/]"))
        {
            Header = new PanelHeader("Connection Failed", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Red),
            Padding = new Padding(2, 1, 2, 1)
        };

        AnsiConsole.Write(errorPanel);
    }

    private static void DisplayTechnicalErrorDetails(ConnectionTestResult result)
    {
        var detailsPanel = new Panel(
            new Markup($"[yellow]Technical Details:[/]\n{result.TechnicalDetails}"))
        {
            Header = new PanelHeader("Technical Error Information", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow),
            Padding = new Padding(2, 1, 2, 1)
        };

        AnsiConsole.Write(detailsPanel);
        AnsiConsole.WriteLine();

        // Show hints
        if (result.Hints != null && result.Hints.Count > 0)
        {
            var hintsTree = new Tree("[yellow]💡 Suggestions:[/]");
            foreach (var hint in result.Hints)
            {
                hintsTree.AddNode($"[dim]{hint}[/]");
            }
            AnsiConsole.Write(hintsTree);
        }
    }

    private static void DisplaySqlServerHeader()
    {
        var header = new Panel(
            new Markup("[bold yellow]----- Microsoft SQL Server (SqlClient) -----[/]\n" +
                       "[dim]Secure database connection with TrustServerCertificate[/]"))
        {
            Border = BoxBorder.Double,
            BorderStyle = new Style(Color.Cyan),
            Padding = new Padding(2, 1, 2, 1)
        };

        AnsiConsole.Write(header);
    }

    private static string PromptServerName()
    {
        while (true)
        {
            AnsiConsole.MarkupLine("[yellow]📍 Server Name[/]");
            AnsiConsole.MarkupLine("[dim]   Example: localhost, ., .\\SQLEXPRESS, 192.168.1.100[/]");
            AnsiConsole.WriteLine();

            var serverName = AnsiConsole.Prompt(
                new TextPrompt<string>("   Server: ")
                    .PromptStyle("white")
                    .DefaultValue(".")
                    .ValidationErrorMessage("[red]Server cannot be empty[/]")
                    .Validate(s => !string.IsNullOrWhiteSpace(s)));

            AnsiConsole.WriteLine();

            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]Confirm server name?[/]")
                    .PageSize(3)
                    .AddChoices(new[] { "✓ Continue", "✏️  Edit", "← Go Back" }));

            if (confirm == "← Go Back")
                return string.Empty;

            if (confirm == "✓ Continue")
                return serverName;
        }
    }

    private static string PromptServerNameForEdit(string current)
    {
        AnsiConsole.MarkupLine("[yellow]📍 Server Name[/]");
        AnsiConsole.WriteLine();

        var serverName = AnsiConsole.Prompt(
            new TextPrompt<string>("   Server: ")
                .PromptStyle("white")
                .DefaultValue(current)
                .ValidationErrorMessage("[red]Server cannot be empty[/]")
                .Validate(s => !string.IsNullOrWhiteSpace(s)));

        return serverName;
    }

    private static string PromptDatabaseName()
    {
        while (true)
        {
            AnsiConsole.MarkupLine("[yellow]📦 Database[/]");
            AnsiConsole.WriteLine();

            var databaseName = AnsiConsole.Prompt(
                new TextPrompt<string>("   Database: ")
                    .PromptStyle("white")
                    .ValidationErrorMessage("[red]Database cannot be empty[/]")
                    .Validate(s => !string.IsNullOrWhiteSpace(s)));

            AnsiConsole.WriteLine();

            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]Confirm database name?[/]")
                    .PageSize(4)
                    .AddChoices(new[] { "✓ Continue", "✏️  Edit", "← Change Server", "← Start Over" }));

            if (confirm == "← Start Over")
                return string.Empty;

            if (confirm == "← Change Server")
                return string.Empty;

            if (confirm == "✓ Continue")
                return databaseName;
        }
    }

    private static string PromptDatabaseNameForEdit(string current)
    {
        AnsiConsole.MarkupLine("[yellow]📦 Database[/]");
        AnsiConsole.WriteLine();

        var databaseName = AnsiConsole.Prompt(
            new TextPrompt<string>("   Database: ")
                .PromptStyle("white")
                .DefaultValue(current)
                .ValidationErrorMessage("[red]Database cannot be empty[/]")
                .Validate(s => !string.IsNullOrWhiteSpace(s)));

        return databaseName;
    }

    private static int PromptAuthenticationMethod()
    {
        AnsiConsole.MarkupLine("[yellow]🔐 Authentication Method[/]");
        AnsiConsole.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("   Select authentication method:")
                .PageSize(3)
                .AddChoices(new[] { "🪟 Windows Authentication", "🔑 SQL Server Authentication", "← Go Back" }));

        AnsiConsole.WriteLine();

        if (choice == "← Go Back")
            return -1;

        return choice.Contains("SQL Server") ? 1 : 0;
    }

    private static int PromptAuthenticationMethodForEdit(string current)
    {
        AnsiConsole.MarkupLine("[yellow]🔐 Authentication Method[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]   Current: {current}[/]");
        AnsiConsole.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("   Select authentication method:")
                .PageSize(3)
                .AddChoices(new[] { "🪟 Windows Authentication", "🔑 SQL Server Authentication", "← Go Back" }));

        AnsiConsole.WriteLine();

        if (choice == "← Go Back")
            return -1;

        return choice.Contains("SQL Server") ? 1 : 0;
    }

    private static string PromptUserId()
    {
        while (true)
        {
            AnsiConsole.MarkupLine("[yellow]👤 User ID (SQL Server Authentication)[/]");
            AnsiConsole.WriteLine();

            var userId = AnsiConsole.Prompt(
                new TextPrompt<string>("   User ID: ")
                    .PromptStyle("white")
                    .ValidationErrorMessage("[red]User ID cannot be empty[/]")
                    .Validate(s => !string.IsNullOrWhiteSpace(s)));

            AnsiConsole.WriteLine();

            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]Confirm User ID?[/]")
                    .PageSize(4)
                    .AddChoices(new[] { "✓ Continue", "✏️  Edit", "← Change Authentication", "← Start Over" }));

            if (confirm == "← Start Over")
                return string.Empty;

            if (confirm == "← Change Authentication")
                return string.Empty;

            if (confirm == "✓ Continue")
                return userId;
        }
    }

    private static string PromptUserIdForEdit(string current)
    {
        AnsiConsole.MarkupLine("[yellow]👤 User ID (SQL Server Authentication)[/]");
        AnsiConsole.WriteLine();

        var userId = AnsiConsole.Prompt(
            new TextPrompt<string>("   User ID: ")
                .PromptStyle("white")
                .DefaultValue(current)
                .ValidationErrorMessage("[red]User ID cannot be empty[/]")
                .Validate(s => !string.IsNullOrWhiteSpace(s)));

        return userId;
    }

    private static string PromptPassword()
    {
        while (true)
        {
            AnsiConsole.MarkupLine("[yellow]🔑 Password (SQL Server Authentication)[/]");
            AnsiConsole.WriteLine();

            var password = AnsiConsole.Prompt(
                new TextPrompt<string>("   Password: ")
                    .PromptStyle("white")
                    .Secret()
                    .AllowEmpty());

            AnsiConsole.WriteLine();

            var confirm = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]Confirm password?[/]")
                    .PageSize(4)
                    .AddChoices(new[] { "✓ Continue", "✏️  Edit", "← Change User ID", "← Start Over" }));

            if (confirm == "← Start Over")
                return string.Empty;

            if (confirm == "← Change User ID")
                return string.Empty;

            if (confirm == "✓ Continue")
                return password;
        }
    }

    private static string PromptPasswordForEdit(string current)
    {
        AnsiConsole.MarkupLine("[yellow]🔑 Password (SQL Server Authentication)[/]");
        AnsiConsole.WriteLine();

        var password = AnsiConsole.Prompt(
            new TextPrompt<string>("   Password: ")
                .PromptStyle("white")
                .Secret()
                .AllowEmpty());

        return password;
    }

    private static string BuildConnectionStringCore(string server, string database, string userId, string password, bool useSqlAuth)
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

    private static void DisplayConnectionSummary(string server, string database, string userId, bool useSqlAuth)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Green);

        table.AddColumn("[bold]Property[/]");
        table.AddColumn("[bold]Value[/]");

        table.AddRow("Server", $"[cyan]{server}[/]");
        table.AddRow("Database", $"[cyan]{database}[/]");
        table.AddRow("Authentication", useSqlAuth ? "[yellow]SQL Server Authentication[/]" : "[green]Windows Authentication[/]");
        
        if (useSqlAuth)
        {
            table.AddRow("User ID", $"[cyan]{userId}[/]");
        }
        
        table.AddRow("Trust Certificate", "[green]True (default)[/]");

        var panel = new Panel(table)
        {
            Header = new PanelHeader("✅ Connection Summary", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Green),
            Padding = new Padding(2, 1, 2, 1)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Result of connection test with detailed information
    /// </summary>
    public class ConnectionTestResult
    {
        public bool Success { get; set; }
        public string ErrorType { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string TechnicalDetails { get; set; } = string.Empty;
        public List<string> Hints { get; set; } = new();
    }
}
