using Docker.DotNet;
using System;
using System.Threading;
using Docker.DotNet.Models;

namespace ConsoleApplication
{
    public class Program
    {
        public static void Main(string[] args)
        {
            SynchronizationContext.SetSynchronizationContext(
                new SynchronizationContext()
            );
            try
            {
                DockerClientConfiguration config = new DockerClientConfiguration(
                    new Uri("unix:///var/run/docker.sock")
                );
                DockerClient client = config.CreateClient();

                var images = client.Images.ListImagesAsync(new ImagesListParameters()).Result;
                foreach (var image in images)
                {
                    Console.WriteLine(image.ID);
                }
            }
            catch (Exception unexpectedError)
            {
                Console.WriteLine(unexpectedError);
            }
        }
    }
}
