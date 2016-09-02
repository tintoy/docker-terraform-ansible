using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace DD.Research.DockerExecutor.Api.Controllers
{
    using Models;

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
        [Route("")]
        public IActionResult ListTemplates()
        {
            return Ok(DummyData.DeploymentTemplates);
        }
    }
}
