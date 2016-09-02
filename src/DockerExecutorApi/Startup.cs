using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System.Threading;

namespace DD.Research.DockerExecutor.Api
{
    public class Startup
    {
        static IConfiguration Configuration { get; set; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging();
            services.AddMvc()
                .AddJsonOptions(json =>
				{
                    json.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
					json.SerializerSettings.Converters.Add(
						new StringEnumConverter()
					);
				});

            services.AddTransient<Executor>();
        }

        public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole();

            app.UseMvc();
        }

        public static void Main(string[] commandLineArguments)
        {
            SynchronizationContext.SetSynchronizationContext(
                new SynchronizationContext()
            );

            Configuration = LoadConfiguration(commandLineArguments);

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

        static IConfiguration LoadConfiguration(string[] commandLineArguments)
        {
            return new ConfigurationBuilder()
                .AddCommandLine(commandLineArguments)
                .AddEnvironmentVariables()
                .Build();
        }
    }
}