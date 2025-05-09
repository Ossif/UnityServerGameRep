﻿using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace socketAPP
{
    class Program
    {
        public static string PrintByteArray(byte[] bytes, int offset = 0)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = offset; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("X2"));
                sb.Append(" ");
            }
            return sb.ToString();
        }
        public class ServerClient
        {
            public TcpClient tcp;
            public byte[] buffer;
            public int CountGetPacketData; //Количество уже полученных байт
            public int Remaining; //Количество запрошеных байт
            public NetworkStream stream;
            public ServerClient(TcpClient tcp)
            {
                this.tcp = tcp;
            }

            public string PlayerName;
            public float[] lastPos = new float[4];
            public bool authorized = false;
        }

        public int port = 6321;
        private static int HeaderSize = 6; //(uint16(2) - PacketID, Uint32(4) - PacketSize)
        private static int MaxDataSize = 256; //Максимальный размер пакета, который может обработать сервер
        private TcpListener server;
        private bool serverStarted;
        private Thread serverThread;
        static ConcurrentDictionary<ServerClient, object> clients = new ConcurrentDictionary<ServerClient, object>();
        private Queue<Tuple<ServerClient, PacketDecryptor>> messageQueue = new Queue<Tuple<ServerClient, PacketDecryptor>>(); //Packet queue
        public StreamWriter sw = null;

        public static void Main()
        {
            Program init = new Program();
            init.Init();
        }

        public void printf(string str)
        {
            if(sw != null)
            {
                sw.WriteLine(str);
                sw.Flush();
            }
        }
        // Start is called before the first frame update
        public async Task Init()
        {
            // Create a file to write to.
            sw = new StreamWriter(Path.Combine(System.IO.Directory.GetCurrentDirectory(), "ServerLogs.txt"));
            printf("SERVER начал работу.");
            try
            {


                server = new TcpListener(IPAddress.Any, port);
                server.Start();
                serverStarted = true;

                Console.WriteLine("SERVER начал работу.");
                serverThread = new Thread(new ThreadStart(QueueUpdate));
                serverThread.Start();
                await StartListening();
            }
            catch (Exception e)
            {
                Console.WriteLine("SERVER Socket error: " + e.Message);
            }
        }
        public async Task StartListening()
        {
            while (true)
            {
                ServerClient client = new ServerClient(await server.AcceptTcpClientAsync());
                clients.TryAdd(client, null);
                Console.WriteLine($"Client connected: {client.tcp.Client.RemoteEndPoint}");
                client.stream = client.tcp.GetStream();
                Packet packet = new Packet((int)WorldCommand.SMSG_OFFER_ENTER);
                packet.Write((int)0);
                await client.stream.WriteAsync(packet.GetBytes());

                client.Remaining = HeaderSize;
                client.CountGetPacketData = 0;
                client.buffer = new byte[client.Remaining];
                //client.stream.BeginRead(client.buffer, 0, client.Remaining, ReadHeaderCallback, new Tuple<ServerClient>(client));
                await client.stream.ReadAsync(client.buffer, 0, client.Remaining);
                ReadHeaderCallback(client.buffer, client);
            }
        }
        async void ReadHeaderCallback(byte[] buffer, ServerClient client)
        {
            try
            {
                int bytesRead = buffer.Length;

                if (bytesRead == 0)
                {
                    // Соединение было закрыто сервером
                    Console.WriteLine($"HEADER: Сервер разорвал соединение с {client.tcp.Client.RemoteEndPoint}");
                    client.stream.Close();
                    clients.TryRemove(client, out _);
                    return;
                }
                printf($"{PrintByteArray(client.buffer)}");
                if (bytesRead != client.Remaining) //Если количество байт которое мы получили не соответствует тому, которое мы запросили
                {
                    printf($"Ошибка сети, количество байт не соответствует запрошенному значению. Запрошено байт: {client.Remaining}, получено: {bytesRead}");
                    Console.WriteLine($"Ошибка сети, количество байт не соответствует запрошенному значению. Запрошено байт: {client.Remaining}, получено: {bytesRead}");
                    //Пытаемся запросить байты по новой
                    client.buffer = new byte[client.Remaining];
                    await client.stream.ReadAsync(client.buffer, 0, client.Remaining);
                    ReadHeaderCallback(client.buffer, client);
                    return;
                }

                int headerSize = (int)BitConverter.ToUInt32(client.buffer, 2); //Ищем длинну пакета

                if (headerSize > MaxDataSize || headerSize < 1) //Если количество байтов пакета больше или меньше разрешенного 
                {
                    printf($"Ошибка сети, получен пакет некорректной длинны. Длинна {headerSize}, байт код: {PrintByteArray(client.buffer)}");
                    Console.WriteLine($"Ошибка сети, получен пакет некорректной длинны. Длинна {headerSize}, байт код: {PrintByteArray(client.buffer)}");
                    client.buffer = new byte[client.Remaining];
                    await client.stream.ReadAsync(client.buffer, 0, client.Remaining);
                    ReadHeaderCallback(client.buffer, client);
                }

                client.CountGetPacketData += bytesRead;
                try
                {
                    client.Remaining = headerSize;
                    Array.Resize(ref client.buffer, client.CountGetPacketData + client.Remaining);
                    //client.stream.BeginRead(client.buffer, client.CountGetPacketData, client.Remaining, ReadDataCallback, new Tuple<ServerClient>(client));
                    await client.stream.ReadAsync(client.buffer, client.CountGetPacketData, client.Remaining);
                    ReadDataCallback(client.buffer, client);
                }
                catch (Exception ex)
                {
                    printf($"Fail to read header. Packet size: {headerSize}\nError: {ex}");
                
                    Console.WriteLine($"Fail to read header. Packet size: {headerSize}\nError: {ex}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                client.stream.Close();
                clients.TryRemove(client, out _);
            }
        }
        async void ReadDataCallback(byte[] buffer, ServerClient client)
        {
            try
            {
                int bytesRead = buffer.Length;

                if (bytesRead == 0)
                {
                    // Соединение было закрыто сервером
                    Console.WriteLine($"DATA: Сервер разорвал соединение с {client.tcp.Client.RemoteEndPoint}");
                    client.stream.Close();
                    clients.TryRemove(client, out _);
                    return;
                }
                printf($"{PrintByteArray(client.buffer)}");
                /*           if (bytesRead != client.Remaining) //Если количество байт которое мы получили не соответствует тому, которое мы запросили
                           {
                               Console.WriteLine($"Ошибка сети, количество байт не соответствует запрошенному значению. Запрошено байт: {client.Remaining}, получено: {bytesRead}");
                               //Пытаемся запросить байты по новой
                               client.stream.BeginRead(client.buffer, client.CountGetPacketData, client.Remaining, ReadDataCallback, new Tuple<ServerClient>(client));
                               return;
                           }*/

                PacketDecryptor packet = new PacketDecryptor(client.buffer);
                client.CountGetPacketData = 0;

                messageQueue.Enqueue(Tuple.Create(client, packet));

                client.Remaining = HeaderSize;
                client.buffer = new byte[client.Remaining];
                //client.stream.BeginRead(client.buffer, 0, client.Remaining, ReadHeaderCallback, new Tuple<ServerClient>(client));
                await client.stream.ReadAsync(client.buffer, 0, client.Remaining);
                ReadHeaderCallback(client.buffer, client);
                //Console.WriteLine("Packet get, find new");
                //
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                client.stream.Close();
                clients.TryRemove(client, out _);
            }
        }
        private void QueueUpdate()
        {
            while (true) //Да, я могу позволить себе бесконечный цикл, а ты?
            {
                ServerClient client = null;
                PacketDecryptor packet = null;
                lock (messageQueue)
                {
                    if (messageQueue.Count > 0)
                    {
                        Tuple<ServerClient, PacketDecryptor> packetTuple = messageQueue.Dequeue();
                        client = packetTuple.Item1;
                        packet = packetTuple.Item2;

                    }
                }

                if (packet != null) //Если нашли какой-то пакетик
                {
                    OnIncomingData(client, packet); //Отправляем его на обработку
                }
                else //Если нет
                {
                    Thread.Sleep(10); //Спим 10 мс
                }
            }
        }
        private void OnIncomingData(ServerClient c, PacketDecryptor packet)
        {
            //Логика обработчика данных
            int packetid = packet.GetPacketId();
            switch ((WorldCommand)packetid)
            {
                case (WorldCommand.CMSG_OFFER_ENTER_ANSWER): //Запрос на авторизацию клиента
                    {
                        int playerid = packet.ReadInt();
                        c.PlayerName = packet.ReadString();
                        Console.WriteLine($"SERVER: Игрок {c.PlayerName} подключился к серверу");
                        Console.WriteLine("SERVER: Начинаем игру!");
                        
                        Packet apacket = new Packet((int)WorldCommand.SMSG_START_GAME);
                        apacket.Write(1);
                        c.stream.WriteAsync(apacket.GetBytes());
                        break;
                    }
                case (WorldCommand.CMSG_PLAYER_LOGIN): //Запрос на вход в игровой мир клиента
                    {

                        c.authorized = true;
                        //int objModelId = packet.ReadInt();


                        c.lastPos[0] = packet.ReadFloat();
                        c.lastPos[1] = packet.ReadFloat();
                        c.lastPos[2] = packet.ReadFloat();

                        c.lastPos[3] = packet.ReadFloat();

                        Packet responcePacket = new Packet((int)WorldCommand.SMSG_PLAYER_LOGIN);
                        responcePacket.Write((string)c.tcp.Client.RemoteEndPoint.ToString());
                        responcePacket.Write((string)c.PlayerName);

                        responcePacket.Write((float)c.lastPos[0]);
                        responcePacket.Write((float)c.lastPos[1]);
                        responcePacket.Write((float)c.lastPos[2]);
                        responcePacket.Write((float)c.lastPos[3]);

                        foreach (ServerClient client in clients.Keys)//Отправляем всем игрокам позицию нового игрока
                        {
                            if (c.tcp == client.tcp) continue;
                            if (client.authorized == false) continue;
                            client.stream.WriteAsync(responcePacket.GetBytes());
                            Console.WriteLine($"Тестирование - {client.tcp.Client.RemoteEndPoint.ToString()}");
                        }

                        //
                        Packet playersPacket = new Packet((int)WorldCommand.SMSG_CREATE_PLAYERS);

                        int counter = 0;
                        foreach (ServerClient client in clients.Keys)//Отправляем всем игрокам позицию нового игрока
                        {
                            if (c.tcp == client.tcp) continue;
                            if (client.authorized == false) continue;
                            counter++;
                        }

                        playersPacket.Write((int)counter);

                        foreach (ServerClient client in clients.Keys)//Отправляем всем игрокам позицию нового игрока
                        {
                            if (c.tcp == client.tcp) continue;
                            if (client.authorized == false) continue;

                            playersPacket.Write((string)client.tcp.Client.RemoteEndPoint.ToString());
                            playersPacket.Write((string)client.PlayerName);
                            playersPacket.Write((float)client.lastPos[0]);
                            playersPacket.Write((float)client.lastPos[1]);
                            playersPacket.Write((float)client.lastPos[2]);
                            playersPacket.Write((float)client.lastPos[3]);
                        }

                        c.stream.WriteAsync(playersPacket.GetBytes());

                        break;
                    }
                case (WorldCommand.CMSG_OBJ_INFO): //Синхронизация объектов и игроков
                    {
                        Packet responcePacket = new Packet((int)WorldCommand.SMSG_OBJ_INFO);

                        responcePacket.Write((string)c.tcp.Client.RemoteEndPoint.ToString()); //Object ID
                        responcePacket.Write((int)packet.ReadInt()); //animid
                        byte before = packet.ReadByte();
                        responcePacket.Write((byte)before); //before

                        bool position = (before & 0b100) != 0;
                        bool rotation = (before & 0b010) != 0;
                        bool speed = (before & 0b001) != 0;

                        if (position)
                        {
                            //Console.WriteLine($"POS: {packet.ReadFloat()}; {packet.ReadFloat()}; {packet.ReadFloat()}");
                            responcePacket.Write((float)packet.ReadFloat());
                            responcePacket.Write((float)packet.ReadFloat());
                            responcePacket.Write((float)packet.ReadFloat());
                        }

                        if (rotation)
                        {
                            //Console.WriteLine($"ROT: {packet.ReadFloat()}; {packet.ReadFloat()}; {packet.ReadFloat()}");
                            responcePacket.Write((float)packet.ReadFloat());
                        }

                        if (speed)
                        {
                            //Console.WriteLine($"SPEED: {packet.ReadFloat()}; {packet.ReadFloat()}; {packet.ReadFloat()}");
                            responcePacket.Write((float)packet.ReadFloat());
                            responcePacket.Write((float)packet.ReadFloat());
                            responcePacket.Write((float)packet.ReadFloat());
                        }




                        foreach (ServerClient client in clients.Keys)
                        {
                            if (c == client) continue;
                            if (client.authorized == false) continue;

                            client.stream.WriteAsync(responcePacket.GetBytes());
                        }
                        //Console.WriteLine("POS: " +packet.ReadFloat() + "; " + packet.ReadFloat() + "; " + packet.ReadFloat() + "; ");
                        break;
                    }
                case (WorldCommand.CMSG_CREATE_BULLET): //Создание пули от клиента
                    {
                        Packet responcePacket = new Packet((int)WorldCommand.SMSG_CREATE_BULLET);

                        responcePacket.Write((string)c.tcp.Client.RemoteEndPoint.ToString());

                        //Позиция
                        responcePacket.Write((float)packet.ReadFloat());
                        responcePacket.Write((float)packet.ReadFloat());
                        responcePacket.Write((float)packet.ReadFloat());

                        //ротация
                        responcePacket.Write((float)packet.ReadFloat());
                        responcePacket.Write((float)packet.ReadFloat());
                        responcePacket.Write((float)packet.ReadFloat());

                        //Скорость
                        responcePacket.Write((float)packet.ReadFloat());
                        responcePacket.Write((float)packet.ReadFloat());
                        responcePacket.Write((float)packet.ReadFloat());

                        foreach (ServerClient client in clients.Keys)
                        {
                            if (c == client) continue;
                            if (client.authorized == false) continue;

                            client.stream.WriteAsync(responcePacket.GetBytes());
                        }

                        break;
                    }
            }
        }

    }
}

