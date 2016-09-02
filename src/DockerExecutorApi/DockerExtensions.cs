using Docker.DotNet;
using Docker.DotNet.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DD.Research.DockerExecutor.Api
{
    public static class DockerExtensions
    {
        public static async Task<ImagesListResponse> FindImageByTagNameAsync(this IImageOperations imageOperations, string tagName)
        {
            if (imageOperations == null)
                throw new ArgumentNullException(nameof(imageOperations));

            IList<ImagesListResponse> images = await imageOperations.ListImagesAsync(
                new ImagesListParameters()
            );

            return images.FirstOrDefault(image =>
                image.RepoTags != null
                &&
                image.RepoTags.Contains("template/do-docker:latest")
            );
        }

        public static async Task<bool> WaitForContainerTerminationAsync(this IContainerOperations containerOperations, string containerId)
        {
            TimeSpan timeout = TimeSpan.FromMinutes(30);
            DateTime then = DateTime.Now;
            DateTime now = DateTime.Now;
            while ((now - then) < timeout)
            {
                ContainerInspectResponse containerState = await containerOperations.InspectContainerAsync(containerId);
                if (containerState.State.Dead)
                    return true;

                await Task.Delay(
                    TimeSpan.FromSeconds(2)
                );

                now = DateTime.Now;
            }

            return false;
        }

        public static async Task<string> GetEntireContainerLogAsync(this IContainerOperations containerOperations, string containerId)
        {
            ContainerLogsParameters logParameters = new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Follow = false
            };
            using (Stream logStream = await containerOperations.GetContainerLogsAsync(containerId, logParameters, CancellationToken.None))
            using (StreamReader logReader = new StreamReader(logStream))
            {
                return logReader.ReadToEnd() ?? String.Empty;
            }
        }
    }
}
