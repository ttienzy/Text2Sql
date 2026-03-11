using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TextToSqlAgent.Console.Configuration;

namespace TextToSqlAgent.Console.Setup;

public static class ConfigurationLoader
{
    public static void Configure(HostBuilderContext context, IConfigurationBuilder config)
    {
        config.SetBasePath(Directory.GetCurrentDirectory())
              .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
              .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json",
                          optional: true, reloadOnChange: true)
              .AddEnvironmentVariables();

        // Load secure configuration (API keys) - highest priority
        LoadSecureConfiguration(config);
    }

    private static void LoadSecureConfiguration(IConfigurationBuilder config)
    {
        try
        {
            var secureStore = new SecureConfigStore();
            var secureConfig = secureStore.LoadConfig();

            if (secureConfig.IsConfigured && !string.IsNullOrEmpty(secureConfig.OpenAIApiKey))
            {
                // Add secure config to configuration with highest priority
                var secureSettings = new Dictionary<string, string?>
                {
                    ["OpenAI:ApiKey"] = secureConfig.OpenAIApiKey
                };

                config.AddInMemoryCollection(secureSettings);
            }
        }
        catch
        {
            // If secure config fails to load, continue without it
            // Environment variables or appsettings will be used as fallback
        }
    }
}
