using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Client
{
    public class ClientObject
    {
        public string Username { get; set; }
        public string Pass { get; set; }
        public TcpClient TcpClient { get; set; }
        public NetworkStream Stream { get; set; }
        public byte[] Buffer { get; set; }
        public StringBuilder Data { get; set; }
        public EventWaitHandle Handler { get; set; }
    };
}
