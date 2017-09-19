using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text.RegularExpressions;

namespace TCP_Bridge_Server
{
    public class Program
    {
        public const int BufferSize = 1024;
        public static Dictionary<int, TCPBridgeClient> Clients = new Dictionary<int, TCPBridgeClient>();
        public static int ListenPort;
        public static int ConnectPort;
        public static string ConnectHost;
        public static TcpListener Server;

        public static void Main(string[] args)
        {
            Console.Write("Listen port:");
            ListenPort = Convert.ToInt32(Console.ReadLine());
            //ListenPort = 3333;
            Console.Write("Connect to:");
            var url = Console.ReadLine();
            //var url = "127.0.0.1:12345";
            var reg = new Regex(@"([a-zA-Z0-9.]+):([0-9]+)");
            var match = reg.Match(url);
            if (match == null || match.Groups.Count < 3)
                throw new Exception("Params error.");
            ConnectHost = match.Groups[1].ToString();
            ConnectPort = Convert.ToInt32(match.Groups[2].ToString());
            Server = new TcpListener(IPAddress.Any, ListenPort);
            Server.Start();
            while (true)
            {
                var client = Server.AcceptTcpClient();
                Thread t = new Thread(() =>
                  {
                      HandleClient(client);
                  });
                t.Start();
            }
        }


        public static byte[] Int32ToBytes(int num)
        {
            var data = new byte[4];
            data[0] = (byte)(num & 0xFF);
            data[1] = (byte)((num & 0xFF00) >> 8);
            data[2] = (byte)((num & 0xFF0000) >> 16);
            data[3] = (byte)((num & 0xFF000000) >> 24);
            return data;
        }

        public static int BytesToInt32(byte[] data)
        {
            int num = data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
            return num;
        }

        static void ReadData(NetworkStream ns, byte[] buffer, int offset, int size)
        {
            var i = offset;
            var l = 0;
            while (l < size)
            {
                var x = ns.Read(buffer, i, size - l);
                i += x;
                l += x;
            }
        }

        public static void HandleClient(TcpClient bridgeTcp)
        {
            var numBuffer = new byte[4];
            var bridgeStream = bridgeTcp.GetStream();
            ReadData(bridgeStream, numBuffer, 0, 4);
            var id = BytesToInt32(numBuffer);
            TCPBridgeClient client;
            if (id == 0)
            {
                client = new TCPBridgeClient();
                id = (int)(DateTime.Now.Ticks & 0xFFFFFFF);
                while (Clients.Keys.Contains(id))
                {
                    id += 1;
                }
                bridgeStream.Write(Int32ToBytes(id), 0, 4);
                client.ID = id;
                Clients[id] = client;
                client.Server = new TcpClient();
                client.Server.Connect(ConnectHost, ConnectPort);
            }
            else
            {
                client = Clients[id];
                client.Bridge = bridgeTcp;
                bridgeStream.Write(Int32ToBytes(0), 0, 4);
            }
            var serverStream = client.Server.GetStream();
            client.Reset = false;
            Action receive = () =>
            {
                try
                {

                    while (true)
                    {
                        //reset
                        client.ReceiveBuffer = new byte[BufferSize];
                        client.ReceiveSize = 0;

                        numBuffer = new byte[4];
                        // Get the size of the data sent from bridge.
                        ReadData(bridgeStream, numBuffer, 0, 4);
                        client.ReceiveSize = BytesToInt32(numBuffer);
                        // Get the data from bridge.
                        ReadData(bridgeStream, client.ReceiveBuffer, 0, client.ReceiveSize);
                        // Send the data to the target.
                        serverStream.Write(client.ReceiveBuffer, 0, client.ReceiveSize);
                        // reset
                        client.ReceiveBuffer = new byte[BufferSize];
                        client.ReceiveSize = 0;
                    }
                }
                catch (Exception)
                {
                    client.Reset = true;
                }
            };
            Action send = () =>
            {
                try
                {
                    // Resend the data.
                    if (client.SendSize > 0)
                    {
                        bridgeStream.Write(Int32ToBytes(client.SendSize), 0, 4);
                        bridgeStream.Write(client.SendBuffer, 0, client.SendSize);
                    }
                    while (true)
                    {
                        // reset
                        client.SendBuffer = new byte[BufferSize];
                        client.SendSize = 0;
                        // Get the data from the target.
                        client.SendSize = serverStream.Read(client.SendBuffer, 0, BufferSize);
                        // Send the size of the data.
                        bridgeStream.Write(Int32ToBytes(client.SendSize), 0, 4);
                        // Send the data.
                        bridgeStream.Write(client.SendBuffer, 0, client.SendSize);

                        // reset
                        client.SendBuffer = new byte[BufferSize];
                        client.SendSize = 0;
                    }
                }
                catch (Exception)
                {
                    client.Reset = true;

                }
            };
            while (true)
            {
                client.Reset = false;
                client.ReceiveThread = new Thread(new ThreadStart(receive));
                client.SendThread = new Thread(new ThreadStart(send));
                client.ReceiveThread.Start();
                client.SendThread.Start();
                while (!client.Reset)
                {
                    Thread.Sleep(500);
                }
                client.Bridge.Close();
            }
        }
    }
}
