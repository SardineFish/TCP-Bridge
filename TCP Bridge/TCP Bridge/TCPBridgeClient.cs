using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TCP_Bridge
{
    public class TCPBridgeClient
    {
        public TcpClient Client;
        public TcpClient Bridge;
        public int ID;
        public Thread HandleThread;
        public Thread ReceiveThread;
        public Thread SendThread;
        public bool Reset = true;
        public byte[] ReceiveBuffer;
        public byte[] SendBuffer;
        public int ReceiveSize = 0;
        public int SendSize = 0;
    }
}
