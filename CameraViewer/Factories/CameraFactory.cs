using System.Net;
using ClientMJPEG;

namespace CameraViewer.Factories
{
    public interface IImageCreatorFactory
    {
        public ImageCreator GetCreator(IPAddress ipAddress, int port);
    }

    public class ImageCreatorFactory : IImageCreatorFactory
    {
        public ImageCreator GetCreator(IPAddress ipAddress, int port)
        {
            return new (new IPEndPoint(ipAddress, port));
        }
    }
}