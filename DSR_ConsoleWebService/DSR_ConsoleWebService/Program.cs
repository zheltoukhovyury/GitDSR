﻿using System.IO;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Permissions;
using System.Web;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Net;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using System.Threading.Tasks;


namespace DSR_ConsoleWebService
{

    
    public class HTTPRequest
    {
        public String method;
        public int contentLenght;
        public List<String> headers; 
        public Byte[] content;

        public HTTPRequest()
        {
            headers = new List<String>();
        }
    }
    
    

    public class HTTPServer
    {
        TcpListener listener;
        Thread thread;
        List<ClientConnection> clientList = new List<ClientConnection>();


        public delegate void NewHttpRequest(HTTPRequest request, ClientConnection client);
        public event NewHttpRequest OnNewHttpRequest; 
        
        public HTTPServer(int port = 800)
        {
            thread = new Thread(new ThreadStart(ServerRun));
            try
            {
                listener = new TcpListener(port);
                listener.Start();
                Console.WriteLine("[+] Start Listening TCP port {0}", port);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[-] Cann't start listening port {0}. {1}", port,ex.Message);
            }

            thread.Start();
        }

        public struct ClientConnection{
            public TcpClient socket{get;set;}
            public Byte[] receivedData{get;set;}
            public int receivedLen{get;set;}
            public HTTPRequest requestOnReceiving;
            public int contentLenght;
            public bool receivingContent;
        }

        //вызов метода после полной обработки поступившего запроса. отправляет ответ и закрывает соединение
        public async void OnHttpResponce(HTTPRequest responce, ClientConnection client)
        {
            if (responce == null)
            {
                client.socket.Close();
                clientList.Remove(client);
                return;
            }

            foreach (String header in responce.headers)
                await client.socket.GetStream().WriteAsync(Encoding.UTF8.GetBytes(header), 0, header.Length);
            await client.socket.GetStream().WriteAsync(Encoding.UTF8.GetBytes("\r\n"), 0, 2);
            if(responce.content != null)
                await client.socket.GetStream().WriteAsync(responce.content, 0, responce.content.Length);
            await client.socket.GetStream().WriteAsync(Encoding.UTF8.GetBytes("\r\n"), 0, 2);
            client.socket.Close();
            clientList.Remove(client);
        }

        void ServerRun()
        {
            while(true)
            {
                Thread.Sleep(5);
                if(listener.Pending())
                {
                    TcpClient newClientSocket = listener.AcceptTcpClient();
                    ClientConnection newClient = new HTTPServer.ClientConnection();
                    newClient.socket = newClientSocket;
                    newClient.receivedData = new byte[4096];
                    newClient.receivedLen = 0;
                    newClient.contentLenght = 0;
                    newClient.receivingContent = false;
                    clientList.Add(newClient);

                    Console.WriteLine("[+] Accepted new Client connection {0}. client number {1}", newClient.socket.Client.LocalEndPoint, clientList.IndexOf(newClient));
                }
                for(int i = 0; i < clientList.Count; i++)
                {
                    ClientConnection client = clientList[i];

                    while (client.socket.Connected == true && client.socket.Available != 0)
                    {
                        if(client.receivingContent == false)
                            client.socket.GetStream().Read(client.receivedData, client.receivedLen++, 1);
                        else
                        {
                            client.socket.GetStream().Read(client.requestOnReceiving.content, client.requestOnReceiving.contentLenght ++, 1);
                            client.contentLenght --;
                            if(client.contentLenght == 0)
                            {
                                if (OnNewHttpRequest != null)
                                {
                                    Byte[] body = new Byte[4096];
                                    int len=0;
                                    OnNewHttpRequest.Invoke(client.requestOnReceiving,client);
                                }
                                client.requestOnReceiving = null;
                                client.receivingContent = false;
                                break;
                            }
                        }
                        

                        if(client.receivedLen >= 2 && Encoding.UTF8.GetString(client.receivedData,client.receivedLen - 2,2) == "\r\n")
                        {
                            String reqStr = Encoding.UTF8.GetString(client.receivedData,0,client.receivedLen);
                            
                            if(client.requestOnReceiving != null && client.requestOnReceiving.headers != null && client.receivedLen == 2)
                            {
                                //end of request headers
                                if(client.contentLenght != 0)
                                {
                                    client.receivedLen = 0;
                                    client.receivingContent = true;
                                }
                                else
                                {
                                    if (OnNewHttpRequest != null)
                                    {
                                        OnNewHttpRequest.Invoke(client.requestOnReceiving,client);

                                    }
                                
                                    client.requestOnReceiving = null;
                                    client.receivingContent = false;
                                    break;
                                }
                            }

                            if(reqStr.StartsWith("GET ") || reqStr.StartsWith("POST "))
                            {
                                //начало нового http запроса
                                client.requestOnReceiving = new HTTPRequest();
                                client.requestOnReceiving.headers = new List<string>();
                            }
                            if(reqStr.StartsWith("Content-Length:"))
                            {
                                int ind = reqStr.IndexOfAny("0123456789".ToCharArray());

                                if(int.TryParse(reqStr.Substring(ind),out client.contentLenght))
                                {
                                    client.requestOnReceiving.content = new byte[client.contentLenght];
                                }
                                
                            }
                            client.requestOnReceiving.headers.Add(reqStr);
                            client.receivedLen = 0;
                            break;
                        }
                    }
                    clientList[i] = client;
                }
            }
        }
    }

