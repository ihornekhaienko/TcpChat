using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Server
{
    public class ClientObject
    {
        public long Id { get; set; }
        public string Username { get; set; }
        public TcpClient TcpClient { get; set; }
        public NetworkStream Stream { get; set; }
        public byte[] Buffer { get; set; }
        public StringBuilder Data { get; set; }
        public EventWaitHandle Handler { get; set; }
    };
}