using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DD.Research.DockerExecutor.Api.Models
{
    /// <summary>
    ///     The represents a deployment.
    /// </summary>
    public class DeploymentModel
    {
        /// <summary>
        ///     The deployment Id.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        ///     The current deployment state.
        /// </summary>
        public DeploymentState State { get; set; }

        /// <summary>
        ///     Is the deployment complete?
        /// </summary>
        public bool IsComplete => State == DeploymentState.Successful || State == DeploymentState.Failed;

        /// <summary>
        ///     The deployment logs (once the deployment is complete).
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Reuse)]
        public List<DeploymentLogModel> Logs { get; } = new List<DeploymentLogModel>();
        
        /// <summary>
        ///     The deployment outputs (once the deployment is complete).
        /// </summary>
        public JObject Outputs { get; set; }
    }
}