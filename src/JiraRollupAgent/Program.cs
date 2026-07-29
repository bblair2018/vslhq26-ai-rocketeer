using System.Diagnostics.CodeAnalysis;
using JiraRollupAgent.DAL.Context;
using JiraRollupAgent.DAL.Repositories.Implementations;
using JiraRollupAgent.DAL.Repositories.Interfaces;
using JiraRollupAgent.Extensions;
using JiraRollupAgent.Services.HtmlReportGeneratorService;
using JiraRollupAgent.Services.JiraHierarchyLoaderService;
using JiraRollupAgent.Services.SummarizationService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace JiraRollupAgent
{
    [ExcludeFromCodeCoverage]
    class Program
    {
        #region Main

        /// <summary>
        /// This is the main entry point to the application. This is used to call the Async main.
        /// </summary>
        /// <param name="args">An array of arguments to be passed to the application.</param>
        static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        #endregion

        #region MainAsync

        /// <summary>
        /// This is the secondary main entry point to the application.
        /// </summary>
        /// <param name="args">These are the arguments for the main application.</param>
        [ExcludeFromCodeCoverage]
        private static async Task<bool> MainAsync(string[] args)
        {
            try
            {
                #region Setup application settings configuration.

                var builder = new ConfigurationBuilder();
                BuildConfig(builder);

                #endregion

                #region Setup logging for Serilog.

                // The File sink's path is set here (rather than in appsettings.json's Serilog:WriteTo)
                // so it always resolves relative to the app's base directory, not the process's
                // current working directory — otherwise "dotnet run"/"dotnet ef" from different
                // folders scatter log files across the repo.
                var logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "JiraRollupAgent.log");
                const string logOutputTemplate = "[{Timestamp:HH:mm:ss.fff zzz} {Level}] {MachineName} {SourceContext}.{MemberName}(Line#:{LineNumber}) => {Message:lj}{NewLine}{Exception}";

                Log.Logger = new LoggerConfiguration()
                        .ReadFrom.Configuration(builder.Build())
                        .MinimumLevel.Verbose()
                        .Enrich.FromLogContext()
                        .Enrich.WithMachineName()
                        .WriteTo.File(logFilePath, outputTemplate: logOutputTemplate, rollingInterval: RollingInterval.Day)
                        .CreateLogger();

                Log.Logger.Here().Information("Application Starting...");

                #endregion

                #region Getting Environment Information

                Log.Logger.Here().Information("Getting Environment Information...");
                var environmentConfiguration = new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .Build();
                var environment = environmentConfiguration["ASPNETCORE_ENVIRONMENT"];
                Log.Logger.Here().Information("Environment: {Environment}", environment);

                #endregion

                #region Registering Services

                Log.Logger.Here().Information("Registering Services...");
                var host = Host.CreateDefaultBuilder()
                    .ConfigureAppConfiguration((context, config) => BuildConfig(config))
                    .ConfigureServices((context, services) =>
                    {
                        // Registering JiraRollupDBContext as a service.
                        services.AddDbContext<JiraRollupDBContext>(options =>
                            options.UseSqlServer(context.Configuration.GetConnectionString("VSLiveJiraRollupConnectionString")));

                        // Registering UnitOfWork and repositories as services.
                        services.AddScoped<IUnitOfWork, UnitOfWork>();

                        // Registering the IJiraHierarchyLoaderService - loads the mocked Jira hierarchy.
                        services.AddScoped<IJiraHierarchyLoaderService, JiraHierarchyLoaderService>();

                        // Registering the ISummarizationService - generates item/Epic/Initiative summaries via Azure OpenAI.
                        services.AddScoped<ISummarizationService, SummarizationService>();

                        // Registering the IHtmlReportGeneratorService - generates the single HTML rollup report.
                        services.AddScoped<IHtmlReportGeneratorService, HtmlReportGeneratorService>();
                    })
                    .UseSerilog()
                    .Build();

                #endregion

                #region Running the Services.

                Log.Logger.Here().Information("\n\nStarting process to build the Jira rollup report...\n");

                bool isSuccess = false;

                #region JiraHierarchyLoaderService

                var jiraHierarchyLoaderService = ActivatorUtilities.CreateInstance<JiraHierarchyLoaderService>(host.Services);
                isSuccess = await jiraHierarchyLoaderService.Run();
                if (isSuccess)
                    Log.Logger.Here().Information("Process to load the Jira hierarchy Finished Normally.");
                else
                    Log.Logger.Here().Error("Process to load the Jira hierarchy *FAILED TO COMPLETE* as expected!");

                #endregion

                #region SummarizationService

                var summarizationService = ActivatorUtilities.CreateInstance<SummarizationService>(host.Services);
                isSuccess = await summarizationService.Run();
                if (isSuccess)
                    Log.Logger.Here().Information("Process to generate summaries Finished Normally.");
                else
                    Log.Logger.Here().Error("Process to generate summaries *FAILED TO COMPLETE* as expected!");

                #endregion

                #region HtmlReportGeneratorService

                var htmlReportGeneratorService = ActivatorUtilities.CreateInstance<HtmlReportGeneratorService>(host.Services);
                isSuccess = await htmlReportGeneratorService.Run();
                if (isSuccess)
                    Log.Logger.Here().Information("Process to generate the HTML rollup report Finished Normally.");
                else
                    Log.Logger.Here().Error("Process to generate the HTML rollup report *FAILED TO COMPLETE* as expected!");

                #endregion

                Log.Logger.Here().Information("\n\nGathering Return Status...\n");

                if (isSuccess)
                {
                    Log.Logger.Here().Information("Returning *SUCCESS*!:--> TRUE");
                    return true;
                }
                else
                {
                    Log.Logger.Here().Error("Returning *FAILURE*!:--> FALSE");
                    return false;
                }

                #endregion
            }
            catch (Exception ex)
            {
                Log.Logger.Here().Error("Something unexpected happened! Returning *FAILURE*! {Error}.", ex.ToString());
                return false;
            }
        }

        #endregion

        #region BuildConfig

        /// <summary>
        /// This will allow us to do logging, before we work with our actual configuration.
        /// </summary>
        /// <param name="builder">Our Configuration Builder.</param>
        [ExcludeFromCodeCoverage]
        static void BuildConfig(IConfigurationBuilder builder)
        {
            string directoryToBeUsed = AppDomain.CurrentDomain.BaseDirectory;

            System.Console.WriteLine($"Using directory for appsettings.json:--> {directoryToBeUsed}");

            builder.SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .AddUserSecrets<Program>()
                .AddEnvironmentVariables();
        }

        #endregion
    }
}
