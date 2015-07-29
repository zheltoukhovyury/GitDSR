using MongoDB.Bson;
using MongoDB.Driver;
using MvcApplication1.Models;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace MvcApplication1.Context
{
    public class DataContextRealiztion : IDataContextAbstract
    {
        MongoClient client;
        IMongoDatabase dataBase;
        //String RabbitMQAddr = ConfigurationManager.AppSettings["RabbitMqHost"];
        //String MongoDbConnectionString = ConfigurationManager.AppSettings["MongoDbConnectionString"];
        IMongoCollection<BsonDocument> collection;
        ConnectionFactory factory;
        List<IConnection> connection;

        public DataContextRealiztion(String RabbitMQAddr, String MongoDbConnectionString, String MongoDbDataBaseName, String MongoDbCollectionName)
        {
            //RabbitMq Init
            factory = new ConnectionFactory() { HostName = RabbitMQAddr };
            connection = new List<IConnection>();
            try
            {
                for(int i=0;i<10;i++)
                    connection.Add(factory.CreateConnection());

                

                using (IModel channel = connection.CreateModel())
                {
                    Console.WriteLine("[+] Connecting to RabbitMQ service at {0} OK", RabbitMQAddr);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[-] Connecting to RabbitMQ service at {0} failed!", RabbitMQAddr, ex.Message);
            }

            //Mongo Init
            client = new MongoClient(MongoDbConnectionString);
            dataBase = client.GetDatabase(MongoDbDataBaseName);
            collection = dataBase.GetCollection<BsonDocument>(MongoDbCollectionName);
            IMongoCollection<BsonDocument> testCollection = dataBase.GetCollection<BsonDocument>("DBConnectionTest");
            BsonDocument testCommand = new BsonDocument();
            testCommand.Add("test", "test");
            try
            {
                Task operation = new Task(() =>
                {
                    testCollection.InsertOneAsync(testCommand);
                });
                operation.RunSynchronously();

                Console.WriteLine("[+] Connecting to MongoDB with ConnectionString {0} OK", MongoDbConnectionString);
            }
            catch (Exception)
            {
                Console.WriteLine("[-] Connecting to MongoDB with ConnectionString {0} FAILED", MongoDbConnectionString);
            }
        }

        public void NewCommand(JObject newCommand)
        {
            JObject command = (JObject)newCommand.DeepClone();

            String json = command.ToString();
            String deviceId = command.GetValue("deviceId").ToString();
            BsonDocument doc = BsonDocument.Parse(json);
            Task operation = new Task(() =>
            {
                collection.InsertOneAsync(doc);
            });
            operation.RunSynchronously();

            if (!connection.IsOpen)
                connection = factory.CreateConnection();

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
            
            //объект в методе изменяется, и не является сериализуемым. копию сделать так просто не получится
        }

        public JObject GetCommand(String deviceId)
        {
            if (!connection.IsOpen)
                connection = factory.CreateConnection();

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
                    command.Remove("creationTime");
                    command.Remove("timeout");
                    command.Remove("expired");
                    command.Remove("delivered");
                    return command;
                }
                else
                    return null;

            }
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