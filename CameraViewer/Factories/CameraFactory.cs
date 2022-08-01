using System.Net;
using ClientMJPEG;

namespace CameraViewer.Factories
{
    public interface IImageCreatorFactory
    {
        public ImageCreator GetCreator(IPAddress ipAddress, int port, string path);
    }

    public class ImageCreatorFactory : IImageCreatorFactory
    {
        public ImageCreator GetCreator(IPAddress ipAddress, int port, string path)
        {
            return new (new IPEndPoint(ipAddress, port), path);
        }
    }
}