using MongoDB.Bson;
using MongoDB.Driver;
using MvcApplication1.Models;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace MvcApplication1.Context
{





    public class RabbitMqContext : IDataContextAbstract
    {
        static public class RabbitMqContextFactory
        {
            static ConnectionFactory factory = new ConnectionFactory()
            {
                HostName = ConfigurationManager.AppSettings["RabbitMqHost"],
                Port = Int32.Parse(ConfigurationManager.AppSettings["RabbitMqPort"]),
                UserName = ConfigurationManager.AppSettings["RabbitMqUser"],
                Password = ConfigurationManager.AppSettings["RabbitMqPassword"],
                VirtualHost = ConfigurationManager.AppSettings["RabbitMqVirtualHost"]
            };
            static List<Connection> connectionPool = new List<Connection>();

            class Connection
            {
                public IConnection connection;
                public bool busy;
            }

            static public RabbitMqContext GetContext()
            {
                RabbitMqContext newContext = new RabbitMqContext();
                newContext.connection = GetRabbitConnection();
                return newContext;
            }


            //наверное в данном случае можно определить Finalize для типа RabbitMqContext и
            //делегат в RabbitMqContextFactory, который будут активаировать экземпляры RabbitMqContext когда 
            //до них доберется сборщик мусора, и в этом делегает будут освобождаться соединеия. ато CloseContext как-то совсем неудобно
            //еще можно поместить вызов в finally блоке 
            
            
            static public void CloseContext(RabbitMqContext context)
            {
                connectionPool.Find(conn => conn.connection == context.connection).busy = false;
            }


            static IConnection GetRabbitConnection()
            {
                lock (connectionPool)
                {
                    for (int i = 0; i < connectionPool.Count; i++)
                    {
                        if (connectionPool.ElementAt(i).busy == false && connectionPool.ElementAt(i).connection.IsOpen)
                        {
                            connectionPool.ElementAt(i).busy = true;
                            return connectionPool.ElementAt(i).connection;
                        }
                    }
                    Connection newConnection = new Connection();
                    newConnection.busy = true;
                    newConnection.connection = factory.CreateConnection();
                    connectionPool.Add(newConnection);
                    return newConnection.connection;
                }
            }

        }

        public IConnection connection;

        public void NewCommand(JObject newCommand)
        {
            JObject command = (JObject)newCommand.DeepClone();

            String json = command.ToString();
            String deviceId = command.GetValue("deviceId").ToString();

            using (IModel channel = connection.CreateModel())
            {
                channel.ExchangeDeclare("ex" + deviceId, ExchangeType.Direct);
                channel.QueueDeclare(deviceId, false, false, false, null);
                channel.QueueBind(deviceId, "ex" + deviceId, "", null);

                IBasicProperties props = channel.CreateBasicProperties();
                props.DeliveryMode = 2;

                Byte[] body = Encoding.UTF8.GetBytes(json);
                channel.BasicPublish("ex" + deviceId, "", props, body);
            }
        }

        public JObject GetCommand(String deviceId)
        {
            using (IModel channel = connection.CreateModel())
            {
                channel.ExchangeDeclare("ex" + deviceId, ExchangeType.Direct);
                channel.QueueDeclare(deviceId, false, false, false, null);
                channel.QueueBind(deviceId, "ex" + deviceId, "", null);

                BasicGetResult res = channel.BasicGet(deviceId, false);
                if (res != null)
                {
                    channel.BasicAck(res.DeliveryTag, false);
                    JObject command = JObject.Parse(Encoding.UTF8.GetString(res.Body));
                    return command;
                }
                else
                    return null;

            }
        }


        public List<JObject> GetHistory(String DevId)
        {
            return new List<JObject>();
        
        }
    }

    

    public class MongoDBContext : IDataContextAbstract
    {
        public class MongoContextFactory
        {
            static MongoClient client = new MongoClient(ConfigurationManager.AppSettings["MongoDbConnectionString"]);

            public static MongoDBContext GetContext()
            {
                MongoDBContext newContext = new MongoDBContext();
                newContext.dataBase = client.GetDatabase(ConfigurationManager.AppSettings["MongoDbDataBaseName"]);
                newContext.collection = newContext.dataBase.GetCollection<BsonDocument>(ConfigurationManager.AppSettings["MongoDbCollectionName"]);
                return newContext;
            }

        }

        public IMongoDatabase dataBase;
        public IMongoCollection<BsonDocument> collection;

        public void NewCommand(JObject newCommand)
        {
            JObject command = (JObject)newCommand.DeepClone();
            command.Add("creationTime", DateTime.Now);
            command.Add("delivered", false);

            String json = command.ToString();
            BsonDocument doc = BsonDocument.Parse(json);
            Task operation = new Task(() =>
            {
                collection.InsertOneAsync(doc);
            });
            operation.RunSynchronously();
        }

        public JObject GetCommand(String deviceId)
        {
            FilterDefinitionBuilder<BsonDocument> builder = Builders<BsonDocument>.Filter;
            FilterDefinition<BsonDocument> filter = builder.Eq("deviceId", deviceId) & builder.Eq("delivered", false);
            JObject result = null;
            Task operation = Task.Factory.StartNew(() =>
            {
                Task<IAsyncCursor<BsonDocument>> cursor = collection.FindAsync(filter);
                cursor.Result.MoveNextAsync();
                Object batch = cursor.Result.Current;
                IEnumerator<BsonDocument> enumerator = (batch as IEnumerable<BsonDocument>).GetEnumerator();
                enumerator.Reset();
                if(enumerator.MoveNext())
                {
                    BsonDocument command = enumerator.Current;
                    command.Remove("_id");
                    command.Remove("creationTime");
                    command.Remove("delivered");
                    result = JObject.Parse(command.ToString());
                }
            });
            operation.Wait();
            return result;
        }

        public List<JObject> GetHistory(String DevId)
        {
            List<JObject> history = new List<JObject>();
            FilterDefinitionBuilder<BsonDocument> builder = Builders<BsonDocument>.Filter;
            FilterDefinition<BsonDocument> filter = builder.Eq("deviceId", DevId);
            Task operation = Task.Factory.StartNew(() =>
            {

                Task<IAsyncCursor<BsonDocument>> cursor = collection.FindAsync(filter);
                cursor.Result.MoveNextAsync();
                Object batch = cursor.Result.Current;
                IEnumerator <BsonDocument> enumerator = (batch as IEnumerable<BsonDocument>).GetEnumerator();
                enumerator.Reset();
                while (enumerator.MoveNext())
                {
                    BsonDocument command = enumerator.Current;
                    command.Remove("_id");
                    command.Remove("expired");
                    history.Add(JObject.Parse(command.ToJson().ToString()));
                }
            });
            operation.Wait();
            return history;
        }

    }

    public class FakeContext : IDataContextAbstract
    {
        JObject obj = null;

        public void NewCommand(JObject command)
        {
            obj = command;
        }

        public JObject GetCommand(String deviceId)
        {
            JObject retObj = obj;
            obj = null;
            return retObj;
        }
        public List<JObject> GetHistory(String deviceId)
        {
            return null;
        }
    }


}
