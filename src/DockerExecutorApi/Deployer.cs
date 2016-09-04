using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace DD.Research.DockerExecutor.Api
{
    using Models;

    using FiltersDictionary = Dictionary<string, IDictionary<string, bool>>;
    using FilterDictionary = Dictionary<string, bool>;

    /// <summary>
    ///     The executor for deployment jobs via Docker.
    /// </summary>
    public class Deployer
    {
        /// <summary>
        ///     Create a new <see cref="Deployer"/>.
        /// </summary>
        /// <param name="deployerOptions">
        ///     The deployer options.
        /// </summary>
        /// <param name="logger">
        ///     The deployer logger.
        /// </summary>
        public Deployer(IOptions<DeployerOptions> deployerOptions, ILogger<Deployer> logger)
        {
            if (deployerOptions == null)
                throw new ArgumentNullException(nameof(deployerOptions));

            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            DeployerOptions options = deployerOptions.Value;
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
        public DirectoryInfo LocalStateDirectory { get; }

        /// <summary>
        ///     The host directory corresponding to the local state directory.
        /// </summary>
        public DirectoryInfo HostStateDirectory { get; }

        /// <summary>
        ///     The executor logger.
        /// </summary>
        ILogger Log { get; }

        /// <summary>
        ///     The Docker API client.
        /// </summary>
        DockerClient Client { get; }

        /// <summary>
        ///     Get all deployments.
        /// </summary>
        /// <returns>
        ///     A list of deployments.
        /// </returns>
        public async Task<DeploymentModel[]> GetDeploymentsAsync()
        {
            List<DeploymentModel> deployments = new List<DeploymentModel>();

            Log.LogInformation("Retrieving all deployments...");

            // Find all containers that have a "deployment.id" label.
            ContainersListParameters listParameters = new ContainersListParameters
            {
                All = true,
                Filters = new FiltersDictionary
                {
                    ["label"] = new FilterDictionary
                    {
                        ["deployment.id"] = true
                    }
                }
            };
            IList<ContainerListResponse> containerListings = await Client.Containers.ListContainersAsync(listParameters);

            deployments.AddRange(containerListings.Select(
                containerListing => ToDeploymentModel(containerListing)
            ));
            
            Log.LogInformation("Retrieved {DeploymentCount} deployments.", deployments.Count);

            return deployments.ToArray();
        }

        /// <summary>
        ///     Get a specific deployment by Id.
        /// </summary>
        /// <param name="deploymentId">
        ///     The deployment Id.
        /// </param>
        /// <returns>
        ///     The deployment, or <c>null</c> if one was not found with the specified Id.
        /// </returns>
        public async Task<DeploymentModel> GetDeploymentAsync(string deploymentId)
        {
            if (String.IsNullOrWhiteSpace(deploymentId))
                throw new ArgumentException("Invalid deployment Id.", nameof(deploymentId));

            Log.LogInformation("Retrieving deployment '{DeploymentId}'...", deploymentId);

            // Find all containers that have a "deployment.id" label.
            ContainersListParameters listParameters = new ContainersListParameters
            {
                All = true,
                Filters = new FiltersDictionary
                {
                    ["label"] = new FilterDictionary
                    {
                        ["deployment.id=" + deploymentId] = true
                    }
                }
            };
            IList<ContainerListResponse> containerListings = await Client.Containers.ListContainersAsync(listParameters);

            ContainerListResponse matchingContainer = containerListings.FirstOrDefault();
            if (matchingContainer == null)
            {
                Log.LogInformation("Deployment '{DeploymentId}' not found.", deploymentId);

                return null;
            }
            
            DeploymentModel deployment = ToDeploymentModel(matchingContainer);

            Log.LogInformation("Retrieved deployment '{DeploymentId}'.", deploymentId);

            return deployment;
        }

        /// <summary>
        ///     Execute a deployment.
        /// </summary>
        /// <param name="deploymentId">
        ///     A unique identifier for the deployment.
        /// </param>
        /// <param name="templateImageTag">
        ///     The tag of the Docker image that implements the deployment template.
        /// </param>
        /// <param name="templateParameters">
        ///     A dictionary containing global template parameters to be written to the state directory.
        /// </param>
        /// <param name="stateDirectory">
        ///     The state directory to be mounted in the deployment container.
        /// </param>
        /// <returns>
        ///     <c>true</c>, if the deployment was started; otherwise, <c>false</c>.
        /// 
        ///     TODO: Consider returning an enum instead.
        /// </returns>
        public async Task<bool> DeployAsync(string deploymentId, string templateImageTag, IDictionary<string, string> templateParameters)
        {
            if (String.IsNullOrWhiteSpace(templateImageTag))
                throw new ArgumentException("Must supply a valid template image name.", nameof(templateImageTag));

            if (templateParameters == null)
                throw new ArgumentNullException(nameof(templateParameters));

            try
            {
                Log.LogInformation("Starting deployment '{DeploymentId}' using image '{ImageTag}'...", deploymentId, templateImageTag);

                DirectoryInfo deploymentLocalStateDirectory = GetLocalStateDirectory(deploymentId);
                DirectoryInfo deploymentHostStateDirectory = GetHostStateDirectory(deploymentId);

                Log.LogInformation("Local state directory for deployment '{DeploymentId}' is '{LocalStateDirectory}'.", deploymentId, deploymentLocalStateDirectory.FullName);
                Log.LogInformation("Host state directory for deployment '{DeploymentId}' is '{LocalStateDirectory}'.", deploymentId, deploymentHostStateDirectory.FullName);
                
                WriteTemplateParameters(templateParameters, deploymentLocalStateDirectory);

                ImagesListResponse targetImage = await Client.Images.FindImageByTagNameAsync(templateImageTag);
                if (targetImage == null)
                {
                    Log.LogError("Image not found: '{TemplateImageName}'.", templateImageTag);

                    return false;
                }

                Log.LogInformation("Template image Id is '{TemplateImageId}'.", targetImage.ID);

                CreateContainerParameters createParameters = new CreateContainerParameters
                {
                    Name = "deploy-" + deploymentId,
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
                    },
                    Labels = new Dictionary<string, string>
                    {
                        ["task.type"] = "deployment",
                        ["deployment.id"] = deploymentId,
                        ["deployment.image.tag"] = templateImageTag
                    }
                };

                CreateContainerResponse newContainer = await Client.Containers.CreateContainerAsync(createParameters);

                string containerId = newContainer.ID;
                Log.LogInformation("Created container '{ContainerId}'.", containerId);

                await Client.Containers.StartContainerAsync(containerId, new HostConfig());
                Log.LogInformation("Started container: '{ContainerId}'.", containerId);

                return true;
            }
            catch (Exception unexpectedError)
            {
                Log.LogError("Unexpected error while executing deployment '{DeploymentId}': {Error}", deploymentId, unexpectedError);

                return false;
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
        DirectoryInfo GetLocalStateDirectory(string deploymentId)
        {
            DirectoryInfo stateDirectory = new DirectoryInfo(Path.Combine(
                LocalStateDirectory.FullName,
                deploymentId
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
        DirectoryInfo GetHostStateDirectory(string deploymentId)
        {
            DirectoryInfo stateDirectory = new DirectoryInfo(Path.Combine(
                HostStateDirectory.FullName,
                deploymentId
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
        void WriteTemplateParameters(IDictionary<string, string> variables, DirectoryInfo stateDirectory)
        {
            if (variables == null)
                throw new ArgumentNullException(nameof(variables));

            if (stateDirectory == null)
                throw new ArgumentNullException(nameof(stateDirectory));
            
            FileInfo variablesFile = GetTerraformVariableFile(stateDirectory);
            Log.LogInformation("Writing {TemplateParameterCount} parameters to '{TerraformVariableFile}'...",
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

            Log.LogInformation("Wrote {TemplateParameterCount} parameters to '{TerraformVariableFile}'.",
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
            Log.LogInformation("Reading Terraform outputs from '{TerraformOutputsFile}'...",
                outputsFile.FullName
            );
            
            if (!outputsFile.Exists)
            {
                Log.LogInformation("Terraform outputs file '{TerraformOutputsFile}' does not exist.", outputsFile.FullName);

                return new JObject();
            }

            JObject outputs;
            using (StreamReader reader = outputsFile.OpenText())
            using (JsonReader jsonReader = new JsonTextReader(reader))
            {
                JsonSerializer serializer = new JsonSerializer();
                
                outputs = serializer.Deserialize<JObject>(jsonReader);
            }

            Log.LogInformation("Read {TemplateParameterCount} Terraform outputs from '{TerraformVariableFile}'.",
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
        ///     Retrieve all deployment logs from the state directory.
        /// </summary>
        /// <param name="stateDirectory">
        ///     The state directory for a deployment.
        /// </param>
        /// <returns>
        ///     A sequence of <see cref="DeploymentLogModel"/>s.
        /// </returns>
        IEnumerable<DeploymentLogModel> ReadDeploymentLogs(DirectoryInfo stateDirectory)
        {
            if (stateDirectory == null)
                throw new ArgumentNullException(nameof(stateDirectory));

            DirectoryInfo logsDirectory = new DirectoryInfo(
                Path.Combine(stateDirectory.FullName, "logs")
            );
            if (!logsDirectory.Exists)
                yield break;

            // Get the log files in the order they were written.
            FileInfo[] logFilesByTimestamp =
                logsDirectory.EnumerateFiles("*.log")
                    .OrderBy(logFile => logFile.LastWriteTime)
                    .ToArray();

            foreach (FileInfo logFile in logFilesByTimestamp)
            {
                Log.LogInformation("Reading deployment log 'LogFile'...", logFile.FullName);
                using (StreamReader logReader = logFile.OpenText())
                {
                    yield return new DeploymentLogModel
                    {
                        LogFile = logFile.Name,
                        LogContent = logReader.ReadToEnd()
                    };
                }
                Log.LogInformation("Read deployment log 'LogFile'.", logFile.FullName);
            }
        }

        /// <summary>
        ///     Convert a <see cref="ContainerListResponse">container listing</see> to a <see cref="DeploymentModel"/>.
        /// </summary>
        /// <param name="containerListing">
        ///     The <see cref="ContainerListResponse">container listing</see> to convert.
        /// </param>
        /// <returns>
        ///     The converted <see cref="DeploymentModel"/>.
        /// </returns>
        DeploymentModel ToDeploymentModel(ContainerListResponse containerListing)
        {
            if (containerListing == null)
                throw new ArgumentNullException(nameof(containerListing));

            string deploymentId = containerListing.Labels["deployment.id"];
            DirectoryInfo deploymentStateDirectory = GetLocalStateDirectory(deploymentId);

            DeploymentModel deployment = new DeploymentModel
            {
                Id = deploymentId
            };

            switch (containerListing.State)
            {
                case "running":
                {
                    deployment.State = DeploymentState.Running;

                    break;
                }
                case "exited":
                {
                    if (containerListing.Status == "Exit 0")
                        deployment.State = DeploymentState.Successful;
                    else
                        deployment.State = DeploymentState.Failed;

                    deployment.Logs.AddRange(
                        ReadDeploymentLogs(deploymentStateDirectory)
                    );
                    deployment.Outputs = ReadOutputs(deploymentStateDirectory);

                    break;
                }
                default:
                {
                    Log.LogInformation("Unexpected container state '{State}'.", containerListing.State);

                    deployment.State = DeploymentState.Unknown;

                    break;
                }
            }

            return deployment;
        }

        /// <summary>
        ///     Represents the result of an <see cref="Deployer"/> deployment run.
        /// </summary>
        public sealed class Result
        {
            /// <summary>
            ///     Create a new <see cref="Deployer"/> <see cref="Result"/>.
            /// </summary>
            /// <param name="succeeded">
            ///     Did execution succeed?
            /// </param>
            /// <param name="outputs">
            ///     JSON representing outputs from Terraform.
            /// </param>
            /// <param name="containerLog">
            ///     The container log.
            /// </param>
            public Result(bool succeeded, JObject outputs, string containerLog, IEnumerable<DeploymentLogModel> deploymentLogs)
            {
                Succeeded = succeeded;
                Outputs = outputs ?? new JObject();
                ContainerLog = containerLog ?? String.Empty;
                DeploymentLogs = (deploymentLogs ?? Enumerable.Empty<DeploymentLogModel>()).ToArray();
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
            ///     The container log.
            /// </summary>
            public string ContainerLog { get; }

            /// <summary>
            ///     The deployment log.
            /// </summary>
            public DeploymentLogModel[] DeploymentLogs { get; }

            /// <summary>
            ///     Create a <see cref="Result"/> representing a failed deployment. 
            /// </summary>
            /// <returns>
            ///     The new <see cref="Result"/>.
            /// </returns>
            /// <param name="containerLog">
            ///     The container log (if any).
            /// </param>
            /// <param name="deploymentLogs">
            ///     The deployment logs (if any).
            /// </param>
            public static Result Failed(string containerLog = null, IEnumerable<DeploymentLogModel> deploymentLogs = null)
            {
                return new Result(
                    succeeded: false,
                    outputs: null,
                    containerLog: containerLog,
                    deploymentLogs: deploymentLogs
                );
            }
        }
    }
}