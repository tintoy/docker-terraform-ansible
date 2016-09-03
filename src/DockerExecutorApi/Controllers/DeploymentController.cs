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
        public async Task<IActionResult> DeployTemplate([FromBody] DeploymentModel model)
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

            Executor.Result deploymentResult = await _executor.ExecuteAsync(template.ImageName, model.Parameters);

            DeploymentResultModel resultModel = new DeploymentResultModel
            {
                Success = deploymentResult.Succeeded,
                Logs =
                {
                    new DeploymentLogModel
                    {
                        LogFile = "Container.log",
                        LogContent = deploymentResult.ContainerLog
                    }
                },
                Outputs = deploymentResult.Outputs
            };
            resultModel.Logs.AddRange(deploymentResult.DeploymentLogs);

            return Ok(resultModel);
        }
    }
}
