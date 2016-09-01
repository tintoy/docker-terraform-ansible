using Docker.DotNet;
using Docker.DotNet.Models;
using System;
using System.Collections.Generic;
using System.Linq;
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
    }
}
