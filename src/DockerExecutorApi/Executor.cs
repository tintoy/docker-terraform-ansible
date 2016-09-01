using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Threading;

namespace DD.Research.DockerExecutor.Api
{
    public class Executor
    {
        public Executor(ILogger<Executor> logger)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            Log = logger;
        }

        ILogger Log { get; }

        public async Task<bool> ExecuteAsync(string targetImageName, DirectoryInfo stateDirectory)
        {
            if (String.IsNullOrWhiteSpace(targetImageName))
                throw new ArgumentException("Must supply a valid target image name.", nameof(targetImageName));

            try
            {
                DockerClientConfiguration config = new DockerClientConfiguration(
                    new Uri("unix:///var/run/docker.sock")
                );
                DockerClient client = config.CreateClient();

                ImagesListResponse targetImage = await client.Images.FindImageByTagNameAsync(targetImageName);
                if (targetImage == null)
                {
                    Log.LogError("Image not found: '{TargetImageName}'.", targetImageName);

                    return false;
                }

                Log.LogInformation("Target image Id is '{TargetImageId}'.", targetImage.ID);

                CreateContainerParameters createParameters = new CreateContainerParameters
                {
                    Name = "do-docker",
                    Image = targetImage.ID,
                    AttachStdout = true,
                    AttachStderr = true,
                    HostConfig = new HostConfig
                    {
                        Binds = new List<string>
                        {
                            $"{stateDirectory.FullName}:/root/state"
                        },
                        LogConfig = new LogConfig
                        {
                            Type = "json-file",
                            Config = new Dictionary<string, string>()
                        }
                    },
                    Env = new List<string>
                    {
                        "ANSIBLE_NOCOLOR=1" // Disable coloured output because escape sequences look weird in the log.
                    }
                };

                CreateContainerResponse containerCreation = client.Containers.CreateContainerAsync(createParameters).Result;
                Log.LogInformation("Created container '{ContainerId}'.", containerCreation.ID);

                bool started = client.Containers.StartContainerAsync(containerCreation.ID, new HostConfig()).Result;
                Log.LogInformation("Started container: {ContainerStarted}", started);

                Log.LogInformation("Waiting for events");
                ContainerEventsParameters eventsParameters = new ContainerEventsParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["container"] = new Dictionary<string, bool>
                        {
                            [containerCreation.ID] = true
                        }
                    }
                };

                using (Stream eventStream = await client.Miscellaneous.MonitorEventsAsync(eventsParameters, CancellationToken.None))
                using (StreamReader eventReader = new StreamReader(eventStream))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    string line = eventReader.ReadLine();
                    while (line != null)
                    {
                        JObject evt = JsonConvert.DeserializeObject<JObject>(line);
                        string containerStatus = evt.Value<string>("status");
                        Log.LogDebug("Status-change event for container '{ContainerId}': {ContainerStatus}", containerCreation.ID, containerStatus);

                        if (containerStatus == "die")
                            break;

                        line = eventReader.ReadLine();
                    }
                }
                Log.LogInformation("End of events");

                Log.LogDebug("Reading logs for container {ContainerId}.", containerCreation.ID);
                ContainerLogsParameters logParameters = new ContainerLogsParameters
                {
                    ShowStdout = true,
                    ShowStderr = true,
                    Follow = false
                };
                using (Stream logStream = await client.Containers.GetContainerLogsAsync(containerCreation.ID, logParameters, CancellationToken.None))
                using (StreamReader logReader = new StreamReader(logStream))
                {
                    string output = logReader.ReadToEnd();
                    Log.LogDebug("Log entries for container {ContainerId}:\n{ContainerLogEntries}", containerCreation.ID, output);
                }
                
                await client.Containers.RemoveContainerAsync(containerCreation.ID, new ContainerRemoveParameters
                {
                    Force = true
                });

                return true;
            }
            catch (Exception unexpectedError)
            {
                Console.WriteLine(unexpectedError);

                return false;
            }
        }
    }
}