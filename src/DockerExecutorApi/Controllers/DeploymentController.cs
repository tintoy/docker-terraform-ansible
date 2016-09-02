using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System;

namespace DD.Research.DockerExecutor.Api.Controllers
{
    using Models;

    /// <summary>
    ///     The API controller for deployments.
    /// </summary>
    [Route("deployments")]
    public class DeploymentsController
        : ControllerBase
    {
        /// <summary>
        ///     The deployment executor.
        /// </summary>
        readonly Executor _executor;

        /// <summary>
        ///     Create a new <see cref="DeploymentsController"/>.
        /// </summary>
        /// <param name="executor">
        ///     The deployment executor.
        /// </param>
        public DeploymentsController(Executor executor)
        {
            if (executor == null)
                throw new ArgumentNullException(nameof(executor));

            _executor = executor;
        }

        /// <summary>
        ///     Deploy a template.
        /// </summary>
        /// <returns>
        ///     The deployment result.
        /// </returns>
        [HttpPost("")]
        public IActionResult DeployTemplate([FromBody] DeploymentModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            return Ok(new Models.DeploymentResultModel
            {
                Success = true,
                Log = "Deployment log goes here.\nIt is multi-line.",
                Outputs = JObject.Parse(@"
                    {
                        ""aws_hosts"": {
                            ""sensitive"": false,
                            ""type"": ""list"",
                            ""value"": [
                                ""demo-aws-web-01"",
                                ""demo-aws-web-02""
                            ]
                        },
                        ""aws_private_ips"": {
                            ""sensitive"": false,
                            ""type"": ""list"",
                            ""value"": [
                                ""172.31.40.126"",
                                ""172.31.38.177""
                            ]
                        },
                        ""aws_public_ips"": {
                            ""sensitive"": false,
                            ""type"": ""list"",
                            ""value"": [
                                """",
                                """"
                            ]
                        }
                    }
                ")
            });
        }
    }
}