    [Serializable]
    struct DSRCommand
    {
        [JsonProperty("deviceId")]
        public String deviceId;

        [Serializable]     
        public struct Command
        {
            public String name;
            public String value;
        };
        [JsonProperty("command")]
        public Command[] command;

        DateTime creationTime;
        int timeoout;
        bool expired;
        bool delivered;
    }



    class Controller
    {
        public Controller(int TCPListeningPort = 800, String RabbitMQAddr = "localhost", String MongoDbConnectionString = "mongodb://localhost:27017")
        {
            controllerThread = new Thread(new ThreadStart(ControllerRunMethod));
            controllerThread.Start();
            this.TCPListeningPort = TCPListeningPort;
            this.RabbitMQAddr = RabbitMQAddr;
            this.MongoDbConnectionString = MongoDbConnectionString;
        }

        int TCPListeningPort;
        String RabbitMQAddr;
        String MongoDbConnectionString;
        MongoClient client = null;
        IMongoDatabase dataBase = null;
        BsonDocument document = null;
        Thread controllerThread;

        public delegate void ResponceForClient(HTTPRequest request, HTTPServer.ClientConnection client);
        public event ResponceForClient onResponceForClient;

        public delegate void NewCommand(DSRCommand command);
        public event NewCommand onNewCommand;

