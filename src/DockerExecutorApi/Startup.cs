using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System.IO;
using System.Threading;

namespace DD.Research.DockerExecutor.Api
{
    public class Startup
    {
        static IConfiguration Configuration { get; set; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddOptions();
            services.Configure<DeployerOptions>(Configuration);

            services.AddLogging();
            services.AddMvc()
                .AddJsonOptions(json =>
				{
                    json.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
					json.SerializerSettings.Converters.Add(
						new StringEnumConverter()
					);
				});

            services.AddTransient<Deployer>();
        }

        public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(LogLevel.Trace, includeScopes: true);

            ILogger logger = loggerFactory.CreateLogger("DockerExecutorApi");
            logger.LogInformation("LocalStateDirectory: '{LocalStateDirectory}'.",
                Configuration["LocalStateDirectory"]
            );
            logger.LogInformation("HostStateDirectory: '{HostStateDirectory}'.",
                Configuration["HostStateDirectory"]
            );

            app.UseDeveloperExceptionPage();
            app.UseMvc();
        }

        public static void Main(string[] commandLineArguments)
        {
            SynchronizationContext.SetSynchronizationContext(
                new SynchronizationContext()
            );

            Configuration = LoadConfiguration();

            IWebHost host = new WebHostBuilder()
                .UseConfiguration(Configuration)
                .UseStartup<Startup>()
                .UseUrls("http://*:5050/")
                .UseKestrel()
                .Build();

            using (host)
            {
                host.Run();
            }
        }

        static IConfiguration LoadConfiguration()
        {
            return new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .AddEnvironmentVariables("DOOZER_")
                .Build();
        }
    }
}
