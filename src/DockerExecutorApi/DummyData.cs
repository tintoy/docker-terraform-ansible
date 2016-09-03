
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace DD.Research.DockerExecutor.Api
{
    using Models;

    /// <summary>
    ///     Dummy data used by the executor API (someday this will come from a database).
    /// </summary>
    public static class DummyData
    {
        /// <summary>
        ///     Well-known deployment templates.     
        /// </summary>
        public static readonly List<TemplateModel> DeploymentTemplates = new List<TemplateModel>
        {
            new TemplateModel
            {
                Id = 1,
                Name = "Web application (multi-cloud)",
                ImageName = "tintoy/tfa-multicloud-template:stable",
                Parameters =
                {
                    new TemplateParameterModel
                    {
                        Name = "app_name",
                        Type = JTokenType.String,
                        Description = "The application name (used as a prefix for resource names."
                    },
                    new TemplateParameterModel
                    {
                        Name = "aws_instance_count",
                        Type = JTokenType.Integer,
                        Description = "The number of AWS EC2 instances to create."
                    }
                }
            }
        };
    }
}