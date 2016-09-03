using Newtonsoft.Json;
using System.Collections.Generic;

namespace DD.Research.DockerExecutor.Api.Models
{
    /// <summary>
    ///     The model for initiating a deployment.
    /// </summary>
    public class DeploymentModel
    {
        /// <summary>
        ///     The Id of the template to deploy.
        /// </summary>
        public int TemplateId { get; set; }

        /// <summary>
        ///     The Id of the template to deploy.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Reuse)]
        public Dictionary<string, string> Parameters { get; } = new Dictionary<string, string>();
    }
}