        async void ControllerRunMethod()
        {

            //RabbitMq Init
            var factory = new ConnectionFactory() { HostName = RabbitMQAddr };
            try
            {
                using (var connection = factory.CreateConnection())
                using (var channel = connection.CreateModel())
                {
                    Console.WriteLine("[+] Connecting to RabbitMQ service at {0} OK", RabbitMQAddr);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[-] Connecting to RabbitMQ service at {0} failed!", RabbitMQAddr, ex.Message);
            }

            //Http SErve Init
            HTTPServer server = new HTTPServer(TCPListeningPort);


            //Mongo Init
            client = new MongoClient(MongoDbConnectionString);
            dataBase = client.GetDatabase("DSR_Commands");
            var collection = dataBase.GetCollection<BsonDocument>("DSR_Commands");
            var testCollection = dataBase.GetCollection<BsonDocument>("DBConnectionTest");
            BsonDocument testCommand = new BsonDocument();
         

            testCommand.Add("test", "test");
            try
            {
                await testCollection.InsertOneAsync(testCommand);
                Console.WriteLine("[+] Connecting to MongoDB with ConnectionString {0} OK", MongoDbConnectionString);
            }
            catch (Exception)
            {
                Console.WriteLine("[-] Connecting to MongoDB with ConnectionString {0} FAILED", MongoDbConnectionString);
            }
            
            onResponceForClient += server.OnHttpResponce;
            server.OnNewHttpRequest += async delegate(HTTPRequest request, HTTPServer.ClientConnection clientConnection)
            {

                String[] substrings = request.headers[0].Split(new String[] { "/", " " }, StringSplitOptions.RemoveEmptyEntries);

                //запрос на получени веб-интерфеса, возврщаеи html файл
                if (substrings[0] == "GET" && substrings[1] == "HTTP")
                {
                    Console.WriteLine("[+] Request Accepted. GET WebPAge Interface ");
                    if (onResponceForClient != null)
                    {
                        HTTPRequest responce = new HTTPRequest();
                        String tr = Properties.Resources.WebInterface;
                        Byte[] content = Encoding.UTF8.GetBytes(tr);
                        String contentLenhthHeader = String.Format("Content-Length: {0}\r\n", content.Length);
                        String[] headers = {
                        "HTTP/1.1 200 OK\r\n",
                        "Date: Wed, 11 Feb 2009 11:20:59 GMT\r\n",
                        "Server: Apache\r\n",
                        "X-Powered-By: PHP/5.2.4-2ubuntu5wm1\r\n",
                        "Last-Modified: Wed, 11 Feb 2009 11:20:59 GMT\r\n",
                        "Content-Language: ru\r\n",
                        "Content-Type: text/html; charset=utf-8",
                        contentLenhthHeader,
                        "Connection: close\r\n"};
                        foreach (String header in headers)
                            responce.headers.Add(header);

                        responce.content = content;
                        onResponceForClient.Invoke(responce, clientConnection);
                    }
                }
                //POST запрос из веб-интерфеса на создание новой команды
                else if (substrings[0] == "POST" && substrings[1] == "newCommand")
                {
                    String argumentString = Encoding.UTF8.GetString(request.content);

                    Console.WriteLine("[+] Request Accepted. POST WebPAge Interface new Command");

                    JObject newCommand = JObject.Parse(argumentString);
                    
                    newCommand.Add("creationTime", DateTime.Now);
                    newCommand.Add("timeout", 60);
                    newCommand.Add("expired", false);
                    newCommand.Add("delivered", false);


                    Console.WriteLine("[+] Request Accepted. POST deviceId = {0}", newCommand.GetValue("deviceId"));
                    Console.WriteLine("          Command Details :");

                    //Console.WriteLine("                  name = {0} value = {1}", details.name, details.value);


                    BsonDocument doc = BsonDocument.Parse(newCommand.ToString());

                    await collection.InsertOneAsync(doc);

                    if (onNewCommand != null)
                        onNewCommand.Invoke(new DSRCommand() { deviceId = newCommand.GetValue("deviceId").ToString() });

                    if (onResponceForClient != null)
                    {
                        HTTPRequest responce = new HTTPRequest();
                        String[] headers = {
                        "HTTP/1.1 200 OK\r\n",
                        "Date: Wed, 11 Feb 2009 11:20:59 GMT\r\n",
                        "Server: Apache\r\n",
                        "X-Powered-By: PHP/5.2.4-2ubuntu5wm1\r\n",
                        "Last-Modified: Wed, 11 Feb 2009 11:20:59 GMT\r\n",
                        "Content-Language: ru\r\n",
                        "Content-Type: text/html; charset=utf-8",
                        "Connection: close\r\n"};
                        foreach (String header in headers)
                            responce.headers.Add(header);
                        onResponceForClient.Invoke(responce, clientConnection);
                    }

                }
                // запрос из веб-интерфеса лога команд для девайса 
                else if (substrings[0] == "GET" && substrings[1].StartsWith("viewLog?devId="))
                {
                    String argumentString;

                    if (true)
                    {
                        argumentString = substrings[1].Remove(0, "viewLog?devId=".Length);
                        Console.WriteLine("[+] Request Accepted. POST View Log deviceId = {0} ", argumentString);
                        var builder = Builders<BsonDocument>.Filter;
                        var filter = builder.Eq("deviceId", argumentString);

                        var cursor = await collection.FindAsync(filter);
                        StringBuilder resultBuilder = new StringBuilder();
                        while (await cursor.MoveNextAsync())
                        {
                            Object batch = cursor.Current;
                            var enumerator = (batch as IEnumerable<BsonDocument>).GetEnumerator();
                            enumerator.Reset();
                            int count = 0;
                            while (enumerator.MoveNext())
                            {
                                BsonDocument command = enumerator.Current;
                                command.Remove("_id");
                                command.Remove("expired");
                                if (resultBuilder.Length == 0) resultBuilder.Append("{\"log\":[");

                                if(count != 0)
                                    resultBuilder.Append(",");
                                resultBuilder.Append(command.ToJson().ToString());
                                count++;
                            }
                            if(count != 0)
                                resultBuilder.Append("]}");
                        }
                        Byte[] content = Encoding.UTF8.GetBytes(resultBuilder.ToString());
                        HTTPRequest responce = new HTTPRequest();
                        String contentLenhthHeader = String.Format("Content-Length: {0}\r\n", content.Length);
                        String[] headers = {
                                    "HTTP/1.1 200 OK\r\n",
                                    "Date: Wed, 11 Feb 2009 11:20:59 GMT\r\n",
                                    "Server: Apache\r\n",
                                    "X-Powered-By: PHP/5.2.4-2ubuntu5wm1\r\n",
                                    "Last-Modified: Wed, 11 Feb 2009 11:20:59 GMT\r\n",
                                    "Content-Language: ru\r\n",
                                    "Content-Type: application/json;\r\n",
                                    contentLenhthHeader,
                                    "Connection: close\r\n"};
                        foreach (String header in headers)
                            responce.headers.Add(header);

                        String test = resultBuilder.ToString();

                        Object and = JsonConvert.DeserializeObject(test);

                        responce.content = content;
                        onResponceForClient.Invoke(responce, clientConnection);

                    }
                }
                // запрос от клиентского девайса команды
                else if (substrings[0] == "GET" && substrings[1] == "command")
                {
                    Console.WriteLine("[+] Request Accepted. GET deviceId = {0}  timeout = {1} ", substrings[2], substrings[3]);
                    var builder = Builders<BsonDocument>.Filter;
                    var filter = builder.Eq("deviceId", substrings[2]) & builder.Eq("expired", false) & builder.Eq("delivered", false);// &builder.Gte("creationTime", DateTime.Now.AddSeconds(-60000));

                    var update = Builders<BsonDocument>.Update;
                    var updater = update.Set("delivered", true);

                    bool polling = true;
                    bool checkCollection = true;
                    Timer pollingTimer = null;
                    int timeout = 10000;
                    if (!int.TryParse(substrings[3], out timeout))
                        timeout = 60000;
                    else
                        timeout *= 1000;

                    pollingTimer = new System.Threading.Timer(delegate(Object state)
                    {
                        if (pollingTimer != null)
                        {
                            pollingTimer.Change(-1, Timeout.Infinite);
                            pollingTimer.Dispose();
                            pollingTimer = null;
                            polling = false;
                            onResponceForClient.Invoke(null, clientConnection);
                        }
                    }, null, (Int32)timeout, timeout);


                    while (polling)
                    {
                        if (checkCollection == false)
                            continue;
                        var cursor = await collection.FindAsync(filter);
                        if(await cursor.MoveNextAsync())
                        {

                            Object batch = cursor.Current;
                            var enumerator = (batch as IEnumerable<BsonDocument>).GetEnumerator();
                            enumerator.Reset();
                            if (enumerator.MoveNext())
                            {
                                if (onResponceForClient != null)
                                {
                                    HTTPRequest responce = new HTTPRequest();
                                    BsonDocument command = enumerator.Current;
                                    BsonDocument details = command.GetValue("command").AsBsonDocument;
                                    command.Remove("_id");
                                    command.Remove("expired");
                                    command.Remove("creationTime");
                                    command.Remove("delivered");
                                    command.Remove("timeout");

                                    Byte[] content = Encoding.UTF8.GetBytes(command.ToJson().ToString());
                                    String contentLenhthHeader = String.Format("Content-Length: {0}\r\n", content.Length);
                                    String[] headers = {
                                    "HTTP/1.1 200 OK\r\n",
                                    "Date: Wed, 11 Feb 2009 11:20:59 GMT\r\n",
                                    "Server: Apache\r\n",
                                    "X-Powered-By: PHP/5.2.4-2ubuntu5wm1\r\n",
                                    "Last-Modified: Wed, 11 Feb 2009 11:20:59 GMT\r\n",
                                    "Content-Language: ru\r\n",
                                    "Content-Type: application/json;\r\n",
                                    contentLenhthHeader,
                                    "Connection: close\r\n"};
                                    foreach (String header in headers)
                                        responce.headers.Add(header);

                                    responce.content = content;
                                    onResponceForClient.Invoke(responce, clientConnection);

                                    await collection.UpdateOneAsync(filter, updater);
                                    polling = false;
                                    pollingTimer.Change(-1, Timeout.Infinite);
                                    pollingTimer.Dispose();
                                    pollingTimer = null;

                                }
                            }
                            else
                            {
                                //начало long polling
                                onNewCommand += delegate(DSRCommand command)
                                {
                                    if (command.deviceId == substrings[2]) // Captain ? :)
                                    {
                                        checkCollection = true;
                                        //check collection once
                                    }
                                };
                            }
                        }
                        checkCollection = false;
                    }
                }
                // POST запрос от клиента с новой командой
                else if (substrings[0] == "POST")
                {
                    String contentString = Encoding.UTF8.GetString(request.content);

                    JObject newCommand = JObject.Parse(contentString);
                    newCommand.Add("creationTime", DateTime.Now);
                    newCommand.Add("timeout", 60);
                    newCommand.Add("expired", false);
                    newCommand.Add("delivered", false);


                    Console.WriteLine("[+] Request Accepted. POST deviceId = {0}", newCommand.GetValue("deviceId"));
                    Console.WriteLine("     Command Details :");
                    Console.WriteLine("         deviceID : {0}", newCommand.GetValue("command"));
                    //Console.WriteLine("                  name = {0} value = {1}", details.name, details.value);


                    BsonDocument doc = BsonDocument.Parse(newCommand.ToString());

                    await collection.InsertOneAsync(doc);

                    if (onNewCommand != null)
                        onNewCommand.Invoke(new DSRCommand() { deviceId = newCommand.GetValue("deviceId").ToString() });

                }
            };

            while (true)
            {
                Thread.Sleep(100);
                String command = Console.ReadLine();

                switch (command)
                {
                    case "get":
                        {
                            using (var connection = factory.CreateConnection())
                            {
                                using (var channel = connection.CreateModel())
                                {
                                    channel.QueueDeclare("hello", false, false, false, null);

                                    var consumer = new QueueingBasicConsumer(channel);
                                    channel.BasicConsume("hello", true, consumer);

                                    var ea = new BasicDeliverEventArgs();
                                    if (consumer.Queue.Dequeue(1000, out ea))
                                    {
                                        var body = ea.Body;
                                        BinaryFormatter formatter = new BinaryFormatter();
                                        Stream stream = new MemoryStream(ea.Body);
                                        DSRCommand gotCommand = (DSRCommand)formatter.Deserialize(stream);

                                        stream.Read(body, 0, (int)stream.Length);
                                    }
                                }
                            }

                            break;
                        }
                }
            }
        
        }
    }










    class Program
    {
        public static void Main(string[] args)
        {
            int port = 800;
            String RabbitMQAddr;
            String MongoDbConnectionString;

            if (args.Length < 1 || !int.TryParse(args[0], out port))
            {
                Console.WriteLine("[-] TCP Port not selected. starting on port 800");
                port = 800;
            }

            if (args.Length < 2)
            {
                RabbitMQAddr = "localhost";
                Console.WriteLine("[-] RabbitMQ Address not selected. connecting to {0}",RabbitMQAddr);
                
            }
            else
                RabbitMQAddr = args[1];
            if (args.Length < 3)
            {
                MongoDbConnectionString = "mongodb://localhost:27017";
                Console.WriteLine("[-] MongoDB connectionString not selected. connecting with {0}", MongoDbConnectionString);
            }
            else
                MongoDbConnectionString = args[2];

            Controller mainController = new Controller(TCPListeningPort: port, RabbitMQAddr: RabbitMQAddr, MongoDbConnectionString: MongoDbConnectionString);
        }
    }
}
