using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;

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
        ///     The deployment service.
        /// </summary>
        readonly Deployer _deployer;

        /// <summary>
        ///     Create a new <see cref="DeploymentsController"/>.
        /// </summary>
        /// <param name="deployer">
        ///     The deployment service.
        /// </param>
        public DeploymentsController(Deployer deployer)
        {
            if (deployer == null)
                throw new ArgumentNullException(nameof(deployer));

            _deployer = deployer;
        }

        /// <summary>
        ///     List all deployments.
        /// </summary>
        /// <returns>
        ///     A list of deployments.
        /// </returns>
        [HttpGet("")]
        public async Task<IActionResult> ListDeployments()
        {
            DeploymentModel[] deployments = await _deployer.GetDeploymentsAsync();

            return Ok(deployments);
        }

        /// <summary>
        ///     Get a specific deployment.
        /// </summary>
        /// <param name="deploymentId">
        ///     The deployment Id.
        /// </param>
        /// <returns>
        ///     The deployment.
        /// </returns>
        [HttpGet("{deploymentId}")]
        public async Task<IActionResult> GetDeployment(string deploymentId)
        {
            DeploymentModel deployment = await _deployer.GetDeploymentAsync(deploymentId);
            if (deployment == null)
            {
                Response.Headers.Add("ErrorCode", "DeploymentNotFound");

                return NotFound(new
                {
                    ErrorCode = "DeploymentNotFound",
                    Message = $"No deployment was found with Id '{deploymentId}'."
                });
            }

            return Ok(deployment);
        }

        /// <summary>
        ///     Deploy a template.
        /// </summary>
        /// <returns>
        ///     The deployment result.
        /// </returns>
        [HttpPost("")]
        public async Task<IActionResult> DeployTemplate([FromBody] CreateDeploymentModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            TemplateModel template = DummyData.DeploymentTemplates.FirstOrDefault(
                deploymentTemplate => deploymentTemplate.Id == model.TemplateId
            );
            if (template == null)
            {
                return NotFound(new
                {
                    ErrorCode = "TemplateNotFound",
                    Message = $"Template {model.TemplateId} not found."
                });
            }

            string deploymentId = HttpContext.TraceIdentifier;
            bool started = await _deployer.DeployAsync(deploymentId, template.ImageName, model.Parameters);

            return Ok(new
            {
                Started = started,
                DeploymentId = deploymentId
            });
        }
    }
}
