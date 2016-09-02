using Microsoft.AspNetCore.Mvc;

namespace DD.Research.DockerExecutor.Api.Controllers
{
    /// <summary>
    ///     The API controller for deployment templates.
    /// </summary>
    [Route("templates")]
    public class TemplatesController
        : Controller
    {
        /// <summary>
        ///     Retrieve a list of deployment templates.
        /// </summary>
        /// <returns>
        ///     A list of deployment templates.
        /// </returns>
        [HttpGet("")]
        public IActionResult ListTemplates()
        {
            return Ok(DummyData.DeploymentTemplates);
        }
    }
}
