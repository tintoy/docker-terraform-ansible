using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DD.Research.DockerExecutor.Api
{
    /// <summary>
    ///     The executor for deployment jobs via Docker.
    /// </summary>
    public class Executor
    {
        /// <summary>
        ///     Create a new <see cref="Executor"/>.
        /// </summary>
        /// <param name="logger">
        ///     The executor logger.
        /// </summary>
        public Executor(ILogger<Executor> logger)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            Log = logger;
        }

        /// <summary>
        ///     The executor logger.
        /// </summary>
        ILogger Log { get; }

        /// <summary>
        ///     Execute a deployment.
        /// </summary>
        /// <param name="templateImageName">
        ///     The name of the Docker image that implements the deployment template.
        /// </param>
        /// <param name="stateDirectory"></param>
        /// <returns>
        ///     <c>true</c>, if the deployment was successful; otherwise, <c>false</c>.
        /// </returns>
        public async Task<Result> ExecuteAsync(string templateImageName, DirectoryInfo stateDirectory)
        {
            if (String.IsNullOrWhiteSpace(templateImageName))
                throw new ArgumentException("Must supply a valid template image name.", nameof(templateImageName));

            if (stateDirectory == null)
                throw new ArgumentNullException(nameof(stateDirectory));

            try
            {
                DockerClientConfiguration config = new DockerClientConfiguration(
                    new Uri("unix:///var/run/docker.sock")
                );
                DockerClient client = config.CreateClient();

                ImagesListResponse targetImage = await client.Images.FindImageByTagNameAsync(templateImageName);
                if (targetImage == null)
                {
                    Log.LogError("Image not found: '{TemplateImageName}'.", templateImageName);

                    return Result.Failed();
                }

                Log.LogDebug("Template image Id is '{TemplateImageId}'.", targetImage.ID);

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

                CreateContainerResponse newContainer = await client.Containers.CreateContainerAsync(createParameters);

                string containerId = newContainer.ID;
                Log.LogInformation("Created container '{ContainerId}'.", containerId);

                await client.Containers.StartContainerAsync(containerId, new HostConfig());
                Log.LogInformation("Started container: '{ContainerId}'.", containerId);

                Log.LogInformation("Waiting for events");
                ContainerEventsParameters eventsParameters = new ContainerEventsParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["container"] = new Dictionary<string, bool>
                        {
                            [newContainer.ID] = true
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
                        Log.LogDebug("Status-change event for container '{ContainerId}': {ContainerStatus}", containerId, containerStatus);

                        if (containerStatus == "die")
                            break;

                        line = eventReader.ReadLine();
                    }
                }
                Log.LogInformation("End of events");

                string deploymentLog;
                Log.LogDebug("Reading logs for container {ContainerId}.", containerId);
                ContainerLogsParameters logParameters = new ContainerLogsParameters
                {
                    ShowStdout = true,
                    ShowStderr = true,
                    Follow = false
                };
                using (Stream logStream = await client.Containers.GetContainerLogsAsync(containerId, logParameters, CancellationToken.None))
                using (StreamReader logReader = new StreamReader(logStream))
                {
                    deploymentLog = logReader.ReadToEnd();
                    Log.LogDebug("Log for container {ContainerId}:\n{ContainerLogEntries}", containerId, deploymentLog);
                }
                
                await client.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters
                {
                    Force = true
                });

                return new Result(
                    succeeded: true,
                    outputs: ReadOutputs(stateDirectory),
                    log: deploymentLog
                );
            }
            catch (Exception unexpectedError)
            {
                Log.LogError("Unexpected error while executing deployment: {Error}.", unexpectedError);

                return Result.Failed();
            }
        }

        /// <summary>
        ///     Write Terraform variables to tfvars.json in the specified state directory.
        /// </summary>
        /// <param name="variables">
        ///     A dictionary containing the variables to write.
        /// </param>
        /// <param name="stateDirectory">
        ///     The state directory.
        /// </param>
        public void WriteVariables(IDictionary<string, object> variables, DirectoryInfo stateDirectory)
        {
            if (variables == null)
                throw new ArgumentNullException(nameof(variables));

            if (stateDirectory == null)
                throw new ArgumentNullException(nameof(stateDirectory));
            
            FileInfo variablesFile = new FileInfo(
                Path.Combine(stateDirectory.FullName, "tfvars.json")
            );
            if (variablesFile.Exists)
                variablesFile.Delete();

            using (StreamWriter writer = variablesFile.CreateText())
            {
                JsonSerializer serializer = new JsonSerializer();
                serializer.Serialize(writer, variables);
            }
        }

        /// <summary>
        ///     Read Terraform outputs from terraform.output.json (if present) in the specified state directory.
        /// </summary>
        /// <param name="variables">
        ///     A dictionary containing the variables to write.
        /// </param>
        /// <param name="stateDirectory">
        ///     The state directory.
        /// </param>
        /// <returns> 
        ///     A <see cref="JObject"/> representing the outputs, or <c>null</c>, if terraform.output.json does not exist in the state directory. 
        /// </returns>
        JObject ReadOutputs(DirectoryInfo stateDirectory)
        {
            if (stateDirectory == null)
                throw new ArgumentNullException(nameof(stateDirectory));
            
            FileInfo outputsFile = new FileInfo(
                Path.Combine(stateDirectory.FullName, "terraform.output.json")
            );
            if (!outputsFile.Exists)
                return new JObject();

            using (StreamReader reader = outputsFile.OpenText())
            using (JsonReader jsonReader = new JsonTextReader(reader))
            {
                JsonSerializer serializer = new JsonSerializer();
                
                return serializer.Deserialize<JObject>(jsonReader);
            }
        }

        /// <summary>
        ///     Represents the result of an <see cref="Executor"/> deployment run.
        /// </summary>
        public sealed class Result
        {
            /// <summary>
            ///     Create a new <see cref="Executor"/> <see cref="Result"/>.
            /// </summary>
            /// <param name="succeeded">
            ///     Did execution succeed?
            /// </param>
            /// <param name="outputs">
            ///     JSON representing outputs from Terraform.
            /// </param>
            /// <param name="log">
            ///     The execution log.
            /// </param>
            public Result(bool succeeded, JObject outputs, string log)
            {
                Succeeded = succeeded;
                Outputs = outputs ?? new JObject();
                Log = log ?? String.Empty;
            }

            /// <summary>
            ///     Did deployment succeed?
            /// </summary>
            public bool Succeeded { get; }

            /// <summary>
            ///     JSON representing outputs from Terraform.
            /// </summary>
            public JObject Outputs { get; }

            /// <summary>
            ///     The deployment output.
            /// </summary>
            /// <returns></returns>
            public string Log { get; }

            /// <summary>
            ///     Create a <see cref="Result"/> representing a failed deployment. 
            /// </summary>
            /// <returns>
            ///     The new <see cref="Result"/>.
            /// </returns>
            /// <param name="log">
            ///     The execution log (if any).
            /// </param>
            public static Result Failed(string log = null)
            {
                return new Result(
                    succeeded: false,
                    outputs: null,
                    log: log
                );
            }
        }
    }
}