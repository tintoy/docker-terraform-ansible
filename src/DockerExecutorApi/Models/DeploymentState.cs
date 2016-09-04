namespace DD.Research.DockerExecutor.Api.Models
{
    /// <summary>
    ///     Represents the state of a deployment.
    /// </summary>
    public enum DeploymentState
    {
        Unknown     = 0,
        Running     = 1,
        Successful  = 2,
        Failed      = 3
    }
}