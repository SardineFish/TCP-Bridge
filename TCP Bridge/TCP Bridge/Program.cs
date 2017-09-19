using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.IO;

namespace TCP_Bridge
{
    class Program
    {
        public const int BufferSize = 1024;
        public static string ServerHost;
        public static int ServerPort;
        public static int LocalPort;
        static List<TCPBridgeClient> Clients = new List<TCPBridgeClient>();
        static TcpListener Server;
        static Dictionary<string, Action<string>> Commands = new Dictionary<string, Action<string>>();
        static Thread ServerThread;
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
        static void Connect(string host,int port)
        {
            ServerHost = host;
            ServerPort = port;
        }
        static void Setup(int port)
        {
            ServerThread = new Thread(() =>
            {
                Server = new TcpListener(IPAddress.Parse("127.0.0.1"), port);
                Server.Start();
                Console.WriteLine("  Server started.");
                Console.WriteLine("  Listening " + port.ToString());
                while (true)
                {
                    var client = Server.AcceptTcpClient();
                    var tcpBridgeClient = new TCPBridgeClient();
                    tcpBridgeClient.Client = client;
                    Clients.Add(tcpBridgeClient);
                    tcpBridgeClient.HandleThread = new Thread(() =>
                    {
                        HandleClient(tcpBridgeClient);
                    });
                    tcpBridgeClient.HandleThread.Start();
                }
            });
            ServerThread.Start();
        }
        static void ReadData(NetworkStream ns,byte[] buffer,int offset,int size)
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
        static void HandleClient(TCPBridgeClient  client)
        {
            Console.WriteLine("  Client connected. ip=" + client.Client.Client.RemoteEndPoint.ToString());
            client.Bridge = new TcpClient();
            client.Bridge.Connect(ServerHost, ServerPort);
            var bridgeStream = client.Bridge.GetStream();
            bridgeStream.Write(Int32ToBytes(0), 0, 4);
            var buffer = new byte[4];
            ReadData(bridgeStream, buffer, 0, 4);
            var id = BytesToInt32(buffer);
            client.ID = id;
            Console.WriteLine("  Client ID=" + id.ToString());
            var clientStream = client.Client.GetStream();
            client.ReceiveBuffer = new byte[BufferSize];
            client.SendBuffer = new byte[BufferSize];
            Action receive=() =>
            {
                try
                {
                    // resend the last package
                    if (client.ReceiveSize != 0)
                    {
                        //Send the size of the data
                        bridgeStream.Write(Int32ToBytes(client.ReceiveSize), 0, 4);
                        //send data to server
                        bridgeStream.Write(client.ReceiveBuffer, 0, client.ReceiveSize);
                    }
                    while (true)
                    {
                        if (client.Reset)
                            return;
                        //reset
                        client.ReceiveBuffer = new byte[BufferSize];
                        client.ReceiveSize = 0;
                        //receive from client
                        client.ReceiveSize = clientStream.Read(client.ReceiveBuffer, 0, BufferSize);
                        //Send the size of the data
                        bridgeStream.Write(Int32ToBytes(client.ReceiveSize), 0, 4);
                        //send data to server
                        bridgeStream.Write(client.ReceiveBuffer, 0, client.ReceiveSize);

                        //reset
                        client.ReceiveBuffer = new byte[BufferSize];
                        client.ReceiveSize = 0;
                    }
                }
                catch (Exception ex)
                {
                     client.Reset = true;
                }
            };
            Action send = () =>
            {
                try
                {
                    while (true)
                    {
                        if (client.Reset)
                            return;
                        var sizeBuffer = new byte[4];
                        //reset
                        client.SendBuffer = new byte[BufferSize];
                        client.SendSize = 0;
                        //get the size of data
                        ReadData(bridgeStream, sizeBuffer, 0, 4);
                        client.SendSize = BytesToInt32(sizeBuffer);
                        //get data
                        ReadData(bridgeStream, client.SendBuffer, 0, client.SendSize);
                        //send the data to client
                        clientStream.Write(client.SendBuffer, 0, client.SendSize);

                        //reset
                        client.SendBuffer = new byte[BufferSize];
                        client.SendSize = 0;
                    }
                }
                catch (Exception ex)
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
                TryAgain:
                client.Bridge.Close();
                client.Bridge = new TcpClient();
                client.Bridge.Connect(ServerHost, ServerPort);
                bridgeStream = client.Bridge.GetStream();
                bridgeStream.Write(Int32ToBytes(client.ID), 0, 4);
                buffer = new byte[4];
                ReadData(bridgeStream, buffer, 0, 4);
                if(BytesToInt32(buffer) != 0)
                {
                    goto TryAgain;
                }
            }
        }
        static void InitCommands()
        {
            Commands["connect"] = (args) =>
            {
                var reg = new Regex(@"([a-zA-Z0-9.]+):([0-9]+)");
                var match = reg.Match(args);
                if (match == null || match.Groups.Count < 3)
                    throw new Exception("Params error.");
                var host = match.Groups[1].ToString();
                int port = Convert.ToInt32(match.Groups[2].ToString());
                Connect(host, port);
            };
            Commands["setup"] = (args) =>
            {
                int port = Convert.ToInt32(args);
                Setup(port);
            };
        }
        static void Excute(string cmd)
        {
            
            var command = "";
            int i = 0;
            for(i = 0; i < cmd.Length; i++)
            {
                if (cmd[i] == ' ')
                    break;
                command += cmd[i];
            }
            command = command.ToLower();
            var args = cmd.Substring(i + 1);
            if (!Commands.Keys.Contains(command))
                throw new Exception("Command not found.");
            Commands[command](args);
        }
        static void Main(string[] args)
        {
            InitCommands();
            //Setup(2222);
            //Connect("127.0.0.1", 3333);
            while (true)
            {
                var cmd = Console.ReadLine();
                try
                {
                    Excute(cmd);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error:" + ex.Message);
                    Console.ForegroundColor = ConsoleColor.Gray;
                }
            }
        }
    }
}
