using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DD.Research.DockerExecutor.Api.Models
{
    public class DeploymentResultModel
    {
        public bool Success { get; set; }
        
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Reuse)]
        public List<DeploymentLogModel> Logs { get; } = new List<DeploymentLogModel>();
        
        public JObject Outputs { get; set; }
    }
}