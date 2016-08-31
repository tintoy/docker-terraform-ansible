using Docker.DotNet;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet.Models;

namespace ConsoleApplication
{
    public static class Program
    {
        public static readonly string TargetImageName = "template/do-docker:latest";

        public static void Main()
        {
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Console.WriteLine(e.Exception);
                e.SetObserved();
            };

            SynchronizationContext.SetSynchronizationContext(
                new SynchronizationContext()
            );
            try
            {
                DockerClientConfiguration config = new DockerClientConfiguration(
                    new Uri("unix:///var/run/docker.sock")
                );
                DockerClient client = config.CreateClient();

                var targetImage = client.FindImageByTagNameAsync(TargetImageName).Result;
                if (targetImage == null)
                {
                    Console.WriteLine($"Image not found: '{TargetImageName}'.");

                    return;
                }

                Console.WriteLine($"Target image Id is '{targetImage.ID}'.");

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
                            "/Users/tintoy/development/test-projects/tfa-do-docker/state:/root/state"
                        },
                        LogConfig = new LogConfig
                        {
                            Type = "json-file",
                            Config = new Dictionary<string, string>()
                        },
                    }
                };

                CreateContainerResponse containerCreation = client.Containers.CreateContainerAsync(createParameters).Result;

                Console.WriteLine($"Created container '{containerCreation.ID}'.");

                CancellationTokenSource cts = new CancellationTokenSource();
                cts.CancelAfter(
                    TimeSpan.FromMinutes(5)
                );

                bool terminated = false;
                Task eventPump = Task.Factory.StartNew(() =>
                {
                    using (DockerClient eventClient = client.Configuration.CreateClient())
                    {
                        Console.WriteLine("+++Waiting for events+++");
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
                        using (Stream eventStream = eventClient.Miscellaneous.MonitorEventsAsync(eventsParameters, cts.Token).Result)
                        using (StreamReader eventReader = new StreamReader(eventStream))
                        {
                            JsonSerializer serializer = new JsonSerializer();
                            string line = eventReader.ReadLine();
                            while (line != null)
                            {
                                JObject evt = JsonConvert.DeserializeObject<JObject>(line);
                                string containerStatus = evt.Value<string>("status");
                                Console.WriteLine("ContainerStatus: '{0}'", containerStatus);

                                if (containerStatus == "die")
                                {
                                    terminated = true;
                                    
                                    return;
                                }

                                line = eventReader.ReadLine();
                            }
                        }
                        Console.WriteLine("+++End of events+++");
                    }
                });

                Task logPump = Task.Factory.StartNew(() =>
                {
                    using (DockerClient logClient = client.Configuration.CreateClient())
                    {
                        CancellationToken token = cts.Token;

                        Console.WriteLine("***Waiting for output***");
                        ContainerLogsParameters logParameters = new ContainerLogsParameters
                        {
                            ShowStdout = true,
                            ShowStderr = true,
                            Follow = true
                        };
                        using (Stream logStream = logClient.Containers.GetContainerLogsAsync(containerCreation.ID, logParameters, cts.Token).Result)
                        using (StreamReader logReader = new StreamReader(logStream))
                        {
                            while (!terminated)
                            {
                                if (token.IsCancellationRequested)
                                    return;

                                string line = logReader.ReadLine();
                                if (line != null)
                                    Console.WriteLine(line);
                                else
                                    Thread.Sleep(200);
                            }
                        }
                        Console.WriteLine("***End of output***");
                    }
                });

                bool started = client.Containers.StartContainerAsync(containerCreation.ID, new HostConfig()).Result;
                Console.WriteLine($"Started container: {started}");

                Task.WaitAll(eventPump, logPump);

                client.Containers.RemoveContainerAsync(containerCreation.ID, new ContainerRemoveParameters
                {
                    Force = true
                });
            }
            catch (Exception unexpectedError)
            {
                Console.WriteLine(unexpectedError);
            }
        }

        static async Task<ImagesListResponse> FindImageByTagNameAsync(this DockerClient client, string tagName)
        {
            var images = await client.Images.ListImagesAsync(
                new ImagesListParameters()
            );

            return images.FirstOrDefault(image =>
                image.RepoTags != null
                &&
                image.RepoTags.Contains("template/do-docker:latest")
            );
        }
    }
}
