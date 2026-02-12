using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace TextToSqlAgent.Console.Setup;

public static class ConfigurationLoader
{
    public static void Configure(HostBuilderContext context, IConfigurationBuilder config)
    {
        config.SetBasePath(Directory.GetCurrentDirectory())
              .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
              .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json",
                          optional: true, reloadOnChange: true)
              .AddUserSecrets<Program>(optional: true)
              .AddEnvironmentVariables();
    }
}