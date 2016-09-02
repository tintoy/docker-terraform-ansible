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
using Microsoft.Extensions.Options;

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
        /// <param name="executorOptions">
        ///     The executor options.
        /// </summary>
        /// <param name="logger">
        ///     The executor logger.
        /// </summary>
        public Executor(IOptions<ExecutorOptions> executorOptions, ILogger<Executor> logger)
        {
            if (executorOptions == null)
                throw new ArgumentNullException(nameof(executorOptions));

            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            ExecutorOptions options = executorOptions.Value;
            LocalStateDirectory = new DirectoryInfo(Path.GetFullPath(
                Path.Combine(Directory.GetCurrentDirectory(), options.LocalStateDirectory)
            ));
            HostStateDirectory = new DirectoryInfo(Path.GetFullPath(
                Path.Combine(Directory.GetCurrentDirectory(), options.HostStateDirectory)
            ));

            Log = logger;

            DockerClientConfiguration config = new DockerClientConfiguration(
                new Uri("unix:///var/run/docker.sock")
            );
            Client = config.CreateClient();
        }

        /// <summary>
        ///     The local directory whose state sub-directories represent the state for deployment containers.
        /// </summary>
        DirectoryInfo LocalStateDirectory { get; }

        /// <summary>
        ///     The host directory corresponding to the local state directory.
        /// </summary>
        DirectoryInfo HostStateDirectory { get; }

        /// <summary>
        ///     The executor logger.
        /// </summary>
        ILogger Log { get; }

        /// <summary>
        ///     The Docker API client.
        /// </summary>
        DockerClient Client { get; }

        /// <summary>
        ///     Execute a deployment.
        /// </summary>
        /// <param name="templateImageName">
        ///     The name of the Docker image that implements the deployment template.
        /// </param>
        /// <param name="templateParameters">
        ///     A dictionary containing global template parameters to be written to the state directory.
        /// </param>
        /// <param name="stateDirectory">
        ///     The state directory to be mounted in the deployment container.
        /// </param>
        /// <returns>
        ///     <c>true</c>, if the deployment was successful; otherwise, <c>false</c>.
        /// </returns>
        public async Task<Result> ExecuteAsync(string templateImageName, IDictionary<string, object> templateParameters)
        {
            if (String.IsNullOrWhiteSpace(templateImageName))
                throw new ArgumentException("Must supply a valid template image name.", nameof(templateImageName));

            if (templateParameters == null)
                throw new ArgumentNullException(nameof(templateParameters));

            Guid deploymentId = Guid.NewGuid();
            try
            {
                Log.LogInformation("Starting deployment '{DeploymentId}'...", deploymentId);

                DirectoryInfo deploymentLocalStateDirectory = GetLocalStateDirectory(deploymentId);
                DirectoryInfo deploymentHostStateDirectory = GetHostStateDirectory(deploymentId);

                Log.LogDebug("Local state directory for deployment '{DeploymentId}' is '{LocalStateDirectory}'.", deploymentId, deploymentLocalStateDirectory.FullName);
                Log.LogDebug("Host state directory for deployment '{DeploymentId}' is '{LocalStateDirectory}'.", deploymentId, deploymentHostStateDirectory.FullName);
                
                WriteTemplateParameters(templateParameters, deploymentLocalStateDirectory);

                ImagesListResponse targetImage = await Client.Images.FindImageByTagNameAsync(templateImageName);
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
                            $"{deploymentHostStateDirectory.FullName}:/root/state"
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

                CreateContainerResponse newContainer = await Client.Containers.CreateContainerAsync(createParameters);

                string containerId = newContainer.ID;
                Log.LogInformation("Created container '{ContainerId}'.", containerId);

                await Client.Containers.StartContainerAsync(containerId, new HostConfig());
                Log.LogInformation("Started container: '{ContainerId}'.", containerId);

                Log.LogInformation("Waiting for container termination...");
                bool terminated = await Client.Containers.WaitForContainerTerminationAsync(containerId);
                if (!terminated)
                {
                    Log.LogError("Timed out waiting for deployment process '{DeploymentId}' to terminate.", deploymentId);

                    return Result.Failed();
                }

                Log.LogDebug("Reading logs for container {ContainerId}.", containerId);
                string deploymentLog = await Client.Containers.GetEntireContainerLogAsync(containerId);
                Log.LogDebug("Log for container {ContainerId}:\n{ContainerLogEntries}", containerId, deploymentLog);

                Log.LogDebug("Destroying container '{ContainerId}'...", containerId);
                await Client.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters
                {
                    Force = true
                });
                Log.LogDebug("Destroyed container '{ContainerId}'...", containerId);

                return new Result(
                    succeeded: true,
                    outputs: ReadOutputs(deploymentLocalStateDirectory),
                    log: deploymentLog
                );
            }
            catch (Exception unexpectedError)
            {
                Log.LogError("Unexpected error while executing deployment '{DeploymentId}': {Error}", deploymentId, unexpectedError);

                return Result.Failed();
            }
        }

        /// <summary>
        ///     Get the local directory to hold state for the specified deployment.
        /// </summary>
        /// <param name="deploymentId">
        ///     The deployment Id.
        /// </param>
        /// <returns>
        ///     A <see cref="DirectoryInfo"/> representing the state directory.
        /// </returns>
        DirectoryInfo GetLocalStateDirectory(Guid deploymentId)
        {
            DirectoryInfo stateDirectory = new DirectoryInfo(Path.Combine(
                LocalStateDirectory.FullName,
                deploymentId.ToString("N")
            ));
            if (!stateDirectory.Exists)
                stateDirectory.Create();

            return stateDirectory;
        }

        /// <summary>
        ///     Get the host directory that holds state for the specified deployment.
        /// <param name="deploymentId">
        ///     The deployment Id.
        /// </param>
        /// </summary>
        /// <returns>
        ///     A <see cref="DirectoryInfo"/> representing the state directory.
        /// </returns>
        DirectoryInfo GetHostStateDirectory(Guid deploymentId)
        {
            DirectoryInfo stateDirectory = new DirectoryInfo(Path.Combine(
                HostStateDirectory.FullName,
                deploymentId.ToString("N")
            ));
            if (!stateDirectory.Exists)
                stateDirectory.Create();

            return stateDirectory;
        }

        /// <summary>
        ///     Write template parameters to tfvars.json in the specified state directory.
        /// </summary>
        /// <param name="variables">
        ///     A dictionary containing the template parameters to write.
        /// </param>
        /// <param name="stateDirectory">
        ///     The state directory.
        /// </param>
        void WriteTemplateParameters(IDictionary<string, object> variables, DirectoryInfo stateDirectory)
        {
            if (variables == null)
                throw new ArgumentNullException(nameof(variables));

            if (stateDirectory == null)
                throw new ArgumentNullException(nameof(stateDirectory));
            
            FileInfo variablesFile = GetTerraformVariableFile(stateDirectory);
            Log.LogDebug("Writing {TemplateParameterCount} parameters to '{TerraformVariableFile}'...",
                variables.Count,
                variablesFile.FullName
            );

            if (variablesFile.Exists)
                variablesFile.Delete();
            else if (!stateDirectory.Exists)
                stateDirectory.Create();

            using (StreamWriter writer = variablesFile.CreateText())
            {
                JsonSerializer serializer = new JsonSerializer();
                serializer.Serialize(writer, variables);
            }

            Log.LogDebug("Wrote {TemplateParameterCount} parameters to '{TerraformVariableFile}'.",
                variables.Count,
                variablesFile.FullName
            );
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
            
            FileInfo outputsFile = GetTerraformOutputFilePath(stateDirectory);
            Log.LogDebug("Reading Terraform outputs from '{TerraformOutputsFile}'...",
                outputsFile.FullName
            );
            
            if (!outputsFile.Exists)
            {
                Log.LogDebug("Terraform outputs file '{TerraformOutputsFile}' does not exist.", outputsFile.FullName);

                return new JObject();
            }

            JObject outputs;
            using (StreamReader reader = outputsFile.OpenText())
            using (JsonReader jsonReader = new JsonTextReader(reader))
            {
                JsonSerializer serializer = new JsonSerializer();
                
                outputs = serializer.Deserialize<JObject>(jsonReader);
            }

            Log.LogDebug("Read {TemplateParameterCount} Terraform outputs from '{TerraformVariableFile}'.",
                outputs.Count,
                outputsFile.FullName
            );

            return outputs;
        }

        /// <summary>
        ///     Get the Terraform variable file.
        /// </summary>
        /// <param name="stateDirectory">
        ///     The deployment state directory.
        /// </param>
        /// <returns>
        ///     The full path to the file.
        /// </returns>
        static FileInfo GetTerraformVariableFile(DirectoryInfo stateDirectory)
        {
            if (stateDirectory == null)
                throw new ArgumentNullException(nameof(stateDirectory));

            return new FileInfo(
                Path.Combine(stateDirectory.FullName, "tfvars.json")
            );
        }

        /// <summary>
        ///     Get the Terraform outputs file.
        /// </summary>
        /// <param name="stateDirectory">
        ///     The deployment state directory.
        /// </param>
        /// <returns>
        ///     The outputs file.
        /// </returns>
        static FileInfo GetTerraformOutputFilePath(DirectoryInfo stateDirectory)
        {
            if (stateDirectory == null)
                throw new ArgumentNullException(nameof(stateDirectory));

            return new FileInfo(
                Path.Combine(stateDirectory.FullName, "terraform.output.json")
            );
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