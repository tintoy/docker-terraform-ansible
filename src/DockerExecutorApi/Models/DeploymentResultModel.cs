using Newtonsoft.Json.Linq;

namespace DD.Research.DockerExecutor.Api.Models
{
    public class DeploymentResultModel
    {
        public bool Success { get; set; }
        public string Log { get; set; }
        public JObject Outputs { get; set; }
    }
